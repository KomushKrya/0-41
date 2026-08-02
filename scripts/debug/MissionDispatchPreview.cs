using Godot;

public partial class MissionDispatchPreview : Node
{
	private const string OutputPath = "res://scenes/debug/MissionDispatchPreview.png";

	public override async void _Ready()
	{
		for (int frame = 0; frame < 4; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(OutputPath));
		GetTree().Quit();
	}
}
