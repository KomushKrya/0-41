using Godot;

public partial class PhoneCallAcceptancePreview : Node
{
	private const string OutputPath = "res://scenes/debug/PhoneCallAcceptancePreview.png";

	public override async void _Ready()
	{
		PhoneCallAcceptanceUI phoneUi = GetNode<PhoneCallAcceptanceUI>("PhoneCallAcceptanceUI");
		phoneUi.ShowTestWindow();
		for (int frame = 0; frame < 8; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(OutputPath));
		GetTree().Quit();
	}
}
