using Godot;

/// <summary>Opens the physical dossier when the player clicks it on the desk.</summary>
public partial class DossierInteractable : Area3D, IInteractable
{
	[Export] public string Label { get; set; } = "Open dossier";
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath DossierPath { get; set; } = new("..");
	[Export] public NodePath OutlinePath { get; set; } = new("../InteractionOutline");

	private DossierDispatchController _dossier = null!;
	private InteractionOutline _outline = null!;

	public string InteractionLabel => Label;

	public override void _Ready()
	{
		_dossier = GetNode<DossierDispatchController>(DossierPath);
		_outline = GetNode<InteractionOutline>(OutlinePath);
	}

	public bool CanInteract(FlyPlayer player) => !RequiresSeated || player.IsSeated;

	public void Interact(FlyPlayer player)
	{
		if (CanInteract(player))
		{
			_dossier.OpenFromDesk();
		}
	}

	public void SetHovered(bool isHovered) => _outline.SetHighlighted(isHovered);
}
