using Godot;

public partial class DeskComputerInteraction : Node3D
{
	[Export] public NodePath FocusCameraPosePath { get; set; } = new("FocusCameraPose");
	[Export] public NodePath ViewportInputPath { get; set; } = new("ViewportInput");
	[Export] public NodePath ComputerUiPath { get; set; } = new("ComputerViewport/ComputerUI");

	private Node3D _focusCameraPose = null!;
	private SubViewportInputController _viewportInput = null!;
	private ComputerUI _computerUi = null!;
	private FlyPlayer _activePlayer = null!;
	private System.Action _onModeExit;
	private bool _isComputerModeActive;
	private bool _pausedRuntime;

	public override void _Ready()
	{
		_focusCameraPose = GetNode<Node3D>(FocusCameraPosePath);
		_viewportInput = GetNode<SubViewportInputController>(ViewportInputPath);
		_computerUi = GetNode<ComputerUI>(ComputerUiPath);
	}

	public override void _Input(InputEvent @event)
	{
		if (!_isComputerModeActive)
		{
			return;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			if (_computerUi.IsDispatchSelectionActive)
			{
				GetViewport().SetInputAsHandled();
				return;
			}

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
		PauseSimulationForComputer();

		_activePlayer.FocusViewAt(_focusCameraPose.GlobalTransform);
		_activePlayer.SetMovementEnabled(false);
		_viewportInput.BeginInteraction();
	}

	public void EnterDispatchMode(FlyPlayer player, string incidentId, System.Action onModeExit)
	{
		_onModeExit = onModeExit;
		EnterComputerMode(player);
		_computerUi.BeginDispatchSelection(incidentId, ExitComputerMode);
	}

	public void ExitComputerMode()
	{
		if (!_isComputerModeActive)
		{
			return;
		}

		_isComputerModeActive = false;
		_viewportInput.EndInteraction();
		_computerUi.CancelDispatchSelection();

		if (_activePlayer != null)
		{
			_activePlayer.SetMovementEnabled(true);
			if (_onModeExit != null && _activePlayer.IsViewFocused)
			{
				_activePlayer.FocusedViewReturned += FinishModeExit;
				_activePlayer.ExitFocusedView();
				return;
			}

			_activePlayer.ExitFocusedView();
		}

		FinishModeExit();
	}

	private void FinishModeExit()
	{
		if (_activePlayer != null)
		{
			_activePlayer.FocusedViewReturned -= FinishModeExit;
		}

		System.Action onModeExit = _onModeExit;
		_onModeExit = null;
		ResumeSimulationIfOwned();
		onModeExit?.Invoke();
	}

	private void PauseSimulationForComputer()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady && !runtime.IsPaused)
		{
			runtime.IsPaused = true;
			_pausedRuntime = true;
		}
	}

	private void ResumeSimulationIfOwned()
	{
		if (!_pausedRuntime)
		{
			return;
		}

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null)
		{
			runtime.IsPaused = false;
		}

		_pausedRuntime = false;
	}
}
