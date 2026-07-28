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

		_computer.EnterComputerMode(player);
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered);
	}
}
