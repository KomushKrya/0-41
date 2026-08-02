using Godot;

/// <summary>Кнопка перехода между экранами, которую можно настроить прямо в подсцене.</summary>
public partial class ComputerNavigationButton : Button
{
	[Export] public ComputerScreen TargetScreen { get; set; } = ComputerScreen.Home;

	public override void _Ready()
	{
		Pressed += OpenTargetScreen;
	}

	public override void _ExitTree()
	{
		Pressed -= OpenTargetScreen;
	}

	private void OpenTargetScreen()
	{
		Node current = GetParent();
		while (current != null)
		{
			if (current is ComputerUI computerUi)
			{
				computerUi.OpenScreen(TargetScreen);
				return;
			}

			current = current.GetParent();
		}
	}
}
