using Godot;

public partial class DossierComputerCameraPreview : Node
{
	private const string OutputPath = "res://scenes/debug/DossierComputerCameraPreview.png";

	public override async void _Ready()
	{
		DossierDispatchController dossier = GetNode<DossierDispatchController>("Main/Desk/EmployeeDossierFolder");
		Node3D dossierFocus = GetNode<Node3D>("Main/Desk/DeskComputer/FocusCameraPose/DossierFocusCameraPose");
		ComputerUI computerUi = GetNode<ComputerUI>("Main/Desk/DeskComputer/ComputerViewport/ComputerUI");
		dossier.OpenForDispatch(computerUi, 0);
		var previewCamera = new Camera3D
		{
			TopLevel = true,
			GlobalTransform = dossierFocus.GlobalTransform,
			Current = true,
		};
		AddChild(previewCamera);

		for (int frame = 0; frame < 35; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(OutputPath));
		GetTree().Quit();
	}
}
