using Godot;

/// <summary>Физическая кнопка задания и её 3D-индикатор состояния.</summary>
public partial class MapMissionMarker : Node3D
{
	[Export] public NodePath PinPath { get; set; } = new("VisualRoot/MapPin");
	[Export] public NodePath RingPath { get; set; } = new("VisualRoot/MissionRing");
	[Export] public NodePath InteractionAreaPath { get; set; } = new("InteractionArea");
	[Export] public NodePath InteractionOutlinePath { get; set; } = new("InteractionOutline");
	[Export] public float RingRadius { get; set; } = 0.02625f;
	[Export] public float RingWidth { get; set; } = 0.023f;
	[Export] public float RingOffset { get; set; } = 0.012f;
	// Тёплый жёлто-оранжевый пришёл на место прежней пары «жёлтый + оранжевый»,
	// а зелёный один на оба состояния, где группа уже работает.
	[Export] public Color DispatchCountdownColor { get; set; } = new("d68b1a");
	[Export] public Color RadioCountdownColor { get; set; } = new("41536e");
	[Export] public Color MissionExecutionColor { get; set; } = new("707819");
	[Export] public Color TravellingColor { get; set; } = new("707819");

	private MapPin _pin = null!;
	private MeshInstance3D _ring = null!;
	private Area3D _interactionArea = null!;
	private InteractionOutline _interactionOutline = null!;

	public bool IsDispatchInteractive { get; private set; }

	public override void _Ready()
	{
		_pin = GetNode<MapPin>(PinPath);
		_ring = GetNode<MeshInstance3D>(RingPath);
		_interactionArea = GetNode<Area3D>(InteractionAreaPath);
		_interactionOutline = GetNode<InteractionOutline>(InteractionOutlinePath);
		_ring.Position = new Vector3(0.0f, 0.0f, RingOffset);
		_ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		_pin.SetInteractionEnabled(false);
		SetDispatchInteractive(false);
		HideIndicator();
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

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			GD.PushWarning("MapMissionMarker: GameRuntime is not ready.");
			return;
		}

		var opened = runtime.Session.OpenDispatchScreen(_pin.IncidentId);
		if (!opened.IsSuccess)
		{
			GD.PushWarning($"MapMissionMarker: {opened.Error}");
			return;
		}

		computer.EnterDispatchMode(player, _pin.IncidentId, () =>
		{
			// Отправка могла не состояться: тогда снимаем именно удержание этого
			// экрана, не затрагивая паузу телефона, рации или другого интерфейса.
			runtime.Session.CloseDispatchScreen(_pin.IncidentId);
		});
	}

	/// <summary>Состояния индикатора; цвет каждого берётся из экспортов.</summary>
	public enum RingState
	{
		Hidden,
		DispatchCountdown,
		Travelling,
		RadioCountdown,
		MissionExecution,
	}

	/// <summary>
	/// Единственная точка отрисовки кольца. Состояние выбирает цвет, доля
	/// оставшегося времени — длину дуги. <see cref="RingState.Travelling"/>
	/// показывает полное кольцо, поэтому время ему не нужно.
	/// </summary>
	public void ShowRing(RingState state, double remainingSeconds = 0.0, double durationSeconds = 0.0)
	{
		if (_ring == null || state == RingState.Hidden)
		{
			HideIndicator();
			return;
		}

		float progress = state == RingState.Travelling
			? 1.0f
			: Mathf.Clamp((float)(remainingSeconds / durationSeconds), 0.0f, 1.0f);
		if (durationSeconds <= 0.0 && state != RingState.Travelling)
		{
			HideIndicator();
			return;
		}

		_ring.Mesh = BuildRingMesh(progress, GetStateColor(state));
		_ring.Visible = progress > 0.0f;
	}

	public void HideIndicator()
	{
		if (_ring != null)
		{
			_ring.Visible = false;
		}
	}

	private Color GetStateColor(RingState state)
	{
		return state switch
		{
			RingState.DispatchCountdown => DispatchCountdownColor,
			RingState.Travelling => TravellingColor,
			RingState.RadioCountdown => RadioCountdownColor,
			RingState.MissionExecution => MissionExecutionColor,
			_ => Colors.Transparent,
		};
	}

	private ImmediateMesh BuildRingMesh(float progress, Color color)
	{
		var material = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = color,
			EmissionEnabled = true,
			Emission = color,
			EmissionEnergyMultiplier = 0.6f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			// Поверхность карты тоже прозрачная, а прозрачные материалы сортируются
			// по расстоянию до начала объекта: кольцо у края доски оказывалось
			// «дальше» центра квада и уезжало под карту. Приоритет это фиксирует.
			RenderPriority = 1,
		};
		var mesh = new ImmediateMesh();
		mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, material);

		const int segments = 48;
		float innerRadius = Mathf.Max(0.001f, RingRadius - RingWidth * 0.5f);
		float outerRadius = RingRadius + RingWidth * 0.5f;
		// Кольцо лежит в плоскости XY маркера, где +Y — верх, значит верхняя точка
		// это угол +пи/2, а убывание угла для игрока выглядит движением по часовой.
		// Оставшаяся дуга всегда заканчивается наверху, а её начало уезжает по
		// часовой стрелке: прогалина растёт от верха кольца по часовой.
		const float topAngle = Mathf.Pi * 0.5f;
		float startAngle = topAngle - Mathf.Tau * (1.0f - progress);
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
