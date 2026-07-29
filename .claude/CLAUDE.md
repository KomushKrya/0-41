# Project: К.О.Н.Т.У.Р.

First-person office-sim game (Godot 4.7, Mono/C#) about an operator at a
regional department containing anomaly outbreaks. Demo scope: 4 in-game
shifts/days. Core architectural pattern: Event Bus. Design doc:
[README.md](README.md) (canonical — the duplicate `ДИЗАЙН-ДОКУМЕНТ_v1.0.md`
was deleted).

## My role

Text engine (Markdown → JSON pipeline) and game texts: calls, on-site
mission events, creature encyclopedia, shift notes, computer reports, plus
story. Not gameplay code (map, movement, interaction) — that's a separate
track, though it lives in the same repo since PR #1 merged it into `main`.

## Repo state

Godot C#/.NET prototype at repo root: `project.godot`,
`kontur_prototype.csproj` (net8.0, `Godot.NET.Sdk/4.7.0`), `scenes/`,
`scripts/{gameplay,computer,interaction,ui,debug}/`,
`tools/map-geometry-generator/`.

- **Working branch is `content`**, not `main` — text-engine work is
  committed there and not yet merged back.
- `AGENTS.md` (root) governs the C# side: **Godot 4.x .NET only, never
  rewrite to GDScript unless explicitly asked**. It is gitignored and
  untracked (as are `.claude/` and `.mcp.json`).
- `КОНТУР/` (root) — stray jam-era Obsidian vault, unrelated to
  `content/raw/`. Its `.obsidian/` was untracked from git; the vault's own
  files are still tracked. Fate still undecided.
- `venv/` **was deleted** by the user. This broke the godot MCP server —
  `.mcp.json` launches it via `venv/Scripts/python.exe -m godot_mcp_server`.
  Recreate the venv and reinstall `godot-mcp-server` if that MCP is needed.
  The converter does **not** need it (system Python 3.12, zero deps).
- .NET 8 SDK (8.0.423) is installed; `dotnet build kontur_prototype.csproj`
  is clean. If Godot's own build fails with `MSB1021`/SDK-7 paths, the
  editor is holding a stale SDK list — restart it.

## Content schema (settled)

Six types, one folder each under `content/raw/`:

| folder | type | notes |
|---|---|---|
| `calls/` | `call` | incoming phone calls |
| `cutscenes/` | `cutscene` | transitions, intro, all endings |
| `mission_events/` | `mission_event` | on-site events (was `radio`) |
| `creatures/` | `creature` | encyclopedia |
| `shift_notes/` | `shift_note` | |
| `reports/` | `report` | only extra field is `outcome` |

- **Frontmatter holds only text-side fields**: `id`, `type`, `status`, plus
  `requirements` and `properties` on every type, and `name`/`day`/`outcome`
  where relevant. Mission requirement numbers, incident→creature links, etc.
  live in Godot-side gameplay data and reference text by `id` — never the
  other way round, to avoid two sources of truth.
- `requirements` = flags gating whether the whole entry may appear.
  `properties` = ids of conditional sub-blocks *inside* an entry (only
  `creature` uses it today, for `%% reveal %%`). Deliberately separate
  fields — one gates the document, the other indexes blocks within it.
- `call` and `cutscene` are separate types but share the bottom-text widget.
  **Their chunks must fit ~2 lines (≤150 chars)** — the widget is
  transparent and centered, so long paragraphs look wrong. The converter
  warns past the limit (`--max-chunk-chars`), and the rule applies *only* to
  these two types; encyclopedia/report render on their own roomier screens.
- Body chunking: blank line = one chunk = one click.
- Custom tags: `[[type:id]]` cross-references (build fails if the target is
  missing), `%% reveal: property_id %% … %% /reveal %%` for creature
  paragraphs. The engine only marks conditionality — whether a flag is set
  is decided by gameplay code, ideally one shared condition/flag service on
  `GameSession`, not a separate checker per widget.
- `content/raw/_system/` is author-facing scaffolding the converter skips
  (leading `_`): `Templates/`, `Syntax/README.md`, auto-generated
  `ids_registry.md` (**never hand-edit**), `chapters/` Excalidraw canvases.
- Markdown style: one paragraph = one line. No manual wrapping at ~80 chars
  — it creates noisy diffs and means nothing to the renderer.

## What is built

- **Converter** — `content/engine/converter/build.py`, no dependencies. Run
  `python content/engine/converter/build.py` from the repo root;
  `--include-drafts` also builds `status: draft` entries. Validates id
  uniqueness, type-vs-folder mismatch, `properties` against actual
  `%% reveal %%` blocks, dangling `[[type:id]]`, non-UTF-8 files; reports
  every error at once and exits 1. Regenerates `ids_registry.md`.
- **Output** — `content/localisation/<locale>/<folder>/<type>.json`. Folder
  names mirror `content/raw/` (user's explicit requirement).
- **Loader** — `content/engine/content/Content.cs` + `ContentEntry.cs`,
  registered as the `Content` autoload. Walks the locale dir recursively,
  exposes `GetChunks(id)` / `GetEntry(id)` / `TryGetEntry(id)`.
- **Bottom-text widget** — `scripts/ui/BottomTextRenderer.cs` +
  `scenes/ui/bottom_text/BottomTextUI.tscn`, instanced in `main.tscn`.
  Right-click publishes `RequestBottomTextAdvance()`; the renderer is a
  subscriber, so anything else (debug key, autoplay) can drive it too.
  First advance loads `StartupContentId` (`cutscene_intro`), later ones page
  through, the last one publishes `BottomTextFinished` and `QueueFree()`s.
- **EventBus additions**: `BottomTextAdvanceRequested`, `BottomTextStarted`,
  `BottomTextFinished`.
- Note: the project does **not** enable `#nullable`, despite the `null!`
  idiom in existing code — `?` annotations produce CS8632 warnings. Keep the
  build at 0 warnings.

## Content written so far

`creatures/{mimic,perekozhnik,simulacre}.md` (still `draft`, so they don't
land in a normal build) and `cutscenes/intro.md` (`ready`). Canon source is
the user's own fandom wiki, which cannot be fetched automatically —
WebFetch returns 402 and the in-app browser blocks the domain, so the user
pastes article text in manually.

## Open questions

- Fate of the stray `КОНТУР/` vault.
- `requirement_modifier` format for mission-event options
  (absolute/percent/stat-specific).
- `properties` is currently meaningful only for `creature`; on the other
  five types it is an empty placeholder with no mechanism behind it.
- JSON under `content/localisation/` is read fine in-editor, but exported
  builds need it added to the export preset's non-resource file filter.
- Nothing has been verified running in-game yet — only builds and generated
  data were checked.
