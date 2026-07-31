using Godot;

[Tool]
public partial class MapBuildingPolygon : Polygon2D
{
	[Export] public string BuildingId { get; set; } = string.Empty;

	[Export] public bool IsDispatchTarget { get; set; } = true;

	[Export] public bool IsHeadquarters { get; set; }
}
