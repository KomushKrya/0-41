# AGENTS.md

## Project

This repository contains a Godot 4.x .NET / C# game project set in the К.О.Н.Т.У.Р / O-41 universe.

The game is planned as an immersive 3D investigation / document-processing experience with interactive environments, diegetic interfaces, case files, reports, terminals, fax/print devices, phone calls, and procedural or authored incident handling.

The current early prototype starts with a simple 3D room and a first-person / floating camera controller, but the repository should be treated as a full game project, not as a throwaway scene.

## Engine and language

* Engine: Godot 4.x .NET
* Language: C#
* Main scene: `res://scenes/main.tscn`
* Do not rewrite the project to GDScript unless explicitly requested.
* Prefer Godot-native C# patterns and simple scene composition.
* Avoid introducing large external frameworks without approval.

## High-level game direction

The intended gameplay is a room-based immersive interface inspired by games where the player interacts with a physical workspace.

Core fantasy:

* the player works as an operator / investigator inside a К.О.Н.Т.У.Р office;
* the player receives incident requests, reports, or alerts;
* the player checks employee folders, case files, object records, maps, terminal data, fax printouts, and phone calls;
* the player makes decisions based on incomplete, bureaucratic, and sometimes anomalous information;
* the world should feel like a Soviet/post-Soviet classified organization dealing with O-41 anomalies.

The game should prioritize:

* atmosphere;
* readable interaction;
* believable diegetic UI;
* modular systems;
* expandable content;
* simple but maintainable code.

## Current milestone

The current milestone is a greybox prototype:

* a basic 3D room;
* player/camera movement with collision;
* grouped room geometry;
* simple collision shapes;
* early scene organization;
* future interactive desk objects.

Current planned interactables:

* work PC / terminal;
* employee folder;
* fax or printer for incident requests;
* telephone;
* paper documents;
* object/case archive;
* map or wall board.

Do not treat this milestone as the final scope. It is only the foundation.

## Repository layout

Preferred structure:

```text
scenes/
  main.tscn
  room/
  player/
  interactables/
  ui/
  documents/
  systems/

scripts/
  player/
  interaction/
  camera/
  ui/
  documents/
  cases/
  systems/

assets/
  models/
  materials/
  textures/
  audio/
  fonts/

data/
  cases/
  employees/
  objects/
  localization/
```

Keep this structure flexible while the project is small, but avoid dumping everything into the root directory.

## Godot scene conventions

* Use clear scene names.
* Use clear node names.
* Prefer modular scenes for reusable objects.
* Keep the main scene readable.
* Avoid huge monolithic scenes when functionality can be split into sub-scenes.
* Use simple collision shapes for gameplay even when visual meshes become more detailed.
* Prefer composition over deep inheritance.
* Avoid hardcoded node paths when exported references are more maintainable.

Examples of future reusable scenes:

```text
Player.tscn
RoomOffice.tscn
DeskPc.tscn
FaxMachine.tscn
Phone.tscn
EmployeeFolder.tscn
DocumentPage.tscn
CaseFile.tscn
```

## C# coding style

* Use C# for gameplay code.
* Use PascalCase for class names, methods, and public properties.
* Use `_camelCase` for private fields.
* Match class names and file names.
* Prefer small scripts with one clear responsibility.
* Use `[Export]` for values that should be tuned in the editor.
* Avoid magic numbers when values should be configurable.
* Keep code readable before making it clever.
* Do not over-engineer early systems.
* Add comments only when they clarify intent, not when they repeat the code.

Example naming:

```text
FlyPlayer.cs
InteractionRaycaster.cs
InteractableObject.cs
CameraFocusController.cs
FaxMachine.cs
CaseDatabase.cs
EmployeeRecord.cs
```

## Interaction system direction

The project will likely need a reusable interaction system.

Preferred direction:

* player looks at an object;
* raycast detects interactable target;
* UI hint appears;
* player presses an interaction key;
* object performs an action or moves camera focus;
* interaction can open diegetic UI, document view, terminal screen, phone call, etc.

Avoid building interaction logic separately inside every object if a shared interface or base component would be cleaner.

Potential future interface:

```csharp
public interface IInteractable
{
    string InteractionLabel { get; }
    void Interact();
}
```

Do not add this interface until it is actually needed by the implementation.

## Content and data direction

The game will contain authored and possibly structured content:

* incidents;
* employees;
* objects;
* protocols;
* case files;
* reports;
* phone scripts;
* fax messages;
* terminal entries.

Prefer storing larger content outside hardcoded C# scripts when practical.

Possible future formats:

* JSON;
* `.tres` resources;
* custom Godot resources;
* plain text files for authored documents.

Do not prematurely create a complex database system. Start simple and evolve the structure as content grows.

## Visual development direction

Early development should use greybox geometry and simple materials.

Later development may add:

* Blender-made models;
* low-poly or stylized office props;
* Soviet/post-Soviet equipment;
* CRT monitors;
* paper folders;
* fax/printer devices;
* lamps;
* shelves;
* archival boxes;
* wall maps;
* document stamps;
* analog UI elements.

Gameplay collision should remain simple even if visual meshes become detailed.

## Atmosphere and UI direction

The project should favor diegetic interfaces:

* physical terminal screens;
* printed forms;
* folders;
* paper tabs;
* stamps;
* archive drawers;
* phone calls;
* fax pages;
* warning lights;
* in-world monitors.

Avoid generic floating UI unless it is needed for debugging or accessibility.

Debug UI is allowed during development but should be clearly separated from final diegetic UI.

## Git workflow

* Work on feature branches, not directly on `main`.
* The current development branch is `andy-branch`.
* Commit small logical changes.
* Use clear commit messages.

Good commit examples:

```text
Add C# flying player controller
Add room collision blockout
Organize room hierarchy
Add basic interaction raycast
Add fax machine placeholder scene
Add AGENTS project instructions
Update Godot C# gitignore
```

Avoid commits like:

```text
fix
stuff
changes
123
```

## Files that should usually be committed

Commit:

```text
project.godot
*.tscn
*.tres
*.cs
*.csproj
*.sln
README.md
AGENTS.md
assets used by the project
data files used by the project
```

Do not commit local editor caches, temporary files, or build outputs.

## Files that should usually be ignored

Ignore:

```text
.godot/
.import/
.export/
.mono/
bin/
obj/
.vs/
.idea/
.vscode/
*.tmp
*.temp
*.log
.DS_Store
Thumbs.db
desktop.ini
```

## Testing expectations

Before committing gameplay changes:

* open the project in Godot .NET;
* build the C# project;
* run the main scene;
* check the Output panel for errors;
* verify player movement and camera controls if touched;
* verify changed interactables still work;
* avoid committing broken scenes unless explicitly marked as work-in-progress.

## Agent behavior rules

When working as an AI coding agent:

* inspect the existing structure before changing files;
* preserve the Godot project layout unless asked to reorganize it;
* avoid unrelated refactors;
* do not change engine version without approval;
* do not add dependencies without approval;
* do not rewrite systems from scratch if a small fix is enough;
* keep changes focused on the requested task;
* explain what files were changed and why;
* mention any assumptions;
* mention any manual Godot editor steps that cannot be done through code alone.

## Scope guidance for agents

Agents may help with:

* C# gameplay scripts;
* Godot scene organization;
* interaction systems;
* camera systems;
* document/folder UI logic;
* data structures for cases and employees;
* prototype mechanics;
* editor-safe refactoring;
* code cleanup;
* README and documentation;
* `.gitignore` maintenance;
* debugging Godot errors.

Agents should ask before:

* adding third-party plugins;
* changing the Godot version;
* changing project renderer settings;
* introducing a new architecture pattern;
* replacing existing scene hierarchy;
* deleting assets or scenes;
* making large-scale rewrites.

## Long-term development direction

The project may grow beyond the first room prototype.

Possible future systems:

* case management;
* incident generation;
* employee database;
* object archive;
* branching decisions;
* phone dialogue;
* fax/print queue;
* terminal interface;
* document inspection;
* time progression;
* room state changes;
* anomaly events;
* save/load;
* localization;
* accessibility settings.

Build systems incrementally. Prefer playable vertical slices over large unfinished abstractions.

## Current priority

The immediate priority is to establish a stable C# Godot foundation:

1. clean repository structure;
2. correct `.gitignore`;
3. C# player controller;
4. organized room scene;
5. basic collision;
6. simple interaction foundation;
7. first desk interactable prototype.

After that, expand toward full gameplay systems.
