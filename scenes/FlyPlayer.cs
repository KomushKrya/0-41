#nullable enable

using Godot;

// Kept under the old name because the interaction API already uses FlyPlayer.
// The controller lives on the physical camera inside a yaw/pitch rig.
public partial class FlyPlayer : Camera3D
{
	[Export] public float MouseSensitivity { get; set; } = 0.0025f;
	[Export] public float CameraTransitionDuration { get; set; } = 0.45f;
	[Export] public NodePath InteractionRayPath { get; set; } = new("InteractionRay");
	[Export] public NodePath YawPivotPath { get; set; } = new("../..");
	[Export] public NodePath PitchPivotPath { get; set; } = new("..");

	public bool IsSeated => true;
	public bool IsViewFocused => _isViewFocused;
	public bool IsCameraTransitioning => _transitionKind != CameraTransitionKind.None;
	public event System.Action? FocusedViewReturned;

	private RayCast3D _interactionRay = null!;
	private Node3D _yawPivot = null!;
	private Node3D _pitchPivot = null!;
	private IInteractable? _hoveredInteractable;
	private float _pitch;
	private bool _isViewFocused;
	private Transform3D _seatedYawTransform;
	private CameraTransitionKind _transitionKind = CameraTransitionKind.None;
	private Transform3D _transitionStartTransform;
	private Transform3D _transitionEndTransform;
	private float _seatedFov;
	private float _transitionStartFov;
	private float _transitionEndFov;
	private float _transitionElapsed;
	private float _transitionDuration;

	private enum CameraTransitionKind { None, Focus, ReturnToSeat }

	public override void _Ready()
	{
		_interactionRay = GetNode<RayCast3D>(InteractionRayPath);
		_yawPivot = GetNode<Node3D>(YawPivotPath);
		_pitchPivot = GetNode<Node3D>(PitchPivotPath);
		_seatedYawTransform = _yawPivot.GlobalTransform;
		_seatedFov = Fov;
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
			_yawPivot.RotateY(-mouseMotion.Relative.X * MouseSensitivity);
			_pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * MouseSensitivity,
				Mathf.DegToRad(-85.0f), Mathf.DegToRad(85.0f));
			_pitchPivot.Rotation = new Vector3(_pitch, 0.0f, 0.0f);
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

	public void FocusViewAt(Node3D cameraPose)
	{
		float targetFov = cameraPose is Camera3D camera ? camera.Fov : Fov;
		FocusViewAt(cameraPose.GlobalTransform, targetFov);
	}

	public void FocusViewAt(Transform3D cameraTransform, float targetFov)
	{
		_isViewFocused = true;
		StartCameraTransition(CameraTransitionKind.Focus, cameraTransform, targetFov);
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
		StartCameraTransition(CameraTransitionKind.ReturnToSeat, GetSeatedCameraTransform(), _seatedFov);
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

	private void StartCameraTransition(CameraTransitionKind transitionKind, Transform3D targetTransform, float targetFov)
	{
		ClearHoveredInteractable();
		_transitionKind = transitionKind;
		_transitionElapsed = 0.0f;
		_transitionDuration = Mathf.Max(CameraTransitionDuration, 0.01f);
		_transitionStartTransform = GlobalTransform;
		_transitionEndTransform = targetTransform;
		_transitionStartFov = Fov;
		_transitionEndFov = targetFov;
	}

	private void UpdateCameraTransition(float delta)
	{
		_transitionElapsed += delta;
		float progress = Mathf.Clamp(_transitionElapsed / _transitionDuration, 0.0f, 1.0f);
		float easedProgress = progress * progress * (3.0f - 2.0f * progress);
		GlobalTransform = _transitionStartTransform.InterpolateWith(_transitionEndTransform, easedProgress);
		Fov = Mathf.Lerp(_transitionStartFov, _transitionEndFov, easedProgress);
		if (progress < 1.0f)
		{
			return;
		}

		GlobalTransform = _transitionEndTransform;
		Fov = _transitionEndFov;
		_pitch = 0.0f;
		CameraTransitionKind completedTransition = _transitionKind;
		_transitionKind = CameraTransitionKind.None;
		if (completedTransition == CameraTransitionKind.ReturnToSeat)
		{
			RestoreSeatedRig();
			FocusedViewReturned?.Invoke();
		}
	}

	private Transform3D GetSeatedCameraTransform()
	{
		Transform3D pitchTransform = new Transform3D(Basis.Identity, Vector3.Zero);
		return _seatedYawTransform * pitchTransform;
	}

	private void RestoreSeatedRig()
	{
		_yawPivot.GlobalTransform = _seatedYawTransform;
		_pitchPivot.Transform = Transform3D.Identity;
		Transform = Transform3D.Identity;
	}
}
