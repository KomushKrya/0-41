using Godot;
using Kontur.Core.Model;

/// <summary>Temporary harness: mourning ribbon on a dead employee's portrait.</summary>
public partial class RibbonPreview : Node
{
	public override async void _Ready()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		runtime.Simulation.StartShift(1);

		var roster = runtime.Simulation.DebugState.Roster;
		if (roster.Count > 1)
		{
			roster[1].Status = EmployeeStatus.Dead;
		}

		var dossier = GetNode<DossierDispatchController>("EmployeeDossierFolder");
		var camera = GetNode<Camera3D>("PreviewCamera");
		camera.Current = true;
		dossier.PresentAtCamera(camera.GlobalTransform);

		for (int frame = 0; frame < 120; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		Error result = GetViewport().GetTexture().GetImage()
			.SavePng(ProjectSettings.GlobalizePath("res://scenes/debug/ribbon.png"));
		GD.Print($"[preview] лента: {result}");
		GetTree().Quit();
	}
}
