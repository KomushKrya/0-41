#!/usr/bin/env python3
"""Собирает .md из content/raw в JSON по локали, который читает рантайм Godot.

Запуск из корня репозитория:
    python content/engine/converter/build.py
    python content/engine/converter/build.py --include-drafts

Зависимостей нет — фронтматтер разбирается вручную, потому что схема
намеренно узкая (см. content/raw/_system/Syntax/README.md): скаляры и
простые списки. Всё, что в неё не укладывается, считается ошибкой сборки,
а не молча пропускается: молчаливая потеря текста хуже упавшего билда.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# Папка контента -> значение поля type во фронтматтере. Ключ — путь от content/raw,
# так что тип может лежать и во вложенной папке (UI/perks); промежуточные папки
# (UI) сами контент не держат.
FOLDER_TYPES = {
    "calls": "call",
    "cutscenes": "cutscene",
    "radio": "radio",
    "creatures": "creature",
    "shift_notes": "shift_note",
    "reports": "report",
    "UI/perks": "perk",
    "UI/characteristics": "characteristic",
}

# Поля, которые есть только у своего типа: имя -> значение по умолчанию.
# Тип значения задаёт и проверку: int по умолчанию требует целого во фронтматтере.
TYPE_FIELDS = {
    "call": {"mission_type": ""},
    "creature": {"name": ""},
    "shift_note": {"day": 0},
    "report": {"outcome": ""},
    "perk": {"name": ""},
    "characteristic": {"name": ""},
}

# Чем заканчивается вызов: радийный требует вмешательства игрока (есть запись radio
# с вариантами решений), филерный решается одной проверкой характеристик группы.
# Пустое значение не допускается: тип вызова определяет, ждать ли под него текст radio.
CALL_MISSION_TYPES = ("radio", "filler")

# Типы, которые показываются виджетом нижнего текста: у них кусок должен влезать
# примерно в две строки. Остальные (энциклопедия, отчёты) рендерятся на своих
# экранах, где места больше, и под это ограничение не попадают.
BOTTOM_TEXT_TYPES = ("call", "cutscene")
DEFAULT_MAX_CHUNK_CHARS = 150

KNOWN_STATUSES = ("draft", "ready")

CHUNK_KIND_TEXT = "text"
CHUNK_KIND_CALL_META = "call_meta"

COMMENT_RE = re.compile(r"<!--.*?-->", re.DOTALL)
# Заметки для разработчиков: блок %% dev %% ... %% /dev %% и инлайн %% dev: ... %%.
DEV_BLOCK_RE = re.compile(r"%%\s*dev\s*%%.*?%%\s*/\s*dev\s*%%", re.DOTALL)
DEV_INLINE_RE = re.compile(r"[ \t]*%%\s*dev:[^\n]*?%%")
DEV_TAG_RE = re.compile(r"%%\s*(/?)\s*dev\s*%%")
DEV_LEFTOVER_RE = re.compile(r"%%\s*/?\s*dev\b")
# Служебная шапка звонка — [ЗВОНОК ПЕРЕНАПРАВЛЕН ...], [НА ЛИНИИ СТАРИК] и т.п.
# В куски попадает отдельным kind, чтобы виджет рендерил её не как реплику.
CALL_META_RE = re.compile(r"^%%\s*call_meta:\s*(.+?)\s*%%$")
REVEAL_OPEN_RE = re.compile(r"^%%\s*reveal:\s*([^\s%]+)\s*%%$")
REVEAL_CLOSE_RE = re.compile(r"^%%\s*/\s*reveal\s*%%$")
OPTION_RE = re.compile(r"^##\s*Вариант:\s*(.+?)\s*$")
OPTION_META_RE = re.compile(r"^(requirement_modifier)\s*:\s*(.*)$")
LINK_RE = re.compile(r"\[\[([a-z_]+):([a-z0-9_]+)\]\]")
# Подстановка числа из геймплейных данных: «+{{bonus.strength}} к силе».
# Намеренно не %%: Обсидиан прячет %%...%% в режиме чтения, а плейсхолдер должен
# оставаться видимым автору. Конвертер значение не подставляет — оно живёт на
# стороне Godot (data/abilities.json и т.п.), движок только помечает место.
VARIABLE_RE = re.compile(r"\{\{([^{}]*)\}\}")
VARIABLE_NAME_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")
VARIABLE_LEFTOVER_RE = re.compile(r"\{\{|\}\}")
# Ключевое слово в реплике: ==дочка маленькая==. Тег только красит — какую характеристику
# слово подсказывает, в тексте не написано и игроку не показывается: это ему и предстоит
# сообразить. Соответствие слов характеристикам — договорённость авторов, живёт в
# _system/Keywords.md и в сборку не едет.
# Синтаксис совпадает с обсидиановской подсветкой ==...==: в режиме чтения автор видит
# подсвеченным ровно то, что увидит игрок.
KEYWORD_RE = re.compile(r"==\s*([^=]+?)\s*==")
KEYWORD_LEFTOVER_RE = re.compile(r"==")
LIST_ITEM_RE = re.compile(r"^\s+-\s*(.*)$")
SCALAR_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$")


class BuildFailed(Exception):
    pass


def check_layout(raw_root: Path) -> tuple[list[str], list[str]]:
    """Ловит контент, который лежит не там, где конвертер его ищет.

    Без этой проверки папка с опечаткой (creature вместо creatures) просто
    не обходится: сборка проходит, а текстов в игре нет.
    """
    errors: list[str] = []
    warnings: list[str] = []
    check_directory(raw_root, raw_root, errors, warnings)
    return errors, warnings


def check_directory(path: Path, raw_root: Path, errors: list[str], warnings: list[str]) -> None:
    for item in sorted(path.iterdir()):
        if item.name.startswith(("_", ".")):
            continue

        relative = item.relative_to(raw_root).as_posix()

        # Старые таблицы локализации ещё лежат в raw/ru/UI/labels. Их читает
        # прежний контентный слой, а новый конвертер работает с единой структурой
        # без префикса локали и не умеет собирать таблицы ui_label. Не даём этому
        # архивному источнику срывать сборку актуальных игровых текстов.
        if relative == "ru":
            continue

        if not item.is_dir():
            warnings.append(f"{item}: лежит вне папки типа и в сборку не попадёт")
        elif relative in FOLDER_TYPES:
            warnings.extend(
                f"{child}: не .md и в сборку не попадёт"
                for child in sorted(item.rglob("*"))
                if child.is_file() and child.suffix != ".md"
            )
        elif any(folder.startswith(f"{relative}/") for folder in FOLDER_TYPES):
            # Промежуточная папка (UI): своего типа у неё нет, идём глубже.
            check_directory(item, raw_root, errors, warnings)
        else:
            errors.append(
                f"{item}: неизвестная папка контента; допустимы "
                f"{', '.join(sorted(FOLDER_TYPES))} либо служебная с '_' в начале"
            )


def parse_frontmatter(text: str, source: Path) -> tuple[dict, list[str]]:
    """Возвращает поля фронтматтера и оставшиеся строки тела."""
    lines = text.replace("\r\n", "\n").split("\n")
    if not lines or lines[0].strip() != "---":
        raise BuildFailed(f"{source}: файл должен начинаться с фронтматтера (---)")

    data: dict = {}
    current_list_key: str | None = None

    for index, line in enumerate(lines[1:], start=1):
        if line.strip() == "---":
            return data, lines[index + 1 :]

        if not line.strip():
            continue

        item = LIST_ITEM_RE.match(line)
        if item:
            if current_list_key is None:
                raise BuildFailed(f"{source}: элемент списка вне поля: {line!r}")
            if item.group(1).strip():
                data[current_list_key].append(item.group(1).strip())
            continue

        scalar = SCALAR_RE.match(line)
        if not scalar:
            raise BuildFailed(f"{source}: непонятная строка фронтматтера: {line!r}")

        key, value = scalar.group(1), scalar.group(2).strip()
        data[key] = value or []
        current_list_key = None if value else key

    raise BuildFailed(f"{source}: фронтматтер не закрыт (нет второго ---)")


def strip_dev_notes(text: str, source: Path) -> str:
    """Выкидывает dev-заметки до разбора кусков, чтобы они не дошли до JSON.

    Парность тегов проверяется до подстановки: DEV_BLOCK_RE работает по DOTALL и
    при пропущенном %% /dev %% дотянулся бы до закрывающего тега следующего блока,
    молча съев реплики между ними.
    """
    depth = 0
    for match in DEV_TAG_RE.finditer(text):
        depth += -1 if match.group(1) else 1
        if depth < 0:
            raise BuildFailed(f"{source}: %% /dev %% без открывающего %% dev %%")
        if depth > 1:
            raise BuildFailed(
                f"{source}: %% dev %% внутри незакрытого %% dev %% — "
                "поставь %% /dev %% в конце предыдущего блока"
            )

    if depth:
        raise BuildFailed(f"{source}: не закрыт блок %% dev %% (нет %% /dev %%)")

    text = DEV_INLINE_RE.sub("", DEV_BLOCK_RE.sub("", text))

    if DEV_LEFTOVER_RE.search(text):
        raise BuildFailed(
            f"{source}: незакрытая инлайн-заметка — нужно %% dev: ... %% в одну строку"
        )

    return text


def make_chunk(text: str, kind: str, reveal: str | None, source: Path) -> dict:
    """Кусок с разобранной подсветкой ключевых слов.

    Текст в JSON лежит уже чистым, без ==стат:...==, а разметка едет отдельным списком
    отрезков — не смещениями. Рантайм подставляет {{имя}} прямо в текст, и любые позиции
    после первой же подстановки уехали бы; отрезки же подставляются каждый сам по себе.
    """
    spans, clean = parse_keywords(text, source)
    chunk = {"text": clean, "kind": kind, "reveal": reveal}
    if spans:
        chunk["spans"] = spans
    return chunk


def parse_keywords(text: str, source: Path) -> tuple[list[dict], str]:
    """Отрезки подсветки и текст без тегов.

    Отрезки покрывают кусок целиком и идут по порядку: у обычного текста `highlight`
    ложный, у ключевого слова истинный. Склей их подряд — получишь текст обратно.
    """
    spans: list[dict] = []
    clean: list[str] = []
    cursor = 0

    def add(part: str, highlight: bool) -> None:
        if part:
            spans.append({"text": part, "highlight": highlight})
            clean.append(part)

    for match in KEYWORD_RE.finditer(text):
        add(text[cursor : match.start()], False)
        add(match.group(1), True)
        cursor = match.end()

    tail = text[cursor:]
    if spans:
        add(tail, False)
    else:
        clean.append(tail)

    result = "".join(clean)

    if KEYWORD_LEFTOVER_RE.search(result):
        raise BuildFailed(
            f"{source}: непарная подсветка == в {text!r} — "
            "нужно ==слово== целиком в одну строку"
        )

    return spans, result


def parse_chunks(lines: list[str], source: Path) -> list[dict]:
    """Абзац = один кусок текста (клик). %% reveal %% помечает кусок условным."""
    chunks: list[dict] = []
    buffer: list[str] = []
    reveal: str | None = None

    def flush() -> None:
        nonlocal buffer
        text = " ".join(part.strip() for part in buffer).strip()
        if text:
            chunks.append(make_chunk(text, CHUNK_KIND_TEXT, reveal, source))
        buffer = []

    for raw_line in lines:
        line = raw_line.strip()

        meta = CALL_META_RE.match(line)
        if meta:
            flush()
            chunks.append(make_chunk(meta.group(1), CHUNK_KIND_CALL_META, reveal, source))
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


def parse_option(name: str, lines: list[str], source: Path) -> dict:
    """Метаполя идут до первой строки текста, пустые строки перед текстом отбрасываются."""
    meta: dict = {}
    start = 0

    for index, raw_line in enumerate(lines):
        line = raw_line.strip()
        if line:
            match = OPTION_META_RE.match(line)
            if not match:
                break
            meta[match.group(1)] = match.group(2).strip()
        start = index + 1

    modifier_text = meta.get("requirement_modifier", "0")
    try:
        modifier = int(modifier_text)
    except ValueError:
        raise BuildFailed(
            f"{source}: requirement_modifier у варианта {name!r} должен быть целым, "
            f"а не {modifier_text!r}"
        ) from None

    return {
        "name": name,
        "requirement_modifier": modifier,
        "chunks": parse_chunks(lines[start:], source),
    }


def parse_options(lines: list[str], source: Path) -> tuple[list[str], list[dict]]:
    """`## Вариант: <название>` делит тело на вводную и блоки решений."""
    intro: list[str] = []
    blocks: list[tuple[str, list[str]]] = []

    for line in lines:
        match = OPTION_RE.match(line.strip())
        if match:
            blocks.append((match.group(1), []))
        elif blocks:
            blocks[-1][1].append(line)
        else:
            intro.append(line)

    return intro, [parse_option(name, body, source) for name, body in blocks]


def collect_variables(entry: dict, source: Path) -> list[str]:
    """Имена подстановок из всех кусков записи — чтобы игра знала, что заполнять.

    Значения конвертер не знает и знать не должен: числа живут в геймплейных
    данных. Проверяется только форма имени — иначе опечатка вроде {{ bonus.strength}}
    молча доехала бы до игрока как есть.
    """
    names: set[str] = set()

    for chunk in all_chunks(entry):
        for raw_name in VARIABLE_RE.findall(chunk["text"]):
            if not VARIABLE_NAME_RE.match(raw_name):
                raise BuildFailed(
                    f"{source}: непонятное имя подстановки {{{{{raw_name}}}}} — "
                    "допустимы буквы, цифры и _ через точку, без пробелов"
                )
            names.add(raw_name)

        if VARIABLE_LEFTOVER_RE.search(VARIABLE_RE.sub("", chunk["text"])):
            raise BuildFailed(
                f"{source}: непарные скобки подстановки в куске {chunk['text']!r} — "
                "нужно {{имя}} целиком в одну строку"
            )

    return sorted(names)


def all_chunks(entry: dict):
    """Куски вводной плюс куски всех вариантов — у radio текст есть и там."""
    yield from entry["chunks"]
    for option in entry.get("options", []):
        yield from option["chunks"]


def parse_file(path: Path, expected_type: str, repo_root: Path) -> dict:
    try:
        raw_text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError as error:
        raise BuildFailed(f"{path}: файл должен быть в UTF-8 ({error})") from None

    frontmatter, body_lines = parse_frontmatter(raw_text, path)

    for required in ("id", "type", "status"):
        if not frontmatter.get(required):
            raise BuildFailed(f"{path}: во фронтматтере нет поля {required}")

    if frontmatter["type"] != expected_type:
        raise BuildFailed(
            f"{path}: type={frontmatter['type']!r}, но файл лежит в папке типа {expected_type!r}"
        )

    if frontmatter["status"] not in KNOWN_STATUSES:
        raise BuildFailed(
            f"{path}: status={frontmatter['status']!r}, допустимы только "
            f"{' и '.join(KNOWN_STATUSES)} (иначе запись молча выпадет из сборки)"
        )

    body_text = COMMENT_RE.sub("", "\n".join(body_lines))
    body_lines = strip_dev_notes(body_text, path).split("\n")

    entry: dict = {
        "id": frontmatter["id"],
        "type": frontmatter["type"],
        "requirements": frontmatter.get("requirements", []),
        "properties": frontmatter.get("properties", []),
    }

    if expected_type == "radio":
        intro_lines, entry["options"] = parse_options(body_lines, path)
        entry["chunks"] = parse_chunks(intro_lines, path)
        if not entry["options"]:
            raise BuildFailed(f"{path}: у radio должен быть хотя бы один вариант решения")
    else:
        entry["chunks"] = parse_chunks(body_lines, path)

    entry["variables"] = collect_variables(entry, path)

    for field, default in TYPE_FIELDS.get(expected_type, {}).items():
        value = frontmatter.get(field, default)
        if isinstance(default, int):
            try:
                value = int(value)
            except (TypeError, ValueError):
                raise BuildFailed(f"{path}: {field} должно быть целым, а не {value!r}") from None
        entry[field] = value

    if expected_type == "call" and entry["mission_type"] not in CALL_MISSION_TYPES:
        raise BuildFailed(
            f"{path}: mission_type={entry['mission_type']!r}, допустимы только "
            f"{' и '.join(CALL_MISSION_TYPES)} (radio — вызов с вмешательством по рации, "
            "filler — вызов, который решается одной проверкой характеристик)"
        )

    if expected_type not in BOTTOM_TEXT_TYPES:
        for chunk in all_chunks(entry):
            if chunk["kind"] == CHUNK_KIND_CALL_META:
                raise BuildFailed(
                    f"{path}: %% call_meta %% рендерится только виджетом нижнего текста, "
                    f"а тип {expected_type!r} показывается на своём экране"
                )

    if expected_type == "creature":
        declared = set(entry["properties"])
        revealed = {chunk["reveal"] for chunk in entry["chunks"] if chunk["reveal"]}
        if declared != revealed:
            raise BuildFailed(
                f"{path}: properties={sorted(declared)} не совпадает "
                f"с %% reveal %% в теле {sorted(revealed)}"
            )

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
        if entry["id"] in by_id:
            errors.append(
                f"дублирующийся id {entry['id']!r}: {by_id[entry['id']]} и {entry['_source']}"
            )
        else:
            by_id[entry["id"]] = entry["_source"]

    for entry in entries:
        for chunk in all_chunks(entry):
            for link_type, link_id in LINK_RE.findall(chunk["text"]):
                if link_id not in by_id:
                    errors.append(
                        f"{entry['_source']}: ссылка [[{link_type}:{link_id}]] ведёт в никуда"
                    )

    if errors:
        raise BuildFailed("\n".join(errors))


def collect_long_chunks(entries: list[dict], limit: int) -> list[str]:
    warnings: list[str] = []

    for entry in entries:
        if entry["type"] not in BOTTOM_TEXT_TYPES:
            continue

        for index, chunk in enumerate(entry["chunks"], start=1):
            # Шапка звонка живёт в своём узле, ограничение на две строки к ней не относится.
            if chunk["kind"] == CHUNK_KIND_CALL_META or len(chunk["text"]) <= limit:
                continue

            warnings.append(
                f"{entry['_source']}: кусок {index} длиной {len(chunk['text'])} символов "
                f"(больше {limit}, не влезет в две строки)"
            )

    return warnings


def write_locale(entries: list[dict], out_root: Path, locale: str) -> list[str]:
    """Папки внутри локали повторяют структуру content/raw."""
    by_type: dict[str, dict] = {content_type: {} for content_type in FOLDER_TYPES.values()}
    for entry in entries:
        public = {key: value for key, value in entry.items() if not key.startswith("_")}
        by_type[entry["type"]][entry["id"]] = public

    written: list[str] = []
    for folder, content_type in sorted(FOLDER_TYPES.items()):
        target_dir = out_root / locale / folder
        target_dir.mkdir(parents=True, exist_ok=True)

        items = by_type[content_type]
        target = target_dir / f"{content_type}.json"
        target.write_text(
            json.dumps(items, ensure_ascii=False, indent="\t") + "\n",
            encoding="utf-8",
        )
        written.append(f"{target.as_posix()} ({len(items)})")

    return written


def write_registry(entries: list[dict], registry_path: Path) -> None:
    lines = [
        "# Реестр id",
        "",
        "Генерируется автоматически: `python content/engine/converter/build.py`.",
        "Руками не править.",
        "",
        "| id | type | файл |",
        "|----|------|------|",
    ]
    lines.extend(
        f"| `{entry_id}` | {content_type} | {source} |"
        for entry_id, content_type, source in sorted(
            (entry["id"], entry["type"], entry["_source"]) for entry in entries
        )
    )
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
    registry_path = raw_root / "_system" / "ids_registry.md"

    try:
        layout_errors, warnings = check_layout(raw_root)
        if layout_errors:
            raise BuildFailed("\n".join(layout_errors))

        entries = collect_entries(raw_root, repo_root)
        validate(entries)
    except BuildFailed as error:
        print("Сборка не удалась:", file=sys.stderr)
        print(error, file=sys.stderr)
        return 1

    write_registry(entries, registry_path)

    for warning in warnings + collect_long_chunks(entries, args.max_chunk_chars):
        print(f"внимание: {warning}", file=sys.stderr)

    drafts = sum(1 for entry in entries if entry["_status"] != "ready")
    if not args.include_drafts:
        entries = [entry for entry in entries if entry["_status"] == "ready"]

    for line in write_locale(entries, repo_root / "content" / "localisation", args.locale):
        print(line)

    if drafts and not args.include_drafts:
        print(f"пропущено черновиков: {drafts} (--include-drafts, чтобы собрать)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
