using Godot;

public partial class DebugInterfacePreview : Node
{
	private const string OutputPath = "res://scenes/debug/DebugInterfacePreview.png";

	public override async void _Ready()
	{
		await WaitFrames(4);
		SendKey(Key.F3);
		await WaitFrames(2);
		SendKey(Key.Key2);
		await WaitFrames(6);

		string absolutePath = ProjectSettings.GlobalizePath(OutputPath);
		Error result = GetViewport().GetTexture().GetImage().SavePng(absolutePath);
		if (result == Error.Ok)
		{
			GD.Print($"Debug interface preview saved to {absolutePath}");
		}
		else
		{
			GD.PushError($"Debug interface preview could not be saved: {result}.");
		}

		GetTree().Quit();
	}

	private async System.Threading.Tasks.Task WaitFrames(int count)
	{
		for (int frame = 0; frame < count; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private void SendKey(Key keycode)
	{
		GetViewport().PushInput(new InputEventKey
		{
			Keycode = keycode,
			Pressed = true
		}, true);
	}
}
