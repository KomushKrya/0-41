using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

[Tool]
public partial class MapBuildingsJsonGenerator : Node
{
	[Export] public NodePath BuildingLayerPath { get; set; } = new("../MapLayers/BuildingLayer");

	[Export(PropertyHint.File, "*.json")] public string OutputPath { get; set; } = "res://data/buildings.json";

	private bool _generateBuildingsJson;

	[Export]
	public bool GenerateBuildingsJson
	{
		get => _generateBuildingsJson;
		set
		{
			_generateBuildingsJson = value;
			if (value && Engine.IsEditorHint())
			{
				GenerateBuildingsFile();
			}
		}
	}

	private void GenerateBuildingsFile()
	{
		_generateBuildingsJson = false;
		NotifyPropertyListChanged();

		Node buildingLayer = GetNodeOrNull(BuildingLayerPath);
		if (buildingLayer == null)
		{
			GD.PushError($"MapBuildingsJsonGenerator: building layer not found at '{BuildingLayerPath}'.");
			return;
		}

		var polygons = new List<MapBuildingPolygon>();
		CollectBuildingPolygons(buildingLayer, polygons);

		var buildingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var definitions = new List<BuildingDefinitionJson>();
		foreach (MapBuildingPolygon polygon in polygons)
		{
			string buildingId = polygon.BuildingId.Trim();
			if (string.IsNullOrEmpty(buildingId))
			{
				GD.PushError($"MapBuildingsJsonGenerator: '{polygon.GetPath()}' has no BuildingId.");
				return;
			}

			if (!buildingIds.Add(buildingId))
			{
				GD.PushError($"MapBuildingsJsonGenerator: duplicate BuildingId '{buildingId}'.");
				return;
			}

			definitions.Add(new BuildingDefinitionJson(
				buildingId,
				polygon.IsDispatchTarget,
				polygon.IsHeadquarters));
		}

		definitions.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
		string json = JsonSerializer.Serialize(definitions, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		});

		using FileAccess file = FileAccess.Open(OutputPath, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PushError($"MapBuildingsJsonGenerator: could not write '{OutputPath}'.");
			return;
		}

		file.StoreString(json + "\n");
		GD.Print($"MapBuildingsJsonGenerator: wrote {definitions.Count} buildings to {OutputPath}.");
	}

	private static void CollectBuildingPolygons(Node parent, List<MapBuildingPolygon> polygons)
	{
		foreach (Node child in parent.GetChildren())
		{
			if (child is MapBuildingPolygon polygon)
			{
				polygons.Add(polygon);
			}

			CollectBuildingPolygons(child, polygons);
		}
	}

	private sealed record BuildingDefinitionJson(string Id, bool IsDispatchTarget, bool IsHeadquarters);
}
