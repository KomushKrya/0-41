using Godot;

public partial class FocusInteractable : Area3D, IInteractable
{
	[Export] public string Label { get; set; } = "Inspect";
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath FocusCameraPosePath { get; set; } = new("../FocusCameraPose");
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private Node3D _focusCameraPose = null!;
	private InteractionOutline _outline = null!;

	public string InteractionLabel => Label;

	public override void _Ready()
	{
		_focusCameraPose = GetNode<Node3D>(FocusCameraPosePath);
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

		player.FocusViewAt(_focusCameraPose.GlobalTransform);
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered);
	}
}
