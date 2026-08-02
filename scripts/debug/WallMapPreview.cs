using Godot;

public partial class WallMapPreview : Node3D
{
	private const string OutputPath = "res://scenes/debug/WallMapPreview.png";
	private const string MarkerScenePath = "res://scenes/interactables/map_marker/MapMissionMarker.tscn";

	public override async void _Ready()
	{
		ShowMarkerPreview();

		for (int frame = 0; frame < 8; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		string absolutePath = ProjectSettings.GlobalizePath(OutputPath);
		Error result = GetViewport().GetTexture().GetImage().SavePng(absolutePath);
		if (result == Error.Ok)
		{
			GD.Print($"Wall map preview saved to {absolutePath}");
		}
		else
		{
			GD.PushError($"Wall map preview could not be saved: {result}.");
		}

		GetTree().Quit();
	}

	private void ShowMarkerPreview()
	{
		PackedScene markerScene = GD.Load<PackedScene>(MarkerScenePath);
		Node3D markerContainer = GetNode<Node3D>("WallMap/VisualRoot/MapMarkers");
		MeshInstance3D mapSurface = GetNode<MeshInstance3D>("WallMap/VisualRoot/ViewportSurface");
		MapMissionMarker marker = markerScene.Instantiate<MapMissionMarker>();
		markerContainer.AddChild(marker);
		marker.Position = mapSurface.Position + new Vector3(0.0f, 0.0f, 0.003f);
		marker.Initialize("preview_marker");
		marker.ShowDispatchCountdown(20.0, 20.0);
	}
}
