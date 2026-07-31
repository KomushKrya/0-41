using Godot;

public partial class OutlineOnlyInteractable : Area3D, IInteractable
{
	[Export] public string Label { get; set; } = "Inspect";
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private InteractionOutline _outline = null!;

	public string InteractionLabel => Label;

	public override void _Ready()
	{
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player)
	{
		return !RequiresSeated || player.IsSeated;
	}

	public void Interact(FlyPlayer player)
	{
		// The object is hoverable now; its specific interaction arrives with its gameplay system.
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered);
	}
}
