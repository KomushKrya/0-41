using Godot;

/// <summary>Подносит плоский предмет со стола к камере, когда игрок кликает по нему.</summary>
public partial class InspectableItemInteractable : Area3D, IInteractable
{
	[Export] public string Label { get; set; } = "Inspect";
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath ItemPath { get; set; } = new("..");
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private InspectableItemController _item = null!;
	private InteractionOutline _outline = null!;

	public string InteractionLabel => Label;

	public override void _Ready()
	{
		_item = GetNode<InspectableItemController>(ItemPath);
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player) => !RequiresSeated || player.IsSeated;

	public void Interact(FlyPlayer player)
	{
		if (CanInteract(player))
		{
			_item.OpenView(player);
		}
	}

	public void SetHovered(bool isHovered) => _outline.SetHighlighted(isHovered);
}
