using Godot;

public partial class DeskComputerScreenPreview : Node3D
{
	private const string OutputPath = "res://.godot/desk_computer_screen_preview.png";
	private const string ViewportOutputPath = "res://.godot/computer_viewport_preview.png";

	public override async void _Ready()
	{
		// The viewport UI and the material override are both ready after the first frames.
		for (var frame = 0; frame < 8; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		var absoluteOutputPath = ProjectSettings.GlobalizePath(OutputPath);
		var result = GetViewport().GetTexture().GetImage().SavePng(absoluteOutputPath);
		var computerViewport = GetNode<SubViewport>("DeskComputer/ComputerViewport");
		var viewportResult = computerViewport.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(ViewportOutputPath));
		if (result == Error.Ok)
		{
			GD.Print($"Desk computer preview saved to {absoluteOutputPath}");
		}
		else
		{
			GD.PushError($"Could not save desk computer preview: {result}.");
		}

		if (viewportResult != Error.Ok)
		{
			GD.PushError($"Could not save computer viewport preview: {viewportResult}.");
		}

		GetTree().Quit();
	}
}
