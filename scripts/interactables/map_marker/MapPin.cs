using Godot;

public partial class MapPin : Node3D
{
	[Export] public NodePath InteractionPath { get; set; } = new("InteractionArea");

	public string IncidentId { get; private set; } = string.Empty;

	public void Initialize(string incidentId)
	{
		IncidentId = incidentId;
		MapPinInteractable interaction = GetNodeOrNull<MapPinInteractable>(InteractionPath);
		if (interaction != null)
		{
			interaction.IncidentId = incidentId;
		}
	}

	public void SetInteractionEnabled(bool isEnabled)
	{
		Area3D interaction = GetNodeOrNull<Area3D>(InteractionPath);
		if (interaction != null)
		{
			interaction.CollisionLayer = isEnabled ? 2u : 0u;
		}
	}
}
