using Godot;

public partial class SeatInteractable : Area3D, IInteractable
{
	[Export] public NodePath SeatCameraPosePath { get; set; } = new("../SeatCameraPose");
	[Export] public NodePath StandUpPointPath { get; set; } = new("../StandUpPoint");
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private Node3D _seatCameraPose = null!;
	private Node3D _standUpPoint = null!;
	private InteractionOutline _outline = null!;

	public string InteractionLabel => "Sit";

	public override void _Ready()
	{
		_seatCameraPose = GetNode<Node3D>(SeatCameraPosePath);
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

		player.SitAt(_seatCameraPose.GlobalTransform, _standUpPoint.GlobalTransform);
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered);
	}
}
