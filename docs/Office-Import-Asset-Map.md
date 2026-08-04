# Office Import Asset Map

Исходный файл `assets/models/environment/import/кабинет.glb` удалён после разделения и проверки 45 самостоятельных GLB-файлов.

Редактируемая распакованная сцена: `scenes/environment/NewOffice.tscn`. Все 45 объектов в ней переименованы на английский.

Самостоятельные GLB-файлы находятся в `assets/models/environment/office_split` и распределены по каталогам `architecture`, `furniture`, `interactive`, `lighting` и `decor`. Исходные текстуры перенесены в каталоги `textures` соответствующих категорий, а общие — в `shared_textures`. Точные исходные трансформации и размеры записаны в `manifest.json`; проверка выполняется скриптом `tools/editor/validate_split_office_glb.gd`.

Интерактивные модели вынесены из базовой сцены в отдельные заготовки с корнем предмета и дочерним `VisualRoot`:

- `NewOfficeChair.tscn`: `OfficeChairFrame`, `OfficeChairSeat`;
- `NewWallMap.tscn`: `WallMapBoard`;
- `NewNotebook.tscn`: `Notebook`, `Pencil`;
- `NewDeskPhone.tscn`: `DeskPhone`;
- `NewRadioStation.tscn`: `Radio`, `RadioMicrophoneAccessories`, `DeskAccessory02`;
- `NewDeskComputer.tscn`: `Keyboard`, `ComputerMonitor`, `ComputerCase`, `ComputerControls`.

Все эти сцены находятся в `scenes/interactables/new_office`. Пока они содержат только итоговые визуальные меши и материалы; функциональные узлы, коллизии и скрипты старого кабинета будут переноситься отдельно.

Godot успешно импортировал GLB как сцену. В ней 45 узлов с мешами, 75 материалов, 139 текстурных ссылок и 136 изображений. Материалы уже назначены примитивам соответствующих мешей; их не нужно вручную переназначать после добавления сцены в уровень.

## Mesh-to-material mapping

| Imported node | Mesh | Assigned materials |
| --- | --- | --- |
| Cube | Cube | Centr, Ramka.001 |
| Cube.001 | Cube.001 | Bloknot, Colca |
| Cube.002 | Cube.003 | Material.002 |
| Cube.002_low | Cube.002 | Parts.002 |
| Cube.006 | Cube.007 | Black |
| Cube.007 | Cube.008 | drow |
| Cylinder | Cylinder | Color, Drov, grifil |
| Cylinder.001 | Cylinder.001 | Gold |
| Cylinder.002 | Cylinder.002 | Drov.001 |
| Cylinder.003 | Cylinder.003 | Material.003 |
| Cylinder.014 | Cylinder.015 | Material.001, korpus, BolshaiaKrutilka, metal.001, Paper, Ramka, ciferblat, metalMelkii, KrutilkiMalenkie, krutilkiMelkie2, Klepki, nasadki, metal, krutilki, [none] |
| microphone_accessories_0 | microphone_accessories_0 | accessories |
| Object_8 | Object_0 | hat_fedora |
| Phone_LowPoly.004 | Circle.006 | Material.008, Material.009, Material.010, Material.011, Material.012 |
| Plane | Plane.002 | monitor.002 |
| polySurface313__0 | polySurface313__0 | Scene_-_Root |
| Torus.008 | Torus.004 | Material.006, Material.007 |
| Torus.015 | Torus.005 | BlackMaterial.002, krutilki.003, korpus.003 |
| батарея | Цилиндр.006 | Материал.019 |
| большая стрелка | Цилиндр.003 | Материал.004 |
| вешалка | Цилиндр.008 | метал |
| диванчик | Куб.006 | Material.004, Материал.017, Материал.018 |
| Лампа | Цилиндр.013 | Материал.013 |
| маленькая стрелка | Цилиндр.004 | Материал.004 |
| окно | Куб.004 | Material, Материал.011 |
| окурок | Цилиндр.002 | Материал.002, Материал.003 |
| пепел | Цилиндр.001 | Материал.001 |
| пепельница | Цилиндр | Материал |
| Плоскость | Плоскость | Материал.014 |
| подстаканик | Цилиндр.009 | метал.001 |
| пол | Плоскость.001 | Colca |
| потолок | Плоскость.002 | Материал.016 |
| рамка | Куб.001 | рамка, вставка |
| рамка.001 | Куб.003 | рамка, вставка |
| ручка | Куб.005 | Материал.009 |
| стакан | Цилиндр.010 | Материал.010 |
| стена 1 | Плоскость.003 | Материал.015 |
| стена 2 | Плоскость.004 | Материал.015 |
| стена 3 | Плоскость.005 | Материал.015 |
| стена 4 | Плоскость.006 | Материал.015 |
| стол | Цилиндр.012 | дерево.002, Материал.009, Материал.012 |
| фикус | Цилиндр.007 | горшок, Материал.006, Материал.007 |
| часы | Цилиндр.005 | дерево, Материал.004, цифер блад |
| шкаф | Куб | дерево.003, резинка.001 |
| шкаф.001 | Куб.002 | дерево.001, Материал.008, Материал.009, резинка |

## Material texture families

The original PBR texture sets remain embedded and are extracted beside the GLB on import. The named material families are:

- `Doska_*`: wall board (`Centr`, `Ramka.001`);
- `Bloknot_*`: notebook and ring (`Bloknot`, `Colca`);
- `Dor_*`: door and handle (`Material.002`, `Gold`, `Drov.001`);
- `Clava_*`: keyboard (`Parts.002`);
- `Stul_*`: chair (`Black`, `drow`);
- `Karandash_*`: pencil (`Color`, `Drov`, `grifil`);
- `Radio_*`: radio housing, dials, faceplate, paper and metal;
- `PhoneColorMap_*`: desk phone body, dial, cable and metal;
- `PCNew_*` and `PC_*`: monitor/computer housing and controls.

Every family above keeps its base-color texture; the first seven and the computer/phone/radio families also keep their metallic-roughness and normal texture assignments where supplied by the source GLB.

## Mapping to the active game scene

The GLB should replace visual geometry only. The following current nodes contain scripts, collision, interaction areas, focus camera poses or UI links and must remain in `scenes/main.tscn` when its visual model is replaced:

| Active game node | Imported visual counterpart | Integration note |
| --- | --- | --- |
| `Room/Floor` | `пол` | Replace visual mesh; keep room collision separately. |
| `Room/Ceiling` | `потолок` | Replace visual mesh; keep room collision separately. |
| `Room/Wall_*` | `стена 1`–`стена 4` | Replace visuals; retain current collision bodies. |
| `Room/Door` | `Cube.002` | Door mesh with `Dor_Material`; retain current collision. |
| `Room/Window` | `окно` | Replace visual mesh only. |
| `Desk` | `стол` | Keep dossier, notebook, phone, radio and computer child nodes. |
| `OfficeChair` | `Cube.006` and `Cube.007` | Chair uses `Stul_*`; retain `FocusCameraPose`. |
| `WallMap` | `Cube` | Board uses `Doska_*`; retain map viewport and marker scripts. |
| `DeskPhone` | `Phone_LowPoly.004` | Retain phone interaction area and `DeskPhone` script. |
| `RadioStation` | `Cylinder.014` | Retain radio interaction area and `DeskRadio` script. |
| `DeskComputer` | `Torus.015`, `Plane`, `Torus.008` | Retain display viewport, interaction and focus pose. |
| `Ficus` | `фикус` | Can replace as a purely visual object. |
| `Cabinet` | `шкаф`, `шкаф.001` | Can replace as a purely visual object. |
| `CoatRack` | `вешалка` | Can replace as a purely visual object. |
| `DeskLamp` | `Лампа` | Can replace as a purely visual object. |
| — | `Cube.002_low` (keyboard), `Cylinder.001`/`Cylinder.002` (door-handle parts), `часы`, `диванчик`, `пепельница`, `окурок`, `пепел`, `стакан`, `подстаканик`, `батарея`, `рамка`, `рамка.001`, `ручка`, `microphone_accessories_0`, `Object_8`, `polySurface313__0` | New decorative objects; no active-game counterpart yet. |

Several source mesh names (`Cube.*`, `Cylinder.*`) are generic, but the object nodes and material names above identify their purpose. The material assignment itself is already preserved by the GLB importer.

## Functional migration status

The functional migration is active in `scenes/main.tscn`:

- shared overlays, debug UI and the screen-space outline manager live under `Main/SharedSystems`;
- `Main/NewOffice/Player` starts seated at `Main/NewOffice/OfficeChair/FocusCameraPose`;
- `Main/NewOffice/DeskComputer` and `Main/NewOffice/DeskPhone` own their complete interaction and UI stacks;
- `Main/NewOffice/RadioStation` owns the radio controller, signal light, interaction area and outline;
- `Main/NewOffice/WallMap` owns the map viewport, marker controller, interaction area and outline;
- `Main/NewOffice/Notebook` owns its document viewport, interaction area and outline;
- `EmployeeDossierFolder`, `DossierDisplayPose` and `ShiftNote` were moved into `Main/NewOffice` with their existing functional scripts and viewports;
- `Main/NewOffice/EnvironmentSystems` owns the room lighting and 13 static collision shapes for the shell, desk and large furniture;
- `Main/NewOffice` occupies the final world origin; the whole `Main/OldOffice` branch has been deleted from the scene together with its orphaned meshes, materials and textures;
- oversized environment textures are imported with 2048 px limits (4096 px for the panoramic wall texture), while their source images remain untouched.

The imported GLB does not contain a dedicated employee-dossier model. The functional dossier therefore keeps its existing visual scene until a replacement asset is supplied.
