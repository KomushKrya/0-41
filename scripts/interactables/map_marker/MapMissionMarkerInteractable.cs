using Godot;

/// <summary>Расширенная область наведения для кнопки задания и её кольца.</summary>
public partial class MapMissionMarkerInteractable : Area3D, IInteractable
{
	[Export] public bool RequiresSeated { get; set; } = true;
	[Export] public NodePath MarkerPath { get; set; } = new("..");

	private MapMissionMarker _marker = null!;

	public string InteractionLabel => "Open computer";

	public override void _Ready()
	{
		_marker = GetNode<MapMissionMarker>(MarkerPath);
	}

	public bool CanInteract(FlyPlayer player)
	{
		return _marker.IsDispatchInteractive && (!RequiresSeated || player.IsSeated);
	}

	public void Interact(FlyPlayer player)
	{
		if (CanInteract(player))
		{
			_marker.OpenComputer(player);
		}
	}

	public void SetHovered(bool isHovered)
	{
		_marker.SetHovered(isHovered && _marker.IsDispatchInteractive);
	}
}
