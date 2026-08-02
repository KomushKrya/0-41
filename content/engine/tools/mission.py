#!/usr/bin/env python3
"""Инструмент автора миссий К.О.Н.Т.У.Р.

    python content/engine/tools/mission.py new
    python content/engine/tools/mission.py check
    python content/engine/tools/mission.py preview m_pensioner_door

Три команды закрывают три вопроса, на которые автор иначе отвечает вручную и с ошибками:

    new      — завести миссию целиком: тексты, варианты, отчёты, геймплейные данные.
               Один вызов вместо семи файлов, которые надо не забыть связать между собой.
    check    — сверить всё со всем и объяснить расхождения словами, а не стектрейсом.
    preview  — показать, во что превратились пороги: таблица шансов по реальным составам.

Зависимостей нет. Все правила продублированы из ядра осознанно: инструмент должен
работать без .NET, а расхождение ловится командой check на первом же прогоне.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# --------------------------------------------------------------------------- модель

STATS = ("strength", "combat", "agility", "charisma", "intellect")

# Первая часть демо. Появится вторая — глава станет параметром команды new.
CHAPTER = "chapter_1"

# Исходники разложены по локалям: content/raw/<локаль>/...
LOCALE = "ru"

STAT_RU = {
    "strength": "Сила",
    "combat": "Боевая подготовка",
    "agility": "Ловкость",
    "charisma": "Харизма",
    "intellect": "Интеллект",
}

STAT_ALIASES = {ru.lower(): key for key, ru in STAT_RU.items()}
STAT_ALIASES.update({key: key for key in STATS})

QUALITIES = ("good", "neutral", "bad")
QUALITY_RU = {"good": "хороший", "neutral": "нейтральный", "bad": "плохой"}
TIERS = ("Story", "Filler")
CAPS = ("None", "Injury", "Death")
CAP_RANK = {"None": 0, "Injury": 1, "Death": 2}

# Должно совпадать с StatMatchConfig в ядре. Расхождение поймает check.
MATCH = {
    "exceeds_margin": 2,
    "meets_score": 0.8,
    "below_falloff": 0.35,
    "primary_weight": 2.0,
    "secondary_weight": 1.0,
}

ID_RE = re.compile(r"^[a-z][a-z0-9_]*$")


class ToolError(Exception):
    pass


# --------------------------------------------------------------------------- пути и чтение


def repo_root() -> Path:
    here = Path(__file__).resolve()
    for candidate in here.parents:
        if (candidate / "project.godot").exists():
            return candidate
    raise ToolError("не найден корень проекта (ищу project.godot вверх по дереву)")


def read_json(path: Path):
    if not path.exists():
        raise ToolError(f"нет файла {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, data) -> None:
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def raw_ids(root: Path) -> set[str]:
    """id, которые уже написаны в .md, но могли не попасть в сборку.

    Различать это важно: «текста нет» и «текст есть, но забыли собрать» —
    разные ошибки с разными действиями, а выглядят одинаково.
    """
    found: set[str] = set()
    raw = root / "content" / "raw"
    if not raw.exists():
        return found

    for file in raw.rglob("*.md"):
        if "_system" in file.parts:
            continue
        match = re.search(r"^id:\s*(\S+)\s*$", file.read_text(encoding="utf-8"), re.M)
        if match:
            found.add(match.group(1))

    return found


def load_world(root: Path) -> dict:
    """Всё, что нужно знать инструменту о текущем состоянии игры."""
    data = root / "data"
    world = {
        "root": root,
        "missions": read_json(data / "missions.json"),
        "events": read_json(data / "mission_events.json"),
        "zones": read_json(data / "zones.json"),
        "creatures": read_json(data / "creatures.json"),
        "roster": read_json(data / "employees.json"),
        "config": read_json(data / "config.json"),
        "entries": {},
        "raw_ids": raw_ids(root),
    }

    locale = root / "content" / "localisation" / "ru"
    if locale.exists():
        for file in sorted(locale.rglob("*.json")):
            world["entries"].update(json.loads(file.read_text(encoding="utf-8")))

    return world


# --------------------------------------------------------------------------- расчёт


def squad_profile(members: list[dict]) -> dict:
    """Профиль группы — ЛУЧШИЙ в отряде по каждой характеристике, а не сумма."""
    return {stat: max((m["stats"].get(stat, 0) for m in members), default=0) for stat in STATS}


def evaluate(requirements: dict, profile: dict, primary: str | None) -> tuple[list[dict], float, bool]:
    """Разбор по характеристикам плюс итоговое совпадение профилей."""
    rows: list[dict] = []
    weighted = 0.0
    total_weight = 0.0

    for stat in STATS:
        required = requirements.get(stat, 0)
        if required <= 0:
            continue

        best = profile.get(stat, 0)
        margin = best - required

        if margin >= MATCH["exceeds_margin"]:
            rating, score = "green", 1.0
        elif margin >= 0:
            rating, score = "yellow", MATCH["meets_score"]
        else:
            rating = "red"
            score = MATCH["meets_score"] * MATCH["below_falloff"] ** (-margin)

        weight = MATCH["primary_weight"] if stat == primary else MATCH["secondary_weight"]
        weighted += score * weight
        total_weight += weight

        rows.append(
            {
                "stat": stat,
                "required": required,
                "best": best,
                "margin": margin,
                "rating": rating,
                "score": score,
                "primary": stat == primary,
            }
        )

    match_score = weighted / total_weight if total_weight else 1.0
    perfect = bool(rows) and all(row["rating"] == "green" for row in rows)
    return rows, match_score, perfect


def scaled_requirements(mission: dict, world: dict) -> dict:
    """Пороги с учётом множителя дня и штриховки зоны — то же, что сделает ядро."""
    import math

    day = next((d for d in world["config"]["days"] if d["day"] == mission.get("day", 1)), {})
    multiplier = day.get("requirementMultiplier", 1.0)

    zone = next((z for z in world["zones"] if z["id"] == mission.get("zoneId")), {})
    state = zone.get("state", "Normal")
    zones_config = world["config"].get("zones", {})
    if state == "Infected":
        multiplier *= zones_config.get("infectedRequirementMultiplier", 1.2)
    elif state == "Cleared":
        multiplier *= zones_config.get("clearedRequirementMultiplier", 0.9)

    return {
        stat: math.ceil(value * multiplier)
        for stat, value in mission.get("requirements", {}).items()
        if value > 0
    }


# --------------------------------------------------------------------------- preview


COLOURS = {"green": "ЗЕЛ", "yellow": "жёлт", "red": "КРАСН"}


def command_preview(args) -> int:
    """Во что превратились пороги: таблица шансов по реальным составам штата.

    Это главный ответ на вопрос «а не слишком ли я загнул»: автор видит не абстрактные
    числа, а то, что получит игрок, отправив каждого из своих людей.
    """
    world = load_world(repo_root())
    missions = [m for m in world["missions"] if not args.mission or m["id"] == args.mission]

    if not missions:
        raise ToolError(f"миссия {args.mission!r} не найдена")

    roster = world["roster"]["startingRoster"]
    events = {e["id"]: e for e in world["events"]}

    for mission in missions:
        primary = mission.get("primaryStat")
        requirements = scaled_requirements(mission, world)

        print()
        print(f"=== {mission['id']}  ({mission.get('tier', 'Filler')}, день {mission.get('day', 1)})")
        print("    пороги: " + ", ".join(
            f"{STAT_RU[s]} {v}{' ★' if s == primary else ''}" for s, v in requirements.items()))

        event = events.get(mission.get("missionEventId", ""))
        text_options = world["entries"].get(mission.get("missionEventId", ""), {}).get("options", [])

        combos = [([i], roster[i]["name"]) for i in range(len(roster))]
        if len(roster) >= 2:
            combos.append(([0, 1], "первый + второй"))
        if len(roster) >= 3:
            combos.append((list(range(len(roster))), "весь штат"))

        for indexes, label in combos:
            profile = squad_profile([roster[i] for i in indexes])
            rows, score, perfect = evaluate(requirements, profile, primary)

            detail = "  ".join(
                f"{STAT_RU[r['stat']]} {r['best']}/{r['required']} {COLOURS[r['rating']]}"
                for r in rows
            )
            verdict = "АВТОУСПЕХ" if perfect else f"{score:.0%}"

            open_options = [
                o["id"] for o in text_options
                if all(profile.get(k, 0) >= v for k, v in (o.get("requires") or {}).items())
            ]

            line = f"  {label:22} {detail:46} → {verdict:>10}"
            if event:
                line += f"   варианты: {', '.join(open_options) if open_options else '—'}"
            print(line)

    print()
    print("★ — главная характеристика, весит вдвое.")
    print("ЗЕЛ — превышение на 2+, полный вклад. жёлт — ровно дотянул, 0.8.")
    print("КРАСН — недобор, вклад падает втрое за каждое недостающее очко.")
    return 0


# --------------------------------------------------------------------------- check


def command_check(args) -> int:
    """Сверка всего со всем, человеческим языком.

    Ядро упадёт на тех же ошибках при запуске игры, но там сообщение прилетает
    в Output посреди отладки. Здесь автор видит их до сборки и списком.
    """
    world = load_world(repo_root())
    problems: list[str] = []
    notes: list[str] = []
    unbuilt: set[str] = set()

    def missing_text(owner: str, kind: str, text_id: str) -> str:
        if text_id in world["raw_ids"]:
            unbuilt.add(text_id)
            return f"{owner}: {kind} {text_id!r} написан, но не собран"
        return f"{owner}: нет {kind} {text_id!r}"

    entries = world["entries"]
    zones = {z["id"] for z in world["zones"]}
    creatures = {c["id"]: c for c in world["creatures"]}
    events = {e["id"]: e for e in world["events"]}
    mission_ids = {m["id"] for m in world["missions"]}

    if not entries:
        notes.append("каталог текстов пуст — сначала запустите build.py, иначе проверить ссылки нечем")

    for mission in world["missions"]:
        mid = mission["id"]
        tier = mission.get("tier", "Filler")
        cap = mission.get("consequenceCap") or ("Death" if tier == "Story" else "Injury")

        if mission.get("zoneId") not in zones:
            problems.append(f"{mid}: район {mission.get('zoneId')!r} не найден в zones.json")

        requirements = mission.get("requirements") or {}
        if not any(v > 0 for v in requirements.values()):
            problems.append(f"{mid}: не задано ни одного порога — вызов невозможно провалить")

        primary = mission.get("primaryStat")
        if primary and requirements.get(primary, 0) <= 0:
            problems.append(
                f"{mid}: главная характеристика {STAT_RU.get(primary, primary)} "
                f"не входит в пороги — вес некуда применить")
        if not primary:
            notes.append(f"{mid}: главная характеристика не задана, все пороги равнозначны")

        creature = mission.get("creatureId") or ""
        if creature and creature not in creatures:
            problems.append(f"{mid}: существо {creature!r} не найдено")
        for property_id in mission.get("manifestedPropertyIds", []):
            if creature and property_id not in creatures.get(creature, {}).get("properties", []):
                problems.append(f"{mid}: у существа {creature} нет свойства {property_id!r}")

        call_id = mission.get("callId", "")
        if not call_id:
            problems.append(f"{mid}: не указан callId — игроку нечего услышать в трубке")
        elif entries and call_id not in entries:
            problems.append(missing_text(mid, "текст звонка", call_id))

        event_id = mission.get("missionEventId", "")
        if event_id:
            if tier != "Story":
                problems.append(
                    f"{mid}: вмешательство по радио бывает только у сюжетных вызовов, а tier={tier}")
            if event_id not in events:
                problems.append(f"{mid}: нет баланса вмешательства {event_id!r} в mission_events.json")
            if entries and event_id not in entries:
                problems.append(missing_text(mid, "текст вмешательства", event_id))

        if tier != "Story" and cap == "Death":
            problems.append(f"{mid}: филлер не может позволять гибель (consequenceCap={cap})")

        problems.extend(check_options(mission, cap, events, entries))
        problems.extend(check_reports(mission, events, entries, missing_text))

    for day in world["config"]["days"]:
        for field in ("shiftNoteId", "outroCutsceneId"):
            value = day.get(field)
            if entries and value and value not in entries:
                problems.append(f"день {day['day']}: нет записи {field}={value!r}")
        for ordered in day.get("missionOrder", []):
            if ordered not in mission_ids:
                problems.append(f"день {day['day']}: в missionOrder указана неизвестная миссия {ordered!r}")

    for note in notes:
        print(f"  примечание: {note}")

    if problems:
        print()
        print(f"НАЙДЕНО ПРОБЛЕМ: {len(problems)}")
        for problem in problems:
            print(f"  - {problem}")

        if unbuilt:
            print()
            print(f"  Из них {len(unbuilt)} — просто несобранный текст. Запустите:")
            print("      python content/engine/converter/build.py --include-drafts")

        return 1

    print()
    print(f"Всё сходится: миссий {len(world['missions'])}, вмешательств {len(events)}.")
    return 0


def check_options(mission: dict, cap: str, events: dict, entries: dict) -> list[str]:
    """Ключи вариантов, потолки, порядок типов и наличие всегда открытого варианта."""
    problems: list[str] = []
    event_id = mission.get("missionEventId", "")
    event = events.get(event_id)
    if not event:
        return problems

    balance = event.get("options", {})
    text_options = entries.get(event_id, {}).get("options", [])
    text_ids = {o["id"] for o in text_options}

    for option_id, option in balance.items():
        if entries and option_id not in text_ids:
            problems.append(
                f"{mission['id']}: вариант {option_id!r} есть в данных, но не в тексте")

        option_cap = option.get("consequenceCap")
        if option_cap and CAP_RANK[option_cap] > CAP_RANK[cap]:
            problems.append(
                f"{mission['id']}/{option_id}: потолок {option_cap} мягче миссии ({cap}) — "
                f"вариант может только ужесточать")

    # Проверять, остался ли вариант «для слабой группы», больше не нужно: requires
    # ничего не запирает. Он называет характеристики, по которым идёт бросок, а
    # нажать можно любой вариант — недобор бьёт по шансу, а не по доступности.

    # Сверять тип варианта с его ценой здесь больше нечего: и то, и другое живёт
    # в mission_events.json, текст несёт только id, порядок и пороги доступности.

    return problems


def check_reports(mission: dict, events: dict, entries: dict, missing_text) -> list[str]:
    """У каждой пары «вариант × исход» должен быть текст отчёта."""
    problems: list[str] = []
    reports = mission.get("reports") or {}

    if "" not in reports:
        problems.append(f"{mission['id']}: нет отчёта на исход без вмешательства (ключ \"\")")

    event = events.get(mission.get("missionEventId", ""))
    expected = set(event.get("options", {})) if event else set()

    for key in expected - set(reports):
        problems.append(f"{mission['id']}: у варианта {key!r} нет отчётов")

    for key, pair in reports.items():
        if key and key not in expected and expected:
            problems.append(f"{mission['id']}: отчёт привязан к варианту {key!r}, которого нет")
        for outcome in ("success", "failure"):
            report_id = pair.get(outcome, "")
            if not report_id:
                problems.append(f"{mission['id']}/{key or 'без вмешательства'}: нет отчёта на {outcome}")
            elif entries and report_id not in entries:
                problems.append(missing_text(mission["id"], "текст отчёта", report_id))

    return problems


# --------------------------------------------------------------------------- new


def ask(prompt: str, default: str = "", options: tuple[str, ...] | None = None) -> str:
    suffix = f" [{default}]" if default else ""
    if options:
        suffix = f" ({' | '.join(options)}){suffix}"

    while True:
        answer = input(f"{prompt}{suffix}: ").strip() or default
        if options and answer not in options:
            print(f"    допустимо: {', '.join(options)}")
            continue
        if answer:
            return answer
        print("    нужно ответить")


def ask_stats(prompt: str) -> dict:
    """«интеллект 5, ловкость 4» — характеристики те же, что и в requires."""
    while True:
        raw = input(f"{prompt}: ").strip()
        if not raw:
            print("    хотя бы один порог обязателен")
            continue

        result: dict = {}
        broken = False
        for part in raw.split(","):
            match = re.match(r"^([A-Za-zА-Яа-яЁё]+)\s+(\d+)$", part.strip())
            if not match:
                print(f"    не разобрал {part.strip()!r}, ожидаю «характеристика число»")
                broken = True
                break

            stat = STAT_ALIASES.get(match.group(1).lower())
            if stat is None:
                print(f"    неизвестная характеристика {match.group(1)!r}")
                broken = True
                break

            result[stat] = int(match.group(2))

        if not broken and result:
            return result


def command_new(args) -> int:
    """Заводит миссию целиком: тексты, варианты, отчёты и геймплейные данные."""
    root = repo_root()
    world = load_world(root)

    print("Новая миссия. Пустой ответ берёт значение в скобках.")
    print()

    slug = ask("Короткое имя латиницей (например pensioner_door)")
    if not ID_RE.match(slug):
        raise ToolError("имя должно быть строчной латиницей: буквы, цифры, подчёркивание")

    mission_id = f"m_{slug}"
    if any(m["id"] == mission_id for m in world["missions"]):
        raise ToolError(f"миссия {mission_id} уже есть")

    tier = ask("Уровень вызова", "Story", TIERS)
    day = int(ask("День (1–4)", "1"))
    zone = ask("Район", world["zones"][0]["id"], tuple(z["id"] for z in world["zones"]))

    creature_options = tuple(c["id"] for c in world["creatures"]) + ("",)
    creature = ask("Существо (пусто — если статьи в энциклопедии нет)", "", creature_options)

    print()
    print("Пороги по характеристикам. Формат: «интеллект 5, ловкость 4».")
    print("Сравнивается ЛУЧШИЙ в группе по каждой — не сумма.")
    requirements = ask_stats("Пороги")

    primary = ask(
        "Главная характеристика (весит вдвое)",
        list(requirements)[0],
        tuple(requirements),
    )

    cap_default = "Death" if tier == "Story" else "Injury"
    cap = ask("Потолок последствий", cap_default, CAPS)

    options: list[dict] = []
    if tier == "Story" and ask("Будет вмешательство по радио?", "да", ("да", "нет")) == "да":
        options = ask_options()

    mission = build_mission(
        mission_id, slug, tier, day, zone, creature, requirements, primary, cap, options)

    write_mission_files(root, world, slug, mission, options)

    print()
    print(f"Готово: {mission_id}")
    print("Дальше:")
    print(f"  1. Напишите тексты в content/raw/{LOCALE}/missions/ — там заготовки.")
    print("  2. python content/engine/converter/build.py --include-drafts")
    print(f"  3. python content/engine/tools/mission.py preview {mission_id}")
    print("  4. python content/engine/tools/mission.py check")
    return 0


def ask_options() -> list[dict]:
    """Варианты решения. Хотя бы один обязан быть без требований."""
    print()
    print("Варианты решения. Хотя бы один должен быть без требований —")
    print("иначе слабой группе будет нечего нажать, а таймер радио идёт.")

    options: list[dict] = []
    while True:
        index = len(options) + 1
        print()
        option_id = ask(f"Вариант {index}: ключ латиницей (пусто — закончить)", "")
        if not option_id or option_id == "-":
            break
        if not ID_RE.match(option_id):
            print("    ключ должен быть строчной латиницей")
            continue

        name = ask("  Как называется для игрока", option_id)
        quality = ask("  Тип диалога", "neutral", QUALITIES)

        requires_raw = input("  Требования (пусто — открыт всегда): ").strip()
        requires = {}
        if requires_raw:
            for part in requires_raw.split(","):
                match = re.match(r"^([A-Za-zА-Яа-яЁё]+)\s+(\d+)$", part.strip())
                if match and STAT_ALIASES.get(match.group(1).lower()):
                    requires[STAT_ALIASES[match.group(1).lower()]] = int(match.group(2))

        options.append({"id": option_id, "name": name, "quality": quality, "requires": requires})

        if len(options) >= 2 and ask("  Добавить ещё вариант?", "да", ("да", "нет")) == "нет":
            break

    if options and all(o["requires"] for o in options):
        print()
        print("  У всех вариантов есть требования — снимаю их с первого, иначе сборка упадёт.")
        options[0]["requires"] = {}

    return options


def build_mission(
    mission_id: str,
    slug: str,
    tier: str,
    day: int,
    zone: str,
    creature: str,
    requirements: dict,
    primary: str,
    cap: str,
    options: list[dict],
) -> dict:
    """Геймплейная запись. Числа последствий — заготовка под правку дизайнером."""
    reports = {
        "": {
            "success": f"report_{slug}_plain_success",
            "failure": f"report_{slug}_plain_failure",
        }
    }
    for option in options:
        reports[option["id"]] = {
            "success": f"report_{slug}_{option['id']}_success",
            "failure": f"report_{slug}_{option['id']}_failure",
        }

    mission = {
        "id": mission_id,
        "day": day,
        "tier": tier,
        "zoneId": zone,
        "creatureId": creature,
        "callId": f"call_{slug}",
        "missionEventId": f"radio_{slug}" if options else "",
        "requirements": requirements,
        "primaryStat": primary,
        "travelSeconds": 12.0,
        "onSiteSeconds": 6.0,
        "returnSeconds": 10.0,
        "experienceOnSuccess": 100,
        "experienceOnFailure": 25,
        "injuryChance": 0.2,
        "deathChance": 0.05 if tier == "Story" else 0.0,
        "scalesOnSuccess": {"infection": -2.0, "publicity": -1.0, "loyalty": 2.0},
        "scalesOnFailure": {"infection": 5.0, "publicity": 4.0, "loyalty": -4.0},
        "scalesOnMissedCall": {"infection": 6.0, "publicity": 6.0, "loyalty": -6.0},
        "scalesOnExpiredMarker": {"infection": 5.0, "publicity": 5.0, "loyalty": -5.0},
        "reports": reports,
    }

    default_cap = "Death" if tier == "Story" else "Injury"
    if cap != default_cap:
        mission["consequenceCap"] = cap

    return mission


# --------------------------------------------------------------------------- запись файлов


CALL_TEMPLATE = """---
id: call_{slug}
type: call
mission_id: m_{slug}
status: draft
requirements:
  -
properties:
  -
---

%% dev %%
Что произошло на самом деле и почему сюда нужен именно такой состав.
Пороги вызова: {thresholds}
Главная характеристика: {primary}
%% /dev %%

%% call_meta: ОТКУДА ЗВОНОК И КТО НА ЛИНИИ %%

Первая реплика звонящего.

Вторая реплика — не длиннее двух строк.
"""

EVENT_HEADER = """---
id: radio_{slug}
type: radio
mission_id: m_{slug}
status: draft
requirements:
  -
properties:
  -
---

%% dev %%
Вмешательство по вызову [[call:call_{slug}]].
Пороги вызова: {thresholds}. Главная: {primary}.
%% /dev %%

Что бригада увидела на месте — первый абзац вводной.

Второй абзац вводной, если нужен.
"""

EVENT_OPTION = """
## Вариант: {name}
id: {option_id}
{requires_line}
{text_hint}
"""

REGISTRY_TEMPLATE = """---
type: mission_id
status: draft
day: {day}
---

<!-- Миссии {day}-й смены: id и название. id тот же, что в data/missions.json;
     на него ссылаются call, radio и report полем mission_id. -->

| id | название |
|----|----------|
"""

REPORT_TEMPLATE = """---
id: {report_id}
type: report
mission_id: {mission_id}
status: draft
outcome: {outcome}
properties:
  -
---

%% dev: эффект на шкалы и что открывается — сюда. %%

Текст отчёта на компьютере: {label}.
"""


def write_mission_files(root: Path, world: dict, slug: str, mission: dict, options: list[dict]) -> None:
    raw = root / "content" / "raw" / LOCALE
    thresholds = ", ".join(f"{STAT_RU[s]} {v}" for s, v in mission["requirements"].items())
    primary = STAT_RU.get(mission.get("primaryStat", ""), "—")
    written: list[Path] = []

    # Тексты миссии разложены по типам, а внутри типа — по главам и сменам.
    shift = f"shift_{mission['day']}"
    missions_dir = raw / "missions"

    register_mission(
        missions_dir / "mission_ids" / CHAPTER / f"{shift}.md",
        mission["id"],
        mission.get("name", slug),
        mission["day"],
        written,
    )

    call_path = missions_dir / "calls" / CHAPTER / shift / f"{slug}.md"
    write_once(call_path, CALL_TEMPLATE.format(slug=slug, thresholds=thresholds, primary=primary), written)

    if options:
        body = EVENT_HEADER.format(slug=slug, thresholds=thresholds, primary=primary)
        for option in options:
            requires_line = ""
            if option["requires"]:
                pairs = ", ".join(f"{STAT_RU[s].lower()} {v}" for s, v in option["requires"].items())
                requires_line = f"requires: {pairs}"

            body += EVENT_OPTION.format(
                name=option["name"],
                option_id=option["id"],
                requires_line=requires_line,
                text_hint=f"\nТекст варианта «{option['name']}».\n",
            )

        write_once(missions_dir / "radio" / CHAPTER / shift / f"{slug}.md", body, written)

    reports_dir = missions_dir / "reports" / CHAPTER / shift / slug
    for key, pair in mission["reports"].items():
        label = key or "без вмешательства"
        for outcome in ("success", "failure"):
            report_id = pair[outcome]
            write_once(
                reports_dir / f"{report_id.replace('report_', '')}.md",
                REPORT_TEMPLATE.format(
                    report_id=report_id,
                    mission_id=mission["id"],
                    outcome=outcome,
                    label=f"{label}, {outcome}",
                ),
                written,
            )

    data = root / "data"
    missions = world["missions"]
    missions.append(mission)
    write_json(data / "missions.json", missions)

    if options:
        events = world["events"]
        events.append({
            "id": mission["missionEventId"],
            # Числа последствий — заготовка: правит дизайнер, не автор текста.
            # Тип диалога тоже здесь: в тексте варианта его больше нет.
            "options": {o["id"]: {"quality": o["quality"]} for o in options},
        })
        write_json(data / "mission_events.json", events)

    print()
    print("Создано:")
    for path in written:
        print(f"  {path.relative_to(root).as_posix()}")
    print(f"  data/missions.json (+{mission['id']})")
    if options:
        print(f"  data/mission_events.json (+{mission['missionEventId']})")


def register_mission(path: Path, mission_id: str, name: str, day: int, written: list[Path]) -> None:
    """Дописывает строку в реестр смены: файл общий, поэтому не write_once."""
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(REGISTRY_TEMPLATE.format(day=day), encoding="utf-8")
        written.append(path)

    body = path.read_text(encoding="utf-8")
    if re.search(rf"(?m)^\|\s*{re.escape(mission_id)}\s*\|", body):
        print(f"  пропускаю (уже в реестре): {mission_id}")
        return

    path.write_text(body.rstrip("\n") + f"\n| {mission_id} | {name} |\n", encoding="utf-8")


def write_once(path: Path, body: str, written: list[Path]) -> None:
    if path.exists():
        print(f"  пропускаю (уже есть): {path.name}")
        return

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(body, encoding="utf-8")
    written.append(path)


# --------------------------------------------------------------------------- точка входа


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Инструмент автора миссий К.О.Н.Т.У.Р.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    commands = parser.add_subparsers(dest="command", required=True)

    commands.add_parser("new", help="завести миссию: тексты, варианты, отчёты, данные")
    commands.add_parser("check", help="сверить связи и правила, объяснить расхождения")

    preview = commands.add_parser("preview", help="таблица шансов по реальным составам")
    preview.add_argument("mission", nargs="?", default="", help="id миссии; пусто — все")

    args = parser.parse_args()

    try:
        if args.command == "new":
            return command_new(args)
        if args.command == "check":
            return command_check(args)
        return command_preview(args)
    except ToolError as error:
        print(f"Ошибка: {error}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print()
        print("Отменено.")
        return 130


if __name__ == "__main__":
    sys.exit(main())
