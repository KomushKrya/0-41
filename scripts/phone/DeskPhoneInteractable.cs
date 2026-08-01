using Godot;

public partial class DeskPhoneInteractable : Area3D, IInteractable
{
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath PhonePath { get; set; } = new("..");
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private DeskPhone _phone = null!;
	private InteractionOutline _outline = null!;

	public string InteractionLabel => _phone != null && _phone.IsRinging ? "Answer phone" : "Phone";

	public override void _Ready()
	{
		_phone = GetNode<DeskPhone>(PhonePath);
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player)
	{
		return _phone.IsRinging && (!RequiresSeated || player.IsSeated);
	}

	public void Interact(FlyPlayer player)
	{
		if (!CanInteract(player))
		{
			return;
		}

		if (!_phone.TryAnswerNextCall(out string error))
		{
			GD.PushWarning($"DeskPhone: {error}");
		}
	}

	public void SetHovered(bool isHovered)
	{
		_outline.SetHighlighted(isHovered && _phone.IsRinging);
	}
}
