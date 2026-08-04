using Godot;

public partial class SeatInteractable : Area3D, IInteractable
{
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private InteractionOutline _outline = null!;

	public string InteractionLabel => "Operator chair";

	public override void _Ready()
	{
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player)
	{
		return false;
	}

	public void Interact(FlyPlayer player)
	{
		// Кресло — постоянная позиция оператора, садиться и вставать нельзя.
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(false);
	}
}
