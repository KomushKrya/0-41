# _system

Служебные материалы для авторов. Конвертер пропускает всё под `_system/`.

- `Templates/` — заготовки под каждый тип, по одной на папку контента.
- `Syntax/` — фронтматтер и кастомные теги.
- `Keywords.md` — какие слова в вызовах подсвечиваем и какой навык они подсказывают.
- `ids_registry.md` — занятые `id`, генерируется конвертером. Руками не править.

## Папки контента

| Папка | Тип | Шаблон |
|---|---|---|
| `calls/shift_N/` | `call` — входящие звонки | `Templates/call.md` |
| `radio/` | `radio` — вмешательства по рации | `Templates/radio.md` |
| `cutscenes/` | `cutscene` — интро, переходы, финалы | `Templates/cutscene.md` |
| `creatures/` | `creature` — энциклопедия | `Templates/creature.md` |
| `reports/<миссия>/` | `report` — отчёты по исходам | `Templates/report.md` |
| `shift_notes/` | `shift_note` — записки сменщика | `Templates/shift_note.md` |
| `equipment/` | `equipment` — снаряжение | — |
| `UI/hover_footnote/perks/` | `perk` — спецспособности | `Templates/perk.md` |
| `UI/hover_footnote/characteristics/` | `characteristic` — характеристики | `Templates/characteristic.md` |
| `UI/hover_footnote/equipment_kinds/` | `equipment_kind` — виды снаряжения | — |
| `UI/hover_footnote/scales/` | `scale` — шкалы блокнота | — |

Вложенность внутри папки типа свободная: конвертер обходит её рекурсивно.
