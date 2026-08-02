using Godot;
using System.Collections.Generic;

public partial class MapBackgroundLayout : Node
{
	[Export] public NodePath BackgroundSurfacePath { get; set; } = new("../VisualRoot/MapBackgroundSurface");
	[Export] public NodePath ViewportSurfacePath { get; set; } = new("../VisualRoot/ViewportSurface");
	[Export] public NodePath ViewportPath { get; set; } = new("../MapViewport");
	[Export] public NodePath BuildingLayerPath { get; set; } = new("../MapViewport/MapUI/MapLayers/GeneratedMapGeometry/BuildingLayer");
	[Export] public NodePath RoadLayerPath { get; set; } = new("../MapViewport/MapUI/MapLayers/GeneratedMapGeometry/RoadLayer");
	[Export] public Vector2 BackgroundOffsetPixels { get; set; } = new(-8.0f, -12.0f);
	[Export(PropertyHint.Range, "0.0001,0.01,0.0001")] public float OverlayDepthOffset { get; set; } = 0.0005f;

	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		LayoutBackground();
	}

	private void LayoutBackground()
	{
		MeshInstance3D backgroundSurface = GetNodeOrNull<MeshInstance3D>(BackgroundSurfacePath);
		MeshInstance3D viewportSurface = GetNodeOrNull<MeshInstance3D>(ViewportSurfacePath);
		SubViewport viewport = GetNodeOrNull<SubViewport>(ViewportPath);
		Control buildingLayer = GetNodeOrNull<Control>(BuildingLayerPath);
		Control roadLayer = GetNodeOrNull<Control>(RoadLayerPath);
		if (backgroundSurface == null || viewportSurface?.Mesh is not QuadMesh viewportMesh
			|| viewport == null || buildingLayer == null || roadLayer == null
			|| backgroundSurface.Mesh is not QuadMesh backgroundMesh)
		{
			GD.PushError($"{nameof(MapBackgroundLayout)}: map surfaces or geometry layers were not found.");
			return;
		}

		Control mapUi = viewport.GetNodeOrNull<Control>("MapUI");
		if (mapUi == null || !TryGetGeometryBounds(buildingLayer, roadLayer, mapUi, out Rect2 geometryBounds))
		{
			GD.PushError($"{nameof(MapBackgroundLayout)}: geometry bounds could not be calculated.");
			return;
		}

		Vector2 viewportSize = viewport.Size;
		if (viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
		{
			return;
		}

		Vector2 surfaceSize = viewportMesh.Size;
		backgroundMesh.Size = new Vector2(
			surfaceSize.X * geometryBounds.Size.X / viewportSize.X,
			surfaceSize.Y * geometryBounds.Size.Y / viewportSize.Y);

		Vector2 mapCenter = geometryBounds.GetCenter() + BackgroundOffsetPixels;
		Vector2 viewportCenter = viewportSize * 0.5f;
		Vector3 localOffset = new(
			(mapCenter.X - viewportCenter.X) * surfaceSize.X / viewportSize.X,
			-(mapCenter.Y - viewportCenter.Y) * surfaceSize.Y / viewportSize.Y,
			-OverlayDepthOffset);
		backgroundSurface.Position = viewportSurface.Position + localOffset;
	}

	private static bool TryGetGeometryBounds(
		Control buildingLayer,
		Control roadLayer,
		Control mapUi,
		out Rect2 bounds)
	{
		Vector2 minimum = new(float.MaxValue, float.MaxValue);
		Vector2 maximum = new(float.MinValue, float.MinValue);
		bool hasPoints = false;

		foreach (Polygon2D building in FindNodesOfType<Polygon2D>(buildingLayer))
		{
			ExpandBounds(building, building.Polygon, mapUi, ref minimum, ref maximum, ref hasPoints);
		}

		foreach (Line2D road in FindNodesOfType<Line2D>(roadLayer))
		{
			ExpandBounds(road, road.Points, mapUi, ref minimum, ref maximum, ref hasPoints);
		}

		bounds = hasPoints ? new Rect2(minimum, maximum - minimum) : default;
		return hasPoints && bounds.Size.X > 0.0f && bounds.Size.Y > 0.0f;
	}

	private static void ExpandBounds(
		CanvasItem source,
		IEnumerable<Vector2> points,
		CanvasItem target,
		ref Vector2 minimum,
		ref Vector2 maximum,
		ref bool hasPoints)
	{
		Transform2D sourceToTarget = target.GetGlobalTransform().AffineInverse() * source.GetGlobalTransform();
		foreach (Vector2 point in points)
		{
			Vector2 targetPoint = sourceToTarget * point;
			minimum = new Vector2(Mathf.Min(minimum.X, targetPoint.X), Mathf.Min(minimum.Y, targetPoint.Y));
			maximum = new Vector2(Mathf.Max(maximum.X, targetPoint.X), Mathf.Max(maximum.Y, targetPoint.Y));
			hasPoints = true;
		}
	}

	private static IEnumerable<T> FindNodesOfType<T>(Node root) where T : Node
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is T typedChild)
			{
				yield return typedChild;
			}

			foreach (T descendant in FindNodesOfType<T>(child))
			{
				yield return descendant;
			}
		}
	}
}
