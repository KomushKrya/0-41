#!/usr/bin/env python3
"""Собирает .md из content/raw в JSON по локали, который читает рантайм Godot.

Запуск из корня репозитория:
    python content/engine/converter/build.py
    python content/engine/converter/build.py --include-drafts

Зависимостей нет — фронтматтер разбирается вручную, потому что схема
намеренно узкая (см. content/raw/_system/Syntax/README.md): скаляры и
простые списки. Всё, что в неё не укладывается, считается ошибкой сборки,
а не молча пропускается.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# Папка контента -> значение поля type во фронтматтере.
FOLDER_TYPES = {
    "calls": "call",
    "cutscenes": "cutscene",
    "mission_events": "mission_event",
    "creatures": "creature",
    "shift_notes": "shift_note",
    "reports": "report",
}

# Типы, которые показываются виджетом нижнего текста: у них кусок должен влезать
# примерно в две строки. Остальные (энциклопедия, отчёты) рендерятся на своих
# экранах, где места больше, и под это ограничение не попадают.
BOTTOM_TEXT_TYPES = ("call", "cutscene")
DEFAULT_MAX_CHUNK_CHARS = 150

COMMENT_RE = re.compile(r"<!--.*?-->", re.DOTALL)
# Заметки для разработчиков: блок %% dev %% ... %% /dev %% и инлайн %% dev: ... %%.
DEV_BLOCK_RE = re.compile(r"%%\s*dev\s*%%.*?%%\s*/\s*dev\s*%%", re.DOTALL)
DEV_INLINE_RE = re.compile(r"[ \t]*%%\s*dev:[^\n]*?%%")
DEV_LEFTOVER_RE = re.compile(r"%%\s*/?\s*dev\b")
# Служебная шапка звонка — [ЗВОНОК ПЕРЕНАПРАВЛЕН ...], [НА ЛИНИИ СТАРИК] и т.п.
# В куски попадает отдельным kind, чтобы виджет рендерил её не как реплику.
CALL_META_RE = re.compile(r"^%%\s*call_meta:\s*(.+?)\s*%%$")
CHUNK_KIND_TEXT = "text"
CHUNK_KIND_CALL_META = "call_meta"
REVEAL_OPEN_RE = re.compile(r"^%%\s*reveal:\s*([^\s%]+)\s*%%$")
REVEAL_CLOSE_RE = re.compile(r"^%%\s*/\s*reveal\s*%%$")
OPTION_RE = re.compile(r"^##\s*Вариант:\s*(.+?)\s*$")
OPTION_META_RE = re.compile(r"^(requirement_modifier|canon)\s*:\s*(.*)$")
LINK_RE = re.compile(r"\[\[([a-z_]+):([a-z0-9_]+)\]\]")
LIST_ITEM_RE = re.compile(r"^\s+-\s*(.*)$")
SCALAR_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$")


class BuildFailed(Exception):
    pass


def split_frontmatter(text: str, source: Path) -> tuple[list[str], list[str]]:
    lines = text.replace("\r\n", "\n").split("\n")
    if not lines or lines[0].strip() != "---":
        raise BuildFailed(f"{source}: файл должен начинаться с фронтматтера (---)")

    for index in range(1, len(lines)):
        if lines[index].strip() == "---":
            return lines[1:index], lines[index + 1 :]

    raise BuildFailed(f"{source}: фронтматтер не закрыт (нет второго ---)")


def parse_frontmatter(lines: list[str], source: Path) -> dict:
    data: dict = {}
    current_list_key: str | None = None

    for line in lines:
        if not line.strip():
            continue

        list_item = LIST_ITEM_RE.match(line)
        if list_item:
            if current_list_key is None:
                raise BuildFailed(f"{source}: элемент списка вне поля: {line!r}")
            value = list_item.group(1).strip()
            if value:
                data[current_list_key].append(value)
            continue

        scalar = SCALAR_RE.match(line)
        if not scalar:
            raise BuildFailed(f"{source}: непонятная строка фронтматтера: {line!r}")

        key, value = scalar.group(1), scalar.group(2).strip()
        if value:
            data[key] = value
            current_list_key = None
        else:
            data[key] = []
            current_list_key = key

    return data


def strip_dev_notes(text: str, source: Path) -> str:
    """Выкидывает dev-заметки до разбора кусков, чтобы они не дошли до JSON.

    Обсидиан и так не показывает %%-комментарии, так что автор видит заметку
    только в исходнике. Инлайн-форма съедает пробел перед собой, иначе внутри
    абзаца оставалась бы двойная пробельная дырка.
    """
    text = DEV_BLOCK_RE.sub("", text)
    text = DEV_INLINE_RE.sub("", text)

    if DEV_LEFTOVER_RE.search(text):
        raise BuildFailed(
            f"{source}: незакрытый dev-тег — нужно либо %% dev: ... %% в одну строку, "
            "либо %% dev %% ... %% /dev %%"
        )

    return text


def parse_chunks(lines: list[str], source: Path) -> list[dict]:
    """Абзац = один кусок текста (клик). %% reveal %% помечает кусок условным."""
    chunks: list[dict] = []
    buffer: list[str] = []
    reveal: str | None = None

    def flush() -> None:
        nonlocal buffer
        text = " ".join(part.strip() for part in buffer).strip()
        if text:
            chunks.append({"text": text, "kind": CHUNK_KIND_TEXT, "reveal": reveal})
        buffer = []

    for raw_line in lines:
        line = raw_line.strip()

        meta_match = CALL_META_RE.match(line)
        if meta_match:
            flush()
            chunks.append(
                {"text": meta_match.group(1), "kind": CHUNK_KIND_CALL_META, "reveal": reveal}
            )
            continue

        open_match = REVEAL_OPEN_RE.match(line)
        if open_match:
            if reveal is not None:
                raise BuildFailed(f"{source}: вложенный %% reveal %% не поддерживается")
            flush()
            reveal = open_match.group(1)
            continue

        if REVEAL_CLOSE_RE.match(line):
            if reveal is None:
                raise BuildFailed(f"{source}: %% /reveal %% без открывающего тега")
            flush()
            reveal = None
            continue

        if not line:
            flush()
            continue

        buffer.append(line)

    if reveal is not None:
        raise BuildFailed(f"{source}: не закрыт блок %% reveal: {reveal} %%")

    flush()
    return chunks


def split_options(lines: list[str]) -> tuple[list[str], list[tuple[str, list[str]]]]:
    intro: list[str] = []
    options: list[tuple[str, list[str]]] = []

    for line in lines:
        option = OPTION_RE.match(line.strip())
        if option:
            options.append((option.group(1), []))
            continue

        if options:
            options[-1][1].append(line)
        else:
            intro.append(line)

    return intro, options


def parse_option(name: str, lines: list[str], source: Path) -> dict:
    meta: dict = {}
    body: list[str] = []

    for index, raw_line in enumerate(lines):
        line = raw_line.strip()
        if not line and not body:
            continue

        match = OPTION_META_RE.match(line)
        if match and not body:
            meta[match.group(1)] = match.group(2).strip()
            continue

        body.extend(lines[index:])
        break

    if "canon" not in meta:
        raise BuildFailed(f"{source}: у варианта {name!r} нет поля canon")

    modifier_text = meta.get("requirement_modifier", "0")
    try:
        modifier = int(modifier_text)
    except ValueError:
        raise BuildFailed(
            f"{source}: requirement_modifier у варианта {name!r} должен быть целым, а не {modifier_text!r}"
        ) from None

    return {
        "name": name,
        "requirement_modifier": modifier,
        "canon": meta["canon"],
        "chunks": parse_chunks(body, source),
    }


def all_chunks(entry: dict) -> list[dict]:
    """Куски вводной плюс куски всех вариантов — у mission_event текст есть и там."""
    chunks = list(entry["chunks"])
    for option in entry.get("options", []):
        chunks.extend(option["chunks"])
    return chunks


def parse_file(path: Path, expected_type: str, repo_root: Path) -> dict:
    try:
        raw_text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError as error:
        raise BuildFailed(f"{path}: файл должен быть в UTF-8 ({error})") from None

    frontmatter_lines, body_lines = split_frontmatter(raw_text, path)
    frontmatter = parse_frontmatter(frontmatter_lines, path)

    for required in ("id", "type", "status"):
        if required not in frontmatter or not frontmatter[required]:
            raise BuildFailed(f"{path}: во фронтматтере нет поля {required}")

    if frontmatter["type"] != expected_type:
        raise BuildFailed(
            f"{path}: type={frontmatter['type']!r}, но файл лежит в папке типа {expected_type!r}"
        )

    body_text = COMMENT_RE.sub("", "\n".join(body_lines))
    body_lines = strip_dev_notes(body_text, path).split("\n")

    entry: dict = {
        "id": frontmatter["id"],
        "type": frontmatter["type"],
        "requirements": frontmatter.get("requirements", []),
        "properties": frontmatter.get("properties", []),
    }

    if expected_type == "mission_event":
        intro_lines, option_blocks = split_options(body_lines)
        entry["chunks"] = parse_chunks(intro_lines, path)
        entry["options"] = [parse_option(name, lines, path) for name, lines in option_blocks]
        if not entry["options"]:
            raise BuildFailed(f"{path}: у mission_event должен быть хотя бы один вариант решения")
    else:
        entry["chunks"] = parse_chunks(body_lines, path)

    if expected_type not in BOTTOM_TEXT_TYPES:
        for chunk in all_chunks(entry):
            if chunk["kind"] == CHUNK_KIND_CALL_META:
                raise BuildFailed(
                    f"{path}: %% call_meta %% рендерится только виджетом нижнего текста, "
                    f"а тип {expected_type!r} показывается на своём экране"
                )

    if expected_type == "creature":
        entry["name"] = frontmatter.get("name", "")
        revealed = [chunk["reveal"] for chunk in entry["chunks"] if chunk["reveal"]]
        declared = set(entry["properties"])
        if declared != set(revealed):
            raise BuildFailed(
                f"{path}: properties={sorted(declared)} не совпадает с %% reveal %% в теле {sorted(set(revealed))}"
            )

    if expected_type == "shift_note":
        entry["day"] = int(frontmatter.get("day", 0))

    if expected_type == "report":
        entry["outcome"] = frontmatter.get("outcome", "")

    entry["_status"] = frontmatter["status"]
    entry["_source"] = path.relative_to(repo_root).as_posix()
    return entry


def collect_entries(raw_root: Path, repo_root: Path) -> list[dict]:
    entries: list[dict] = []
    errors: list[str] = []

    for folder, content_type in sorted(FOLDER_TYPES.items()):
        folder_path = raw_root / folder
        if not folder_path.is_dir():
            continue

        for path in sorted(folder_path.rglob("*.md")):
            try:
                entries.append(parse_file(path, content_type, repo_root))
            except BuildFailed as error:
                errors.append(str(error))

    if errors:
        raise BuildFailed("\n".join(errors))

    return entries


def validate(entries: list[dict]) -> None:
    errors: list[str] = []
    by_id: dict[str, str] = {}

    for entry in entries:
        entry_id = entry["id"]
        if entry_id in by_id:
            errors.append(f"дублирующийся id {entry_id!r}: {by_id[entry_id]} и {entry['_source']}")
        else:
            by_id[entry_id] = entry["_source"]

    known_ids = set(by_id)
    for entry in entries:
        for text in (chunk["text"] for chunk in all_chunks(entry)):
            for link_type, link_id in LINK_RE.findall(text):
                if link_id not in known_ids:
                    errors.append(f"{entry['_source']}: ссылка [[{link_type}:{link_id}]] ведёт в никуда")

    if errors:
        raise BuildFailed("\n".join(errors))


def write_locale(entries: list[dict], out_root: Path, locale: str) -> list[str]:
    """Папки внутри локали повторяют структуру content/raw."""
    out_dir = out_root / locale

    by_type: dict[str, dict] = {content_type: {} for content_type in FOLDER_TYPES.values()}
    for entry in entries:
        public_entry = {key: value for key, value in entry.items() if not key.startswith("_")}
        by_type[entry["type"]][entry["id"]] = public_entry

    written: list[str] = []
    for folder, content_type in sorted(FOLDER_TYPES.items()):
        target_dir = out_dir / folder
        target_dir.mkdir(parents=True, exist_ok=True)

        items = by_type[content_type]
        target = target_dir / f"{content_type}.json"
        target.write_text(
            json.dumps(items, ensure_ascii=False, indent="\t") + "\n",
            encoding="utf-8",
        )
        written.append(f"{target.as_posix()} ({len(items)})")

    return written


def collect_long_chunks(entries: list[dict], limit: int) -> list[str]:
    warnings: list[str] = []

    for entry in entries:
        if entry["type"] not in BOTTOM_TEXT_TYPES:
            continue

        for index, chunk in enumerate(entry["chunks"], start=1):
            # Шапка звонка живёт в своём узле, ограничение на две строки к ней не относится.
            if chunk["kind"] == CHUNK_KIND_CALL_META:
                continue

            length = len(chunk["text"])
            if length > limit:
                warnings.append(
                    f"{entry['_source']}: кусок {index} длиной {length} символов "
                    f"(больше {limit}, не влезет в две строки)"
                )

    return warnings


def write_registry(entries: list[dict], registry_path: Path) -> None:
    rows = sorted((entry["id"], entry["type"], entry["_source"]) for entry in entries)
    lines = [
        "# Реестр id",
        "",
        "Генерируется автоматически: `python content/engine/converter/build.py`.",
        "Руками не править.",
        "",
        "| id | type | файл |",
        "|----|------|------|",
    ]
    lines.extend(f"| `{entry_id}` | {content_type} | {source} |" for entry_id, content_type, source in rows)
    registry_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Сборка текстового контента в JSON.")
    parser.add_argument("--locale", default="ru")
    parser.add_argument(
        "--include-drafts",
        action="store_true",
        help="собрать и незавершённые тексты (status: draft)",
    )
    parser.add_argument(
        "--max-chunk-chars",
        type=int,
        default=DEFAULT_MAX_CHUNK_CHARS,
        help="длина куска, после которой он не влезает в две строки",
    )
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[3]
    raw_root = repo_root / "content" / "raw"
    out_root = repo_root / "content" / "localisation"
    registry_path = raw_root / "_system" / "ids_registry.md"

    try:
        entries = collect_entries(raw_root, repo_root)
        validate(entries)
    except BuildFailed as error:
        print("Сборка не удалась:", file=sys.stderr)
        print(error, file=sys.stderr)
        return 1

    write_registry(entries, registry_path)

    for warning in collect_long_chunks(entries, args.max_chunk_chars):
        print(f"внимание: {warning}", file=sys.stderr)

    drafts = [entry for entry in entries if entry["_status"] != "ready"]
    if not args.include_drafts:
        entries = [entry for entry in entries if entry["_status"] == "ready"]

    for line in write_locale(entries, out_root, args.locale):
        print(line)

    if drafts and not args.include_drafts:
        print(f"пропущено черновиков: {len(drafts)} (--include-drafts, чтобы собрать)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
