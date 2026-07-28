# Map Geometry Generator

This folder is intentionally separated from the game scene and runtime files.

## Contents

- `input/buildings.geojson`: exported building polygons from QGIS.
- `input/roads.geojson`: exported road lines from QGIS.
- `GenerateMapGeometry.ps1`: creates a Godot `.tscn` scene with `Polygon2D` and `Line2D` nodes.
- `output/GeneratedMapGeometry.tscn`: generated result. It is not connected to the game right now.

## Run

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools\map-geometry-generator\GenerateMapGeometry.ps1
```

The script uses the files in `input` by default. Optional paths can be supplied with `-BuildingsPath`, `-RoadsPath`, and `-OutputPath`.

## Reconnecting Later

To use the generated geometry in the game again, copy the generated scene into `scenes/ui/map`, instance it below `MapLayers` in `MapUI.tscn`, and point `MapBuildingEditor` to its `BuildingLayer` and `RoadLayer`.
