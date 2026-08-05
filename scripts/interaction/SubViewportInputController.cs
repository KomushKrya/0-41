using Godot;

public partial class SubViewportInputController : Node
{
	[Export] public NodePath ViewportPath { get; set; } = new("");
	[Export] public NodePath CursorPath { get; set; } = new("");
	[Export] public NodePath CursorBoundsPath { get; set; } = new("");
	[Export] public float CursorSensitivity { get; set; } = 1.0f;

	private SubViewport _viewport = null!;
	private Control _cursor;
	private Control _cursorBounds;
	private Vector2 _cursorPosition;

	public bool IsActive { get; private set; }

	public override void _Ready()
	{
		if (!string.IsNullOrWhiteSpace(ViewportPath.ToString()))
		{
			_viewport = GetNodeOrNull<SubViewport>(ViewportPath);
		}

		if (!string.IsNullOrWhiteSpace(CursorPath.ToString()))
		{
			_cursor = GetNode<Control>(CursorPath);
			_cursor.Visible = false;
		}

		if (!string.IsNullOrWhiteSpace(CursorBoundsPath.ToString()))
		{
			_cursorBounds = GetNode<Control>(CursorBoundsPath);
		}
	}

	public void Configure(SubViewport viewport, Control cursor = null, Control cursorBounds = null)
	{
		_viewport = viewport;
		_cursor = cursor;
		_cursorBounds = cursorBounds;
		if (_cursor != null)
		{
			_cursor.Visible = false;
		}
	}

	public void BeginInteraction()
	{
		if (_viewport == null)
		{
			GD.PushWarning("SubViewportInputController: target viewport is not configured.");
			return;
		}

		IsActive = true;
		_cursorPosition = GetCursorBounds().GetCenter();
		Input.MouseMode = Input.MouseModeEnum.Captured;
		UpdateCursorVisual();
	}

	public void EndInteraction()
	{
		IsActive = false;

		if (_cursor != null)
		{
			_cursor.Visible = false;
		}

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public bool HandleInput(InputEvent @event)
	{
		if (!IsActive || _viewport == null)
		{
			return false;
		}

		if (@event is InputEventMouseMotion motion)
		{
			Vector2 nextPosition = _cursorPosition + motion.Relative * CursorSensitivity;
			ForwardMouseMotion(motion, ClampToViewport(nextPosition));
			return true;
		}

		if (@event is InputEventMouseButton button)
		{
			ForwardMouseButton(button);
			return true;
		}

		return false;
	}

	private Vector2 ClampToViewport(Vector2 position)
	{
		Rect2 bounds = GetCursorBounds();
		Vector2 maximum = bounds.End - Vector2.One;
		return new Vector2(
			Mathf.Clamp(position.X, bounds.Position.X, maximum.X),
			Mathf.Clamp(position.Y, bounds.Position.Y, maximum.Y)
		);
	}

	private Rect2 GetCursorBounds()
	{
		return _cursorBounds != null
			? _cursorBounds.GetGlobalRect()
			: new Rect2(Vector2.Zero, _viewport.Size);
	}

	private void ForwardMouseMotion(InputEventMouseMotion sourceEvent, Vector2 nextPosition)
	{
		var forwardedEvent = new InputEventMouseMotion
		{
			Position = nextPosition,
			GlobalPosition = nextPosition,
			Relative = nextPosition - _cursorPosition,
			Velocity = sourceEvent.Velocity,
			ButtonMask = sourceEvent.ButtonMask
		};

		_cursorPosition = nextPosition;
		_viewport.PushInput(forwardedEvent, true);
		UpdateCursorVisual();
	}

	private void ForwardMouseButton(InputEventMouseButton sourceEvent)
	{
		var forwardedEvent = new InputEventMouseButton
		{
			Position = _cursorPosition,
			GlobalPosition = _cursorPosition,
			ButtonIndex = sourceEvent.ButtonIndex,
			Pressed = sourceEvent.Pressed,
			DoubleClick = sourceEvent.DoubleClick,
			ButtonMask = sourceEvent.ButtonMask,
			Factor = sourceEvent.Factor
		};

		_viewport.PushInput(forwardedEvent, true);
	}

	private void UpdateCursorVisual()
	{
		if (_cursor == null)
		{
			return;
		}

		_cursor.Visible = IsActive;
		_cursor.Position = _cursorPosition - _cursor.Size * 0.5f;
	}
}
