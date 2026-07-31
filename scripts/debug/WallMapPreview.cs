using Godot;

public partial class WallMapPreview : Node3D
{
	private const string OutputPath = "res://scenes/debug/WallMapPreview.png";

	public override async void _Ready()
	{
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
}
