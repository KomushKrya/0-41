using Godot;

public partial class SeatInteractable : Area3D, IInteractable
{
	[Export] public NodePath FocusCameraPosePath { get; set; } = new("../FocusCameraPose");
	[Export] public NodePath StandUpPointPath { get; set; } = new("../StandUpPoint");
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private Camera3D _focusCameraPose = null!;
	private Node3D _standUpPoint = null!;
	private InteractionOutline _outline = null!;

	public string InteractionLabel => "Sit";

	public override void _Ready()
	{
		_focusCameraPose = GetNode<Camera3D>(FocusCameraPosePath);
		_standUpPoint = GetNode<Node3D>(StandUpPointPath);
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player)
	{
		return !player.IsSeated;
	}

	public void Interact(FlyPlayer player)
	{
		if (!CanInteract(player))
		{
			return;
		}

		player.SitAt(_focusCameraPose.GlobalTransform, _standUpPoint.GlobalTransform);
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered);
	}
}
