using Godot;

/// <summary>
/// Кнопка перехода между экранами, которую можно настроить прямо в подсцене.
///
/// С появлением постоянной нижней панели терминалу она больше не нужна и
/// осталась только у старого главного экрана.
/// </summary>
public partial class ComputerNavigationButton : Button
{
	[Export] public ComputerScreen TargetScreen { get; set; } = ComputerScreen.Employees;

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
