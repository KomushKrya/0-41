#nullable enable

using Godot;

// Kept under the old name because the interaction API already uses FlyPlayer.
// The controller now lives directly on the chair camera rather than on a player body.
public partial class FlyPlayer : Camera3D
{
	[Export] public float MouseSensitivity { get; set; } = 0.0025f;
	[Export] public float CameraTransitionDuration { get; set; } = 0.45f;
	[Export] public NodePath InteractionRayPath { get; set; } = new("InteractionRay");

	public bool IsSeated => true;
	public bool IsViewFocused => _isViewFocused;
	public bool IsCameraTransitioning => _transitionKind != CameraTransitionKind.None;
	public event System.Action? FocusedViewReturned;

	private RayCast3D _interactionRay = null!;
	private IInteractable? _hoveredInteractable;
	private float _pitch;
	private bool _isViewFocused;
	private Transform3D _seatedCameraTransform;
	private CameraTransitionKind _transitionKind = CameraTransitionKind.None;
	private Transform3D _transitionStartTransform;
	private Transform3D _transitionEndTransform;
	private float _transitionElapsed;
	private float _transitionDuration;

	private enum CameraTransitionKind { None, Focus, ReturnToSeat }

	public override void _Ready()
	{
		_interactionRay = GetNode<RayCast3D>(InteractionRayPath);
		_seatedCameraTransform = GlobalTransform;
		Current = true;
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
			RotateObjectLocal(Vector3.Up, -mouseMotion.Relative.X * MouseSensitivity);
			float nextPitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * MouseSensitivity,
				Mathf.DegToRad(-85.0f), Mathf.DegToRad(85.0f));
			RotateObjectLocal(Vector3.Right, nextPitch - _pitch);
			_pitch = nextPitch;
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

	public void FocusViewAt(Transform3D cameraTransform)
	{
		_isViewFocused = true;
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
		StartCameraTransition(CameraTransitionKind.ReturnToSeat, _seatedCameraTransform);
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void UpdateHoveredInteractable()
	{
		_interactionRay.ForceRaycastUpdate();
		IInteractable? nextInteractable = null;
		if (_interactionRay.GetCollider() is Node colliderNode)
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

	private void StartCameraTransition(CameraTransitionKind transitionKind, Transform3D targetTransform)
	{
		ClearHoveredInteractable();
		_transitionKind = transitionKind;
		_transitionElapsed = 0.0f;
		_transitionDuration = Mathf.Max(CameraTransitionDuration, 0.01f);
		_transitionStartTransform = GlobalTransform;
		_transitionEndTransform = targetTransform;
	}

	private void UpdateCameraTransition(float delta)
	{
		_transitionElapsed += delta;
		float progress = Mathf.Clamp(_transitionElapsed / _transitionDuration, 0.0f, 1.0f);
		float easedProgress = progress * progress * (3.0f - 2.0f * progress);
		GlobalTransform = _transitionStartTransform.InterpolateWith(_transitionEndTransform, easedProgress);
		if (progress < 1.0f)
		{
			return;
		}

		GlobalTransform = _transitionEndTransform;
		_pitch = 0.0f;
		CameraTransitionKind completedTransition = _transitionKind;
		_transitionKind = CameraTransitionKind.None;
		if (completedTransition == CameraTransitionKind.ReturnToSeat)
		{
			FocusedViewReturned?.Invoke();
		}
	}
}
