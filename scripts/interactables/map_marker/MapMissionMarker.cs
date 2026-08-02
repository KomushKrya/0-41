using Godot;

/// <summary>Физическая кнопка задания и её 3D-индикатор состояния.</summary>
public partial class MapMissionMarker : Node3D
{
	[Export] public NodePath PinPath { get; set; } = new("VisualRoot/MapPin");
	[Export] public NodePath RingPath { get; set; } = new("VisualRoot/MissionRing");
	[Export] public NodePath InteractionAreaPath { get; set; } = new("InteractionArea");
	[Export] public NodePath InteractionOutlinePath { get; set; } = new("InteractionOutline");
	[Export] public NodePath DebugSpherePath { get; set; } = new("DebugInteractionSphere");
	[Export] public float RingRadius { get; set; } = 0.02625f;
	[Export] public float RingWidth { get; set; } = 0.023f;
	[Export] public float RingOffset { get; set; } = 0.012f;
	[Export] public Color DispatchCountdownColor { get; set; } = new(0.58f, 0.47f, 0.18f, 1.0f);
	[Export] public Color RadioCountdownColor { get; set; } = new(0.58f, 0.31f, 0.13f, 1.0f);
	[Export] public Color MissionExecutionColor { get; set; } = new(0.23f, 0.42f, 0.27f, 1.0f);
	[Export] public Color TravellingColor { get; set; } = new(0.27f, 0.46f, 0.30f, 1.0f);

	private MapPin _pin = null!;
	private MeshInstance3D _ring = null!;
	private Area3D _interactionArea = null!;
	private InteractionOutline _interactionOutline = null!;
	private MeshInstance3D _debugSphere = null!;

	public bool IsDispatchInteractive { get; private set; }

	public override void _Ready()
	{
		_pin = GetNode<MapPin>(PinPath);
		_ring = GetNode<MeshInstance3D>(RingPath);
		_interactionArea = GetNode<Area3D>(InteractionAreaPath);
		_interactionOutline = GetNode<InteractionOutline>(InteractionOutlinePath);
		_debugSphere = GetNode<MeshInstance3D>(DebugSpherePath);
		_ring.Position = new Vector3(0.0f, 0.0f, RingOffset);
		_ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		_pin.SetInteractionEnabled(false);
		SetDispatchInteractive(false);
		DebugInterfaceOverlay.InteractionRayDebugChanged += SetInteractionDebugVisible;
		SetInteractionDebugVisible(DebugInterfaceOverlay.IsInteractionRayDebugEnabled);
		HideIndicator();
	}

	public override void _ExitTree()
	{
		DebugInterfaceOverlay.InteractionRayDebugChanged -= SetInteractionDebugVisible;
	}

	public void Initialize(string incidentId)
	{
		_pin.Initialize(incidentId);
	}

	public void SetDispatchInteractive(bool isInteractive)
	{
		IsDispatchInteractive = isInteractive;
		_interactionArea.CollisionLayer = isInteractive ? 2u : 0u;
		if (!isInteractive)
		{
			_interactionOutline.SetHighlighted(false);
		}

		SetInteractionDebugVisible(DebugInterfaceOverlay.IsInteractionRayDebugEnabled);
	}

	public void SetHovered(bool isHovered)
	{
		_interactionOutline.SetHighlighted(isHovered);
	}

	public void OpenComputer(FlyPlayer player)
	{
		DeskComputerInteraction computer = GetTree().GetFirstNodeInGroup("desk_computer") as DeskComputerInteraction;
		if (computer == null)
		{
			GD.PushWarning("MapMissionMarker: DeskComputer is not available.");
			return;
		}

		computer.EnterComputerMode(player);
	}

	public void ShowDispatchCountdown(double remainingSeconds, double durationSeconds)
	{
		ShowCountdown(remainingSeconds, durationSeconds, DispatchCountdownColor);
	}

	public void ShowTravelling()
	{
		ShowProgress(1.0, 1.0, TravellingColor);
	}

	public void ShowMissionExecution(double remainingSeconds, double durationSeconds)
	{
		ShowCountdown(remainingSeconds, durationSeconds, MissionExecutionColor);
	}

	public void ShowRadioCountdown(double remainingSeconds, double durationSeconds)
	{
		ShowCountdown(remainingSeconds, durationSeconds, RadioCountdownColor);
	}

	private void ShowCountdown(double remainingSeconds, double durationSeconds, Color color)
	{
		ShowProgress(remainingSeconds, durationSeconds, color);
	}

	public void HideIndicator()
	{
		if (_ring != null)
		{
			_ring.Visible = false;
		}
	}

	private void ShowProgress(double remainingSeconds, double durationSeconds, Color color)
	{
		if (durationSeconds <= 0.0 || _ring == null)
		{
			HideIndicator();
			return;
		}

		float progress = Mathf.Clamp((float)(remainingSeconds / durationSeconds), 0.0f, 1.0f);
		_ring.Mesh = BuildRingMesh(progress, color);
		_ring.Visible = progress > 0.0f;
	}

	private ImmediateMesh BuildRingMesh(float progress, Color color)
	{
		Color translucentColor = new(color.R, color.G, color.B, color.A * 0.5f);
		var material = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = translucentColor,
			EmissionEnabled = true,
			Emission = translucentColor * 0.35f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var mesh = new ImmediateMesh();
		mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, material);

		const int segments = 48;
		float innerRadius = Mathf.Max(0.001f, RingRadius - RingWidth * 0.5f);
		float outerRadius = RingRadius + RingWidth * 0.5f;
		float startAngle = -Mathf.Pi * 0.5f;
		for (int index = 0; index <= segments; index++)
		{
			float angle = startAngle - Mathf.Tau * progress * index / segments;
			Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
			mesh.SurfaceAddVertex(new Vector3(direction.X * outerRadius, direction.Y * outerRadius, 0.0f));
			mesh.SurfaceAddVertex(new Vector3(direction.X * innerRadius, direction.Y * innerRadius, 0.0f));
		}

		mesh.SurfaceEnd();
		return mesh;
	}

	private void SetInteractionDebugVisible(bool isRayDebugEnabled)
	{
		if (_debugSphere != null)
		{
			_debugSphere.Visible = isRayDebugEnabled && IsDispatchInteractive;
		}
	}
}
