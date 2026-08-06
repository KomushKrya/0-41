using Godot;

public partial class DeskComputerInteractable : Area3D, IInteractable
{
	[Export] public string Label { get; set; } = "Computer Screen";
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath ComputerPath { get; set; } = new("../..");
	[Export] public NodePath OutlinePath { get; set; } = new("../../InteractionOutline");

	private DeskComputerInteraction _computer = null!;
	private InteractionOutline _outline = null!;

	public string InteractionLabel => Label;

	public override void _Ready()
	{
		_computer = GetNode<DeskComputerInteraction>(ComputerPath);
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player)
	{
		return !RequiresSeated || player.IsSeated;
	}

	public void Interact(FlyPlayer player)
	{
		if (!CanInteract(player))
		{
			return;
		}

		// Время в игре останавливают только модальные экраны: отправка группы,
		// разговор по телефону и по рации. Обычный терминал — справочник:
		// пока игрок листает энциклопедию, вызов продолжает идти и может сорваться.
		_computer.EnterComputerMode(player, pauseSimulation: false);
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered);
	}
}
