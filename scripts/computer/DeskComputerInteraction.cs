using Godot;

public partial class DeskComputerInteraction : ComputerScreenRenderer
{
	[Export] public NodePath FocusCameraPosePath { get; set; } = new("FocusCameraPose");
	[Export] public NodePath ScreenInputAreaPath { get; set; } = new("ScreenInputArea");
	[Export] public NodePath CursorPath { get; set; } = new("ComputerViewport/ComputerUI/ComputerCursor");
	[Export] public NodePath StatusLabelPath { get; set; } = new("ComputerViewport/ComputerUI/StatusLines");
	[Export] public NodePath TestButtonPath { get; set; } = new("ComputerViewport/ComputerUI/TestButton");
	[Export] public uint ScreenInputCollisionMask { get; set; } = 2;

	private MeshInstance3D _screen = null!;
	private CollisionObject3D _screenInputArea = null!;
	private SubViewport _computerViewport = null!;
	private Node3D _focusCameraPose = null!;
	private Control _cursor = null!;
	private Label _statusLabel = null!;
	private Button _testButton = null!;
	private FlyPlayer _activePlayer = null!;
	private Vector2 _lastComputerMousePosition;
	private bool _isComputerModeActive;

	public override void _Ready()
	{
		base._Ready();

		_screen = GetNode<MeshInstance3D>(ScreenPath);
		_screenInputArea = GetNode<CollisionObject3D>(ScreenInputAreaPath);
		_computerViewport = GetNode<SubViewport>(ViewportPath);
		_focusCameraPose = GetNode<Node3D>(FocusCameraPosePath);
		_cursor = GetNode<Control>(CursorPath);
		_statusLabel = GetNode<Label>(StatusLabelPath);
		_testButton = GetNode<Button>(TestButtonPath);

		_cursor.Visible = false;
		_testButton.Pressed += OnTestButtonPressed;
	}

	public override void _Input(InputEvent @event)
	{
		if (!_isComputerModeActive)
		{
			return;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			ExitComputerMode();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventMouseMotion motion)
		{
			if (TryGetComputerViewportPosition(motion.Position, out Vector2 computerPosition))
			{
				ForwardMouseMotion(motion, computerPosition);
				UpdateComputerCursor(computerPosition);
			}

			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventMouseButton button)
		{
			if (TryGetComputerViewportPosition(button.Position, out Vector2 computerPosition))
			{
				ForwardMouseButton(button, computerPosition);
				UpdateComputerCursor(computerPosition);
			}

			GetViewport().SetInputAsHandled();
		}
	}

	public void EnterComputerMode(FlyPlayer player)
	{
		_activePlayer = player;
		_isComputerModeActive = true;
		_lastComputerMousePosition = _computerViewport.Size / 2;

		_activePlayer.FocusViewAt(_focusCameraPose.GlobalTransform);
		_activePlayer.SetMovementEnabled(false);

		Input.MouseMode = Input.MouseModeEnum.ConfinedHidden;
		_cursor.Visible = true;
		UpdateComputerCursor(_lastComputerMousePosition);
	}

	public void ExitComputerMode()
	{
		if (!_isComputerModeActive)
		{
			return;
		}

		_isComputerModeActive = false;
		_cursor.Visible = false;

		if (_activePlayer != null)
		{
			_activePlayer.SetMovementEnabled(true);
			_activePlayer.ExitFocusedView();
		}

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private bool TryGetComputerViewportPosition(Vector2 screenMousePosition, out Vector2 computerPosition)
	{
		computerPosition = Vector2.Zero;

		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
		{
			return false;
		}

		Vector3 rayOrigin = camera.ProjectRayOrigin(screenMousePosition);
		Vector3 rayDirection = camera.ProjectRayNormal(screenMousePosition).Normalized();
		Vector3 rayEnd = rayOrigin + rayDirection * 100.0f;
		var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
		query.CollideWithAreas = true;
		query.CollideWithBodies = false;
		query.CollisionMask = ScreenInputCollisionMask;

		var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (hit.Count == 0)
		{
			return false;
		}

		GodotObject collider = hit["collider"].AsGodotObject();
		if (collider != _screenInputArea)
		{
			return false;
		}

		Vector3 hitPosition = hit["position"].AsVector3();
		Transform3D screenToLocal = _screen.GlobalTransform.AffineInverse();
		Vector3 localHit = screenToLocal * hitPosition;
		Aabb screenBounds = _screen.Mesh.GetAabb();

		float u = Mathf.InverseLerp(screenBounds.Position.X, screenBounds.End.X, localHit.X);
		float v = Mathf.InverseLerp(screenBounds.Position.Z, screenBounds.End.Z, localHit.Z);

		if (u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f)
		{
			return false;
		}

		Vector2 viewportSize = _computerViewport.Size;
		computerPosition = new Vector2(u * viewportSize.X, v * viewportSize.Y);
		return true;
	}

	private void ForwardMouseMotion(InputEventMouseMotion sourceEvent, Vector2 computerPosition)
	{
		var forwardedEvent = new InputEventMouseMotion
		{
			Position = computerPosition,
			GlobalPosition = computerPosition,
			Relative = computerPosition - _lastComputerMousePosition,
			Velocity = sourceEvent.Velocity,
			ButtonMask = sourceEvent.ButtonMask
		};

		_lastComputerMousePosition = computerPosition;
		_computerViewport.PushInput(forwardedEvent, true);
	}

	private void ForwardMouseButton(InputEventMouseButton sourceEvent, Vector2 computerPosition)
	{
		var forwardedEvent = new InputEventMouseButton
		{
			Position = computerPosition,
			GlobalPosition = computerPosition,
			ButtonIndex = sourceEvent.ButtonIndex,
			Pressed = sourceEvent.Pressed,
			DoubleClick = sourceEvent.DoubleClick,
			ButtonMask = sourceEvent.ButtonMask,
			Factor = sourceEvent.Factor
		};

		_lastComputerMousePosition = computerPosition;
		_computerViewport.PushInput(forwardedEvent, true);
	}

	private void UpdateComputerCursor(Vector2 computerPosition)
	{
		_cursor.Position = computerPosition - _cursor.Size * 0.5f;
	}

	private void OnTestButtonPressed()
	{
		_statusLabel.Text = "СЕАНС: ОПЕРАТОР 041\nКАНАЛ: ВНУТРЕННИЙ\nАРХИВ: ДОСТУП ОГРАНИЧЕН\nСИСТЕМА: КНОПКА НАЖАТА";
	}
}
