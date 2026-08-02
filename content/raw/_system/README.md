# _system

Служебные материалы для авторов. Конвертер пропускает всё под `_system/`.

- `Templates/` — заготовки под каждый тип, по одной на папку контента.
- `Syntax/` — фронтматтер и кастомные теги.
- `Keywords.md` — какие слова в вызовах подсвечиваем и какой навык они подсказывают.
- `ids_registry.md` — занятые `id`, генерируется конвертером. Руками не править.

## Раскладка

```
content/raw/
  _system/        служебное: шаблоны, синтаксис, реестр id — общее на все локали
  ru/             тексты русской локали
    missions/ cutscenes/ creatures/ equipment/ shift_notes/ UI/
```

Сборка идёт по одной локали за раз: `--locale ru` (по умолчанию) читает
`content/raw/ru/` и пишет в `content/localisation/ru/`. Новый язык — новая папка
рядом с `ru/`, той же структуры; `_system/` не дублируется.

## Папки контента

Пути ниже — от корня локали, то есть от `content/raw/ru/`.

| Папка | Тип | Шаблон |
|---|---|---|
| `missions/mission_ids/chapter_N/shift_N.md` | `mission_id` — реестр названий миссий смены | `Templates/mission_id.md` |
| `missions/calls/…` | `call` — входящие звонки | `Templates/call.md` |
| `missions/radio/…` | `radio` — вмешательства по рации | `Templates/radio.md` |
| `missions/reports/…/<миссия>/` | `report` — отчёты по исходам | `Templates/report.md` |
| `cutscenes/` | `cutscene` — интро, переходы, финалы | `Templates/cutscene.md` |
| `creatures/` | `creature` — энциклопедия | `Templates/creature.md` |
| `shift_notes/` | `shift_note` — записки сменщика | `Templates/shift_note.md` |
| `equipment/` | `equipment` — снаряжение | — |
| `UI/hover_footnote/perks/` | `perk` — спецспособности | `Templates/perk.md` |
| `UI/hover_footnote/characteristics/` | `characteristic` — характеристики | `Templates/characteristic.md` |
| `UI/hover_footnote/equipment_kinds/` | `equipment_kind` — виды снаряжения | — |
| `UI/hover_footnote/scales/` | `scale` — шкалы блокнота | — |

Вложенность внутри папки типа свободная: конвертер обходит её рекурсивно. У четырёх
папок под `missions/` она не свободная, а договорная — `chapter_<N>/shift_<N>/`, чтобы
все четыре текста одной миссии лежали на одинаковой глубине и находились глазами.

Связь между ними — поле `mission_id` в `call`, `radio` и `report`. Оно указывает на
строку реестра `missions/mission_ids/chapter_N/shift_N.md` — там таблица «id | название»,
одна строка на миссию, и своего `id` у самого файла нет: их раздаёт таблица. Сборка
падает, если `mission_id` пуст или ведёт в никуда. Название миссии не дублируется больше нигде,
а `id` совпадает с `id` миссии в `data/missions.json` — одна миссия, один ключ на
текстовую и геймплейную сторону.
