"""Готовит временный контент для снимка досье: два случайных сотрудника с двумя перками.

Пара к tools/editor/_shot_dossier_perks.gd: тот берёт результат из
temp/dossier_shot_data/ и снимает разворот. Второй аргумент — seed, без него случайный.

Правила совпадают с EmployeeFactory: уровень 3 (второй перк выдаётся с уровня 3),
бюджет статов statPointsBase + statPointsPerLevel * (level - 1), веса архетипа,
перки — два разных из пула архетипа, био — по строке на слот.
"""
import json
import os
import random
import shutil
import sys

SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DATA = os.path.join(SRC, "data")
OUT = os.path.join(SRC, "temp", "dossier_shot_data")
BIO = os.path.join(SRC, "content", "localisation", "ru", "personnel", "bio", "bio_line.json")

seed = int(sys.argv[2]) if len(sys.argv) > 2 else random.randrange(1 << 30)
rnd = random.Random(seed)

os.makedirs(OUT, exist_ok=True)
for name in os.listdir(DATA):
    if name.endswith(".json"):
        shutil.copyfile(os.path.join(DATA, name), os.path.join(OUT, name))

employees = json.load(open(os.path.join(DATA, "employees.json"), encoding="utf-8"))
gen = employees["generator"]
bio_ids = list(json.load(open(BIO, encoding="utf-8")).keys())

STATS = ["strength", "combat", "agility", "charisma", "intellect"]
LEVEL = max(gen["secondAbilityFromLevel"], 1)


def roll_stats(archetype):
    stats = {kind: gen["minStat"] for kind in STATS}
    weights = {kind: 1.0 for kind in STATS}
    for kind in archetype.get("secondary", []):
        weights[kind] = gen["secondaryWeight"]
    for kind in archetype.get("primary", []):
        weights[kind] = gen["primaryWeight"]

    budget = gen["statPointsBase"] + gen["statPointsPerLevel"] * (LEVEL - 1)
    for _ in range(budget):
        pool = [kind for kind in STATS if stats[kind] < gen["maxStat"]]
        if not pool:
            break
        stats[rnd.choices(pool, [weights[kind] for kind in pool])[0]] += 1
    return stats


def pick_bio():
    lines = []
    for slot in gen["bioSlots"]:
        slot_lines = [i for i in bio_ids if i.startswith("bio_" + slot + "_")]
        if slot_lines:
            lines.append(rnd.choice(slot_lines))
    return lines


taken_names, taken_portraits, roster = set(), set(), []
for index in range(2):
    # Только архетипы, у которых в пуле есть два перка: иначе второй слот пустой.
    archetype = rnd.choice([a for a in gen["archetypes"] if len(a["abilities"]) >= 2])

    while True:
        name = rnd.choice(gen["surnames"]) + " " + rnd.choice(gen["initials"])
        if name not in taken_names:
            taken_names.add(name)
            break

    portraits = [p for p in gen["portraits"] if p not in taken_portraits] or gen["portraits"]
    portrait = rnd.choice(portraits)
    taken_portraits.add(portrait)

    roster.append({
        "id": f"emp_shot_{index + 1}",
        "name": name,
        "rankTitle": archetype["rankTitle"],
        "level": LEVEL,
        "portraitId": portrait,
        "stats": roll_stats(archetype),
        "abilities": rnd.sample(archetype["abilities"], 2),
        "age": rnd.randint(gen["minAge"], gen["maxAge"]),
        "bio": pick_bio(),
    })

employees["startingRoster"] = roster
# Ноль отключает автонабор — в ростер попадает именно этот состав.
employees["generator"]["startingRosterSize"] = 0
json.dump(employees, open(os.path.join(OUT, "employees.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

print("seed", seed)
for employee in roster:
    print(employee["name"], employee["rankTitle"], employee["abilities"])
