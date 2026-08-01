using Godot;

/// <summary>Физическая кнопка задания и её 3D-индикатор состояния.</summary>
public partial class MapMissionMarker : Node3D
{
	[Export] public NodePath PinPath { get; set; } = new("MapPin");
	[Export] public NodePath RingPath { get; set; } = new("MissionRing");
	[Export] public float RingRadius { get; set; } = 0.035f;
	[Export] public float RingWidth { get; set; } = 0.023f;
	[Export] public float RingOffset { get; set; } = 0.012f;
	[Export] public Color CountdownColor { get; set; } = new(0.38f, 0.38f, 0.38f, 1.0f);
	[Export] public Color TravellingColor { get; set; } = new(0.36f, 0.68f, 0.44f, 1.0f);

	private MapPin _pin = null!;
	private MeshInstance3D _ring = null!;

	public override void _Ready()
	{
		_pin = GetNode<MapPin>(PinPath);
		_ring = GetNode<MeshInstance3D>(RingPath);
		_ring.Position = new Vector3(0.0f, 0.0f, RingOffset);
		_ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		HideIndicator();
	}

	public void Initialize(string incidentId)
	{
		_pin.Initialize(incidentId);
	}

	public void ShowDispatchCountdown(double remainingSeconds, double durationSeconds)
	{
		ShowCountdown(remainingSeconds, durationSeconds);
	}

	public void ShowTravelling()
	{
		ShowProgress(1.0, 1.0, TravellingColor);
	}

	public void ShowMissionExecution(double remainingSeconds, double durationSeconds)
	{
		ShowCountdown(remainingSeconds, durationSeconds);
	}

	private void ShowCountdown(double remainingSeconds, double durationSeconds)
	{
		ShowProgress(remainingSeconds, durationSeconds, CountdownColor);
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
		var material = new StandardMaterial3D
		{
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = color,
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
}
