#!/usr/bin/env python3
"""Собирает .md из content/raw в JSON по локали; запускать из корня репозитория.
Схема узкая намеренно: что в неё не уложилось — ошибка сборки, а не тихий пропуск."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# Папка контента -> поле type. Ключ — путь от content/raw, так что тип может лежать
# и глубоко; промежуточные папки сами контент не держат.
FOLDER_TYPES = {
    "missions/calls": "call",
    "missions/mission_ids": "mission_id",
    "missions/radio": "radio",
    "missions/reports": "report",
    "cutscenes": "cutscene",
    "creatures": "creature",
    "shift_notes": "shift_note",
    "equipment": "equipment",
    "UI/hover_footnote/perks": "perk",
    "UI/hover_footnote/characteristics": "characteristic",
    "UI/hover_footnote/equipment_kinds": "equipment_kind",
    "UI/hover_footnote/scales": "scale",
    "UI/labels": "ui_label",
    "personnel/bio": "bio_line",
}

# Поля своего типа: имя -> значение по умолчанию, тип значения задаёт и проверку.
# Проверяются все, в JSON едут только PUBLIC_TYPE_FIELDS — остальное уже есть в data/.
TYPE_FIELDS = {
    "call": {"mission_type": "", "mission_id": ""},
    "creature": {"name": ""},
    "radio": {"mission_id": ""},
    "shift_note": {"day": 0},
    "report": {"outcome": "", "mission_id": ""},
    "perk": {"name": ""},
    "characteristic": {"name": ""},
    "equipment": {"name": ""},
    "equipment_kind": {"name": ""},
    "scale": {"name": ""},
    # Кусочек досье: slot говорит, в какое место анкеты фраза встаёт.
    "bio_line": {"slot": ""},
}

# Что из TYPE_FIELDS игра действительно читает: name — подпись в интерфейсе,
# slot — по нему фабрика сотрудников набирает фразы досье.
PUBLIC_TYPE_FIELDS = ("name", "slot")

# radio — вызов с вмешательством игрока, filler — одна проверка характеристик.
CALL_MISSION_TYPES = ("radio", "filler")

# Флаги появления есть только у звонка: планировщик ядра выкидывает из пула миссию,
# у чьего звонка не выставлен хотя бы один флаг из списка.
REQUIREMENTS_FIELD = "requirements"
REQUIREMENTS_TYPES = ("call",)
FLAG_RE = re.compile(r"^flag_[a-z0-9_]+$")

# properties — id условных блоков внутри записи. Работает только у существ: у остальных
# типов %% reveal %% не бывает, и список там всегда пустой.
PROPERTIES_TYPES = ("creature",)

# Виджет нижнего текста: кусок должен влезать примерно в две строки. Всё остальное —
# энциклопедия, отчёты, звонки — рендерится на своих экранах, где абзац уместен.
BOTTOM_TEXT_TYPES = ("cutscene",)
DEFAULT_MAX_CHUNK_CHARS = 150

KNOWN_STATUSES = ("draft", "ready")

COMMENT_RE = re.compile(r"<!--.*?-->", re.DOTALL)
# Шапки звонка больше нет: её место занял заголовок с названием миссии.
CALL_META_LEFTOVER_RE = re.compile(r"%%\s*call_meta\s*:")
# Заметки для разработчиков: блок %% dev %% ... %% /dev %% и инлайн %% dev: ... %%.
DEV_BLOCK_RE = re.compile(r"%%\s*dev\s*%%.*?%%\s*/\s*dev\s*%%", re.DOTALL)
DEV_INLINE_RE = re.compile(r"[ \t]*%%\s*dev:[^\n]*?%%")
DEV_TAG_RE = re.compile(r"%%\s*(/?)\s*dev\s*%%")
DEV_LEFTOVER_RE = re.compile(r"%%\s*/?\s*dev\b")
REVEAL_OPEN_RE = re.compile(r"^%%\s*reveal:\s*([^\s%]+)\s*%%$")
REVEAL_CLOSE_RE = re.compile(r"^%%\s*/\s*reveal\s*%%$")
OPTION_RE = re.compile(r"^##\s*Вариант:\s*(.+?)\s*$")
OPTION_META_RE = re.compile(r"^(id|requires)\s*:\s*(.*)$")
ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")
# Реестр названий миссий: файл на смену, строка таблицы — миссия. Разбирается не как
# обычная запись, поэтому тип вынесен в константу.
REGISTRY_TYPE = "mission_id"

# Типы-таблицы «id | текст»: запись — короткая строка без тела, файлом держать дороже.
# У ui_label текст едет в name, тело пустое; числа подставляет игра через {{имя}}.
REGISTRY_TYPES = (REGISTRY_TYPE, "ui_label")
# Как назвать вторую колонку в сообщении об ошибке — таблицы одинаковые, смысл разный.
REGISTRY_COLUMN = {REGISTRY_TYPE: "название", "ui_label": "текст"}
# Характеристики автор пишет по-русски — «интеллект, ловкость». Ядро знает
# характеристики по английским ключам, поэтому перевод делается здесь, на сборке.
STAT_ALIASES = {
    "сила": "strength",
    "боевая подготовка": "combat",
    "ловкость": "agility",
    "харизма": "charisma",
    "интеллект": "intellect",
}
REQUIRES_NAME_RE = re.compile(r"^[А-Яа-яЁё]+(?:\s+[А-Яа-яЁё]+)*$")
LINK_RE = re.compile(r"\[\[([a-z_]+):([a-z0-9_]+)\]\]")
# Подстановка числа из геймплейных данных: «+{{bonus.strength}} к силе». Не %%,
# потому что Обсидиан прячет %%...%%, а плейсхолдер должен быть виден автору.
VARIABLE_RE = re.compile(r"\{\{([^{}]*)\}\}")
VARIABLE_NAME_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")
VARIABLE_LEFTOVER_RE = re.compile(r"\{\{|\}\}")
# Разметка абзаца: ==ключевое слово== красится, **слово** идёт жирным. Обе формы —
# обычный markdown, Обсидиан показывает их так же, как увидит игрок. Вложенности нет.
INLINE_RE = re.compile(r"==\s*([^=]+?)\s*==|\*\*\s*([^*]+?)\s*\*\*")
INLINE_LEFTOVER_RE = re.compile(r"==|\*\*")
LIST_ITEM_RE = re.compile(r"^\s+-\s*(.*)$")
SCALAR_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$")


class BuildFailed(Exception):
    pass


def check_layout(raw_root: Path) -> tuple[list[str], list[str]]:
    """Ловит контент не в той папке: иначе папка с опечаткой молча выпадет из сборки."""
    errors: list[str] = []
    warnings: list[str] = []
    check_directory(raw_root, raw_root, errors, warnings)
    return errors, warnings


def check_directory(path: Path, raw_root: Path, errors: list[str], warnings: list[str]) -> None:
    for item in sorted(path.iterdir()):
        if item.name.startswith(("_", ".")):
            continue

        relative = item.relative_to(raw_root).as_posix()

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
    """Выкидывает dev-заметки. Парность проверяется заранее: DEV_BLOCK_RE идёт по DOTALL
    и при пропущенном %% /dev %% съел бы реплики до следующего закрывающего тега."""
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


def make_chunk(text: str, reveal: str | None, source: Path) -> dict:
    """Кусок с разобранной разметкой: текст чистый, разметка — списком отрезков.
    Отрезки, а не смещения: рантайм подставляет {{имя}} в текст, и позиции уехали бы."""
    spans, clean = parse_spans(text, source)
    chunk = {"text": clean, "reveal": reveal}
    if spans:
        chunk["spans"] = spans
    return chunk


def parse_spans(text: str, source: Path) -> tuple[list[dict], str]:
    """Отрезки разметки и текст без тегов. Склей отрезки подряд — получишь текст обратно."""
    spans: list[dict] = []
    clean: list[str] = []
    cursor = 0

    def add(part: str, highlight: bool = False, bold: bool = False) -> None:
        if part:
            spans.append({"text": part, "highlight": highlight, "bold": bold})
            clean.append(part)

    for match in INLINE_RE.finditer(text):
        add(text[cursor : match.start()])
        if match.group(1) is not None:
            add(match.group(1), highlight=True)
        else:
            add(match.group(2), bold=True)
        cursor = match.end()

    tail = text[cursor:]
    if spans:
        add(tail)
    else:
        clean.append(tail)

    result = "".join(clean)

    if INLINE_LEFTOVER_RE.search(result):
        raise BuildFailed(
            f"{source}: непарная разметка == или ** в {text!r} — "
            "нужно ==слово== либо **слово** целиком в одну строку"
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
            chunks.append(make_chunk(text, reveal, source))
        buffer = []

    for raw_line in lines:
        line = raw_line.strip()

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

    option_id = meta.get("id", "")
    if not ID_RE.match(option_id):
        raise BuildFailed(
            f"{source}: у варианта {name!r} нет поля id или оно не по формату "
            f"(латиница, цифры и _, начиная с буквы) — по id вариант связан с отчётом"
        )

    return {
        "name": name,
        "id": option_id,
        "requires": parse_requires(meta.get("requires", ""), option_id, source),
        "chunks": parse_chunks(lines[start:], source),
    }


def parse_flags(value, source: Path) -> list[str]:
    """Флаги появления вызова: `flag_что_то`, выставляет их геймплей по ходу партии.
    Имя проверяется по форме, а не по списку: перечень флагов ведёт геймплейная сторона."""
    if isinstance(value, str):
        value = [value] if value.strip() else []

    flags: list[str] = []
    for item in value:
        flag = item.strip()
        if not flag:
            continue
        if not FLAG_RE.match(flag):
            raise BuildFailed(
                f"{source}: {flag!r} не похоже на флаг — нужно flag_ и дальше "
                f"латиница, цифры и подчёркивания"
            )
        if flag in flags:
            raise BuildFailed(f"{source}: флаг {flag!r} указан дважды")
        flags.append(flag)

    return flags


def parse_requires(text: str, owner: str, source: Path) -> list[str]:
    """«боевая подготовка, сила» — какие характеристики проверяются, без порогов.
    Порог живёт в данных миссии: текст отвечает «что проверяем», а не «насколько трудно»."""
    requires: list[str] = []
    text = text.strip()
    if not text:
        return requires

    for part in text.split(","):
        name = part.strip()
        if not REQUIRES_NAME_RE.match(name):
            raise BuildFailed(
                f"{source}: у {owner!r} непонятная запись требования {name!r} — "
                f"нужны только названия характеристик через запятую, без чисел "
                f"(порог берётся из data/missions.json)"
            )

        stat = STAT_ALIASES.get(name.lower())
        if stat is None:
            raise BuildFailed(
                f"{source}: у {owner!r} неизвестная характеристика {name!r}; "
                f"допустимы {', '.join(sorted(STAT_ALIASES))}"
            )

        if stat in requires:
            raise BuildFailed(f"{source}: у {owner!r} характеристика {name!r} указана дважды")

        requires.append(stat)

    return requires


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

    options = [parse_option(name, body, source) for name, body in blocks]
    check_option_ids(options, source)
    return intro, options


def check_option_ids(options: list[dict], source: Path) -> None:
    """id — ключ связи с отчётом и последствиями, внутри файла он уникален."""
    seen: dict[str, str] = {}
    for option in options:
        if option["id"] in seen:
            raise BuildFailed(
                f"{source}: id варианта {option['id']!r} занят вариантом "
                f"{seen[option['id']]!r} — внутри файла id уникален"
            )
        seen[option["id"]] = option["name"]


def collect_variables(entry: dict, source: Path) -> list[str]:
    """Проверяет форму имён подстановок: иначе опечатка вроде {{ bonus.strength}}
    доехала бы до игрока как есть."""
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
    if CALL_META_LEFTOVER_RE.search(body_text):
        raise BuildFailed(
            f"{path}: тега %% call_meta %% больше нет — заголовок звонка берётся "
            f"из названия миссии в mission_ids/"
        )
    body_lines = strip_dev_notes(body_text, path).split("\n")

    entry: dict = {
        "id": frontmatter["id"],
        "type": frontmatter["type"],
    }

    if expected_type in REQUIREMENTS_TYPES:
        entry[REQUIREMENTS_FIELD] = parse_flags(
            frontmatter.get(REQUIREMENTS_FIELD, []), path
        )
    elif frontmatter.get(REQUIREMENTS_FIELD):
        raise BuildFailed(
            f"{path}: флаги появления есть только у {', '.join(REQUIREMENTS_TYPES)} — "
            f"запись типа {expected_type!r} показывают по её миссии или по дню, "
            f"а не по своему флагу"
        )

    if expected_type in PROPERTIES_TYPES:
        entry["properties"] = frontmatter.get("properties", [])
    elif frontmatter.get("properties"):
        raise BuildFailed(
            f"{path}: properties есть только у {', '.join(PROPERTIES_TYPES)} — "
            f"это id блоков %% reveal %%, а у типа {expected_type!r} их не бывает"
        )

    if expected_type == "radio":
        intro_lines, entry["options"] = parse_options(body_lines, path)
        entry["chunks"] = parse_chunks(intro_lines, path)
        if not entry["options"]:
            raise BuildFailed(f"{path}: у radio должен быть хотя бы один вариант решения")
    else:
        entry["chunks"] = parse_chunks(body_lines, path)

    # Имена подстановок не сохраняем: их видно в самом тексте, а игра подставляет
    # значения по имени. Проверка формы {{имя}} при этом остаётся.
    collect_variables(entry, path)

    fields: dict = {}
    for field, default in TYPE_FIELDS.get(expected_type, {}).items():
        value = frontmatter.get(field, default)
        if isinstance(default, int):
            try:
                value = int(value)
            except (TypeError, ValueError):
                raise BuildFailed(f"{path}: {field} должно быть целым, а не {value!r}") from None
        fields[field] = value
        if field in PUBLIC_TYPE_FIELDS:
            entry[field] = value

    if expected_type == "call" and fields["mission_type"] not in CALL_MISSION_TYPES:
        raise BuildFailed(
            f"{path}: mission_type={fields['mission_type']!r}, допустимы только "
            f"{' и '.join(CALL_MISSION_TYPES)} (radio — вызов с вмешательством по рации, "
            "filler — вызов, который решается одной проверкой характеристик)"
        )

    if expected_type == "call":
        # requires автор пишет для себя и для сверки с data/missions.json, в JSON он не едет.
        # У филлера это единственная проверка смены, поэтому пустой она быть не может.
        requires = parse_requires(frontmatter.get("requires", ""), entry["id"], path)
        if fields["mission_type"] == "filler" and not requires:
            raise BuildFailed(
                f"{path}: у филлера пустой requires — вся миссия держится на одной "
                f"проверке, и назвать её нужно здесь"
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
    # Служебные поля (с «_») в JSON не едут: они нужны проверкам сборки, а не игре.
    entry["_fields"] = fields
    return entry


def parse_registry(path: Path, repo_root: Path, content_type: str = REGISTRY_TYPE) -> list[dict]:
    """Таблица «id | текст»: файл на смену (миссии) или на экран (подписи).
    Вместе намеренно: состав смены видно списком, а не пятнадцатью файлами."""
    column = REGISTRY_COLUMN.get(content_type, "название")
    try:
        raw_text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError as error:
        raise BuildFailed(f"{path}: файл должен быть в UTF-8 ({error})") from None

    frontmatter, body_lines = parse_frontmatter(raw_text, path)

    for required in ("type", "status"):
        if not frontmatter.get(required):
            raise BuildFailed(f"{path}: во фронтматтере нет поля {required}")

    if frontmatter["type"] != content_type:
        raise BuildFailed(
            f"{path}: type={frontmatter['type']!r}, но файл лежит в папке типа {content_type!r}"
        )

    if frontmatter["status"] not in KNOWN_STATUSES:
        raise BuildFailed(
            f"{path}: status={frontmatter['status']!r}, допустимы только "
            f"{' и '.join(KNOWN_STATUSES)}"
        )

    entries: list[dict] = []
    source = path.relative_to(repo_root).as_posix()

    for line in body_lines:
        row = line.strip()
        if not row.startswith("|"):
            continue

        cells = [cell.strip() for cell in row.strip("|").split("|")]
        if len(cells) != 2:
            raise BuildFailed(
                f"{path}: в строке {row!r} должно быть ровно две колонки — id и {column}"
            )

        entry_id, name = cells
        # Шапка таблицы и разделитель под ней — не записи.
        if entry_id.lower() == "id" or set(entry_id) <= set("-: "):
            continue

        if not ID_RE.match(entry_id):
            raise BuildFailed(
                f"{path}: id {entry_id!r} не по формату — латиница, цифры и _, "
                f"начиная с буквы"
            )

        if not name:
            raise BuildFailed(f"{path}: у записи {entry_id!r} пустое поле «{column}»")

        entries.append(
            {
                "id": entry_id,
                "type": content_type,
                "name": name,
                "chunks": [],
                "_status": frontmatter["status"],
                "_source": source,
            }
        )

    if not entries:
        raise BuildFailed(f"{path}: в таблице нет ни одной строки «id | {column}»")

    return entries


def collect_entries(raw_root: Path, repo_root: Path) -> list[dict]:
    entries: list[dict] = []
    errors: list[str] = []

    for folder, content_type in sorted(FOLDER_TYPES.items()):
        folder_path = raw_root / folder
        if not folder_path.is_dir():
            continue

        for path in sorted(folder_path.rglob("*.md")):
            try:
                if content_type in REGISTRY_TYPES:
                    entries.extend(parse_registry(path, repo_root, content_type))
                else:
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

    # Вызов, вмешательство и отчёты одной миссии должны сходиться на одном mission_id:
    # иначе название миссии живёт в трёх местах и расходится при первой же правке.
    missions = {entry["id"] for entry in entries if entry["type"] == "mission_id"}
    for entry in entries:
        mission_id = entry.get("_fields", {}).get("mission_id")
        if mission_id is None:
            continue

        if not mission_id:
            errors.append(
                f"{entry['_source']}: не заполнен mission_id — по нему {entry['type']} "
                f"связан с миссией из mission_ids/"
            )
        elif mission_id not in missions:
            errors.append(
                f"{entry['_source']}: mission_id={mission_id!r} — такой миссии "
                f"нет в mission_ids/"
            )

    if errors:
        raise BuildFailed("\n".join(errors))


def collect_long_chunks(entries: list[dict], limit: int) -> list[str]:
    warnings: list[str] = []

    for entry in entries:
        if entry["type"] not in BOTTOM_TEXT_TYPES:
            continue

        for index, chunk in enumerate(entry["chunks"], start=1):
            if len(chunk["text"]) <= limit:
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
        # newline="\n" по той же причине, что и у реестра: собранный JSON лежит
        # в git, а .gitattributes требует LF. Без этого каждая сборка контента
        # помечала бы изменёнными все четырнадцать файлов, не меняя в них ни строки.
        target.write_text(
            json.dumps(items, ensure_ascii=False, indent="\t") + "\n",
            encoding="utf-8",
            newline="\n",
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
    # newline="\n" обязателен: реестр лежит в git, а .gitattributes требует LF.
    # Без него write_text на Windows подставит CRLF, и файл будет числиться
    # изменённым после каждой сборки, хотя ни одна строка в нём не поменялась.
    registry_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


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
    # Исходники разложены по локалям: content/raw/<локаль>/<папка типа>/...
    # _system лежит рядом с локалями, а не внутри: шаблоны и реестр id общие.
    raw_root = repo_root / "content" / "raw" / args.locale
    registry_path = repo_root / "content" / "raw" / "_system" / "ids_registry.md"

    if not raw_root.is_dir():
        print(f"Нет исходников локали {args.locale!r}: ожидается {raw_root}", file=sys.stderr)
        return 1

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
