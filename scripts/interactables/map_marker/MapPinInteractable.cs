using Godot;

public partial class MapPinInteractable : Area3D, IInteractable
{
	[Export] public string Label { get; set; } = "Open dispatch";
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	public string IncidentId { get; set; } = string.Empty;

	private InteractionOutline _outline = null!;

	public string InteractionLabel => Label;

	public override void _Ready()
	{
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player)
	{
		// Без свободных людей экран отправки не закрыть — см. MapMissionMarker.
		return MapMissionMarker.HasAvailableStaff(this) && (!RequiresSeated || player.IsSeated);
	}

	public void Interact(FlyPlayer player)
	{
		if (!CanInteract(player) || string.IsNullOrEmpty(IncidentId))
		{
			return;
		}

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return;
		}

		var result = runtime.Session.OpenDispatchScreen(IncidentId);
		if (!result.IsSuccess)
		{
			GD.PushWarning($"Map pin '{IncidentId}': {result.Error}");
		}
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered);
	}
}
