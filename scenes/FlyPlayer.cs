#nullable enable

using Godot;

public partial class FlyPlayer : CharacterBody3D
{
	[Export] public float MouseSensitivity { get; set; } = 0.0025f;
	[Export] public float CameraTransitionDuration { get; set; } = 0.45f;
	[Export] public NodePath HeadPath { get; set; } = new("Head");
	[Export] public NodePath InteractionRayPath { get; set; } = new("Head/Camera3D/InteractionRay");
	[Export] public NodePath InitialSeatPosePath { get; set; } = new("../OfficeChair/FocusCameraPose");

	public bool IsSeated => _isSeated;
	public bool IsViewFocused => _isViewFocused;
	public bool IsCameraTransitioning => _transitionKind != CameraTransitionKind.None;
	public event System.Action? FocusedViewReturned;

	private Node3D _head = null!;
	private RayCast3D _interactionRay = null!;
	private IInteractable? _hoveredInteractable;
	private float _pitch;
	private bool _isSeated;
	private bool _isViewFocused;
	private Transform3D _seatedCameraTransform;
	private CameraTransitionKind _transitionKind = CameraTransitionKind.None;
	private Transform3D _transitionStartTransform;
	private Transform3D _transitionEndTransform;
	private Vector3 _transitionStartHeadRotation;
	private Vector3 _transitionEndHeadRotation;
	private float _transitionElapsed;
	private float _transitionDuration;

	private enum CameraTransitionKind
	{
		None,
		Focus,
		ReturnToSeat
	}

	public override void _Ready()
	{
		_head = GetNode<Node3D>(HeadPath);
		_interactionRay = GetNode<RayCast3D>(InteractionRayPath);
		AnchorAtSeat(GetNode<Node3D>(InitialSeatPosePath).GlobalTransform);
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_transitionKind != CameraTransitionKind.None)
		{
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			if (_isViewFocused)
			{
				ReturnToSeatView();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		bool isMarkerLeftClick = @event is InputEventMouseButton mouseButton
			&& mouseButton.Pressed
			&& mouseButton.ButtonIndex == MouseButton.Left
			&& _hoveredInteractable is MapMissionMarkerInteractable;
		if ((@event.IsActionPressed("interact") || isMarkerLeftClick)
			&& _hoveredInteractable != null
			&& _hoveredInteractable.CanInteract(this))
		{
			_hoveredInteractable.Interact(this);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!_isViewFocused && @event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			RotateY(-mouseMotion.Relative.X * MouseSensitivity);

			_pitch -= mouseMotion.Relative.Y * MouseSensitivity;
			_pitch = Mathf.Clamp(_pitch, Mathf.DegToRad(-85.0f), Mathf.DegToRad(85.0f));
			_head.Rotation = new Vector3(_pitch, _head.Rotation.Y, _head.Rotation.Z);
		}
	}

	public override void _Process(double delta)
	{
		if (_transitionKind != CameraTransitionKind.None)
		{
			UpdateCameraTransition((float)delta);
			ClearHoveredInteractable();
			return;
		}

		if (_isViewFocused)
		{
			ClearHoveredInteractable();
			return;
		}

		UpdateHoveredInteractable();
	}

	public override void _PhysicsProcess(double delta)
	{
		// Оператор не покидает кресло: CharacterBody3D остаётся контейнером
		// камеры и луча взаимодействия.
		Velocity = Vector3.Zero;
	}

	private void AnchorAtSeat(Transform3D cameraTransform)
	{
		_isSeated = true;
		_isViewFocused = false;
		_seatedCameraTransform = cameraTransform;
		Velocity = Vector3.Zero;
		GlobalTransform = GetPlayerTransformForCamera(cameraTransform);
		_head.Rotation = Vector3.Zero;
		_pitch = 0.0f;
	}

	public void FocusViewAt(Transform3D cameraTransform)
	{
		if (!_isSeated)
		{
			return;
		}

		_isViewFocused = true;
		Velocity = Vector3.Zero;
		StartCameraTransition(CameraTransitionKind.Focus, cameraTransform);
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public void ExitFocusedView()
	{
		if (_isViewFocused)
		{
			ReturnToSeatView();
		}
	}

	private void ReturnToSeatView()
	{
		_isViewFocused = false;
		Velocity = Vector3.Zero;
		StartCameraTransition(CameraTransitionKind.ReturnToSeat, _seatedCameraTransform);
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void UpdateHoveredInteractable()
	{
		_interactionRay.ForceRaycastUpdate();

		IInteractable? nextInteractable = null;
		GodotObject? collider = _interactionRay.GetCollider();

		if (collider is Node colliderNode)
		{
			nextInteractable = FindInteractable(colliderNode);
			if (nextInteractable != null && !nextInteractable.CanInteract(this))
			{
				nextInteractable = null;
			}
		}

		if (nextInteractable == _hoveredInteractable)
		{
			return;
		}

		_hoveredInteractable?.SetHovered(false);
		_hoveredInteractable = nextInteractable;
		_hoveredInteractable?.SetHovered(true);
	}

	private void ClearHoveredInteractable()
	{
		_hoveredInteractable?.SetHovered(false);
		_hoveredInteractable = null;
	}

	private static IInteractable? FindInteractable(Node node)
	{
		Node? current = node;

		while (current != null)
		{
			if (current is IInteractable interactable)
			{
				return interactable;
			}

			current = current.GetParent();
		}

		return null;
	}

	private Transform3D GetPlayerTransformForCamera(Transform3D cameraTransform)
	{
		Basis cameraBasis = cameraTransform.Basis.Orthonormalized();
		Vector3 headOffset = cameraBasis * _head.Position;
		return new Transform3D(cameraBasis, cameraTransform.Origin - headOffset);
	}

	private void StartCameraTransition(CameraTransitionKind transitionKind, Transform3D cameraTransform)
	{
		StartPlayerTransition(transitionKind, GetPlayerTransformForCamera(cameraTransform));
	}

	private void StartPlayerTransition(CameraTransitionKind transitionKind, Transform3D targetTransform)
	{
		ClearHoveredInteractable();
		_transitionKind = transitionKind;
		_transitionElapsed = 0.0f;
		_transitionDuration = Mathf.Max(CameraTransitionDuration, 0.01f);
		_transitionStartTransform = GlobalTransform;
		_transitionEndTransform = targetTransform;
		_transitionStartHeadRotation = _head.Rotation;
		_transitionEndHeadRotation = Vector3.Zero;
		Velocity = Vector3.Zero;
	}

	private void UpdateCameraTransition(float delta)
	{
		_transitionElapsed += delta;
		float progress = Mathf.Clamp(_transitionElapsed / _transitionDuration, 0.0f, 1.0f);
		float easedProgress = progress * progress * (3.0f - 2.0f * progress);

		GlobalTransform = _transitionStartTransform.InterpolateWith(_transitionEndTransform, easedProgress);
		_head.Rotation = _transitionStartHeadRotation.Lerp(_transitionEndHeadRotation, easedProgress);
		_pitch = _head.Rotation.X;

		if (progress < 1.0f)
		{
			return;
		}

		GlobalTransform = _transitionEndTransform;
		_head.Rotation = _transitionEndHeadRotation;
		_pitch = 0.0f;

		CameraTransitionKind completedTransition = _transitionKind;
		_transitionKind = CameraTransitionKind.None;
		if (completedTransition == CameraTransitionKind.ReturnToSeat)
		{
			FocusedViewReturned?.Invoke();
		}
	}
}
