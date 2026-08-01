using Godot;

public partial class RadioDecisionPreview : Node
{
	private const string OutputPath = "res://scenes/debug/RadioDecisionPreview.png";

	public override async void _Ready()
	{
		GetNode<RadioDecisionUI>("RadioDecisionUI").ShowWithTransition();
		await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

		for (int frame = 0; frame < 8; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		string absolutePath = ProjectSettings.GlobalizePath(OutputPath);
		Error result = GetViewport().GetTexture().GetImage().SavePng(absolutePath);
		if (result != Error.Ok)
		{
			GD.PushError($"Radio decision preview could not be saved: {result}.");
		}

		GetTree().Quit();
	}
}
