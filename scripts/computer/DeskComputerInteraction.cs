using Godot;

public partial class DeskComputerInteraction : Node3D
{
	[Export] public NodePath FocusCameraPosePath { get; set; } = new("FocusCameraPose");
	[Export] public NodePath ViewportInputPath { get; set; } = new("ViewportInput");

	private Node3D _focusCameraPose = null!;
	private SubViewportInputController _viewportInput = null!;
	private FlyPlayer _activePlayer = null!;
	private bool _isComputerModeActive;

	public override void _Ready()
	{
		_focusCameraPose = GetNode<Node3D>(FocusCameraPosePath);
		_viewportInput = GetNode<SubViewportInputController>(ViewportInputPath);
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

		if (_viewportInput.HandleInput(@event))
		{
			GetViewport().SetInputAsHandled();
		}
	}

	public void EnterComputerMode(FlyPlayer player)
	{
		_activePlayer = player;
		_isComputerModeActive = true;

		_activePlayer.FocusViewAt(_focusCameraPose.GlobalTransform);
		_activePlayer.SetMovementEnabled(false);
		_viewportInput.BeginInteraction();
	}

	public void ExitComputerMode()
	{
		if (!_isComputerModeActive)
		{
			return;
		}

		_isComputerModeActive = false;
		_viewportInput.EndInteraction();

		if (_activePlayer != null)
		{
			_activePlayer.SetMovementEnabled(true);
			_activePlayer.ExitFocusedView();
		}
	}
}
