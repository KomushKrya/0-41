using Godot;

public partial class DeskRadioInteractable : Area3D, IInteractable
{
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath RadioPath { get; set; } = new("..");
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private DeskRadio _radio = null!;
	private InteractionOutline _outline = null!;

	public string InteractionLabel => _radio != null && _radio.IsActive ? "Answer radio" : "Radio";

	public override void _Ready()
	{
		_radio = GetNode<DeskRadio>(RadioPath);
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player)
	{
		return _radio.IsActive && (!RequiresSeated || player.IsSeated);
	}

	public void Interact(FlyPlayer player)
	{
		if (!CanInteract(player))
		{
			return;
		}

		if (!_radio.TryOpenMostUrgentDecision(out string error))
		{
			GD.PushWarning($"DeskRadio: {error}");
		}
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered && _radio.IsActive);
	}
}
