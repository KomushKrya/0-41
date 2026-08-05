using Godot;

public partial class DeskComputerInteraction : Node3D
{
	[Export] public NodePath FocusCameraPosePath { get; set; } = new("FocusCameraPose");
	[Export] public DossierPresentationLayout PresentationLayout { get; set; } = null!;
	[Export] public NodePath ViewportInputPath { get; set; } = new("ViewportInput");
	[Export] public NodePath DossierViewportInputPath { get; set; } = new("DossierViewportInput");
	[Export] public NodePath ComputerUiPath { get; set; } = new("ComputerViewport/ComputerUI");

	private Camera3D _focusCameraPose = null!;
	private SubViewportInputController _viewportInput = null!;
	private SubViewportInputController _dossierViewportInput = null!;
	private ComputerUI _computerUi = null!;
	private DossierDispatchController _dossier = null!;
	private FlyPlayer _activePlayer = null!;
	private System.Action _onModeExit;
	private bool _isComputerModeActive;
	private bool _pausedRuntime;
	private bool _isDossierMode;

	public override void _Ready()
	{
		_focusCameraPose = GetNode<Camera3D>(FocusCameraPosePath);
		if (PresentationLayout == null)
		{
			GD.PushError("DeskComputerInteraction: dossier presentation layout is not assigned.");
		}
		_viewportInput = GetNode<SubViewportInputController>(ViewportInputPath);
		_dossierViewportInput = GetNode<SubViewportInputController>(DossierViewportInputPath);
		_computerUi = GetNode<ComputerUI>(ComputerUiPath);
		_dossier = GetTree().GetFirstNodeInGroup("employee_dossier") as DossierDispatchController;
		if (_dossier == null)
		{
			GD.PushError("DeskComputerInteraction: dispatch dossier is not available.");
		}
		else
		{
			_dossier.SelectionConfirmed += ExitDossierMode;
			SubViewport dossierViewport = _dossier.GetNodeOrNull<SubViewport>("DossierViewport");
			Control dossierCursor = _dossier.GetNodeOrNull<Control>("DossierViewport/DossierUI/DossierCursor");
			_dossierViewportInput.Configure(dossierViewport, dossierCursor);
		}

		_computerUi.DispatchSlotRequested += EnterDossierMode;
		_computerUi.ShiftStartRequested += StartShift;

		// Заставка ставится сразу, как собрался кабинет, а не при подходе к столу:
		// монитор рисуется во вьюпорт постоянно, и игрок должен видеть, что смены
		// ждут именно от него, ещё до того, как сядет за компьютер.
		ShowShiftStartIfPending();
	}

	public override void _Input(InputEvent @event)
	{
		if (!_isComputerModeActive)
		{
			return;
		}

		if (_isDossierMode)
		{
			if (@event.IsActionPressed("ui_cancel"))
			{
				ExitDossierMode();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (_dossierViewportInput.HandleInput(@event))
			{
				GetViewport().SetInputAsHandled();
			}

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

	public void EnterComputerMode(FlyPlayer player, bool pauseSimulation = true)
	{
		_activePlayer = player;
		_isComputerModeActive = true;
		if (pauseSimulation)
		{
			PauseSimulationForComputer();
		}

		_activePlayer.FocusViewAt(_focusCameraPose);
		_viewportInput.BeginInteraction();

		ShowShiftStartIfPending();
	}

	/// <summary>
	/// Смена не начата — терминал показывает заставку с одной кнопкой, а не
	/// разделы, которым до начала смены нечего показывать.
	/// </summary>
	private void ShowShiftStartIfPending()
	{
		if (GameFlow.Instance != null && GameFlow.Instance.HasPendingShift)
		{
			_computerUi.BeginShiftStartMode();
		}
	}

	/// <summary>
	/// Кнопка «Приступить к смене»: смена идёт, игрок откидывается от монитора.
	///
	/// Выход из режима сам снимает паузу, поставленную при входе, — поэтому
	/// симуляция трогается с места ровно тогда, когда камера пошла назад.
	/// </summary>
	private void StartShift()
	{
		if (GameFlow.Instance == null || !GameFlow.Instance.StartPendingShift())
		{
			return;
		}

		_computerUi.EndShiftStartMode();
		ExitComputerMode();
	}

	public void EnterDispatchMode(
		FlyPlayer player,
		string incidentId,
		System.Action onModeExit,
		string callTitle = null,
		string callTranscript = null)
	{
		_onModeExit = onModeExit;
		// Экран отправки уже удерживает время через KonturSimulation.OpenDispatchScreen.
		// Обычный компьютер продолжает пользоваться своим отдельным владельцем паузы.
		EnterComputerMode(player, false);
		_computerUi.BeginDispatchSelection(incidentId, ExitComputerMode, callTitle, callTranscript);
	}

	public void ExitComputerMode()
	{
		if (!_isComputerModeActive)
		{
			return;
		}

		_isComputerModeActive = false;
		ExitDossierMode();
		_viewportInput.EndInteraction();
		_computerUi.CancelDispatchSelection();

		if (_activePlayer != null)
		{
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

	private void EnterDossierMode(int slotIndex)
	{
		if (!_isComputerModeActive || _isDossierMode || _dossier == null || _activePlayer == null)
		{
			return;
		}

		_isDossierMode = true;
		_dossier.OpenForDispatch(_computerUi, slotIndex);
		_dossierViewportInput.BeginInteraction();
		_activePlayer.FocusViewAt(GetDossierFocusTransform(), PresentationLayout.CameraFov);
	}

	private void ExitDossierMode()
	{
		if (!_isDossierMode)
		{
			return;
		}

		_isDossierMode = false;
		_dossierViewportInput.EndInteraction();
		_dossier?.CloseDispatch();
		if (_isComputerModeActive && _activePlayer != null)
		{
			_activePlayer.FocusViewAt(_focusCameraPose);
		}
	}

	private Transform3D GetDossierFocusTransform()
	{
		return GlobalTransform * PresentationLayout.CameraTransform;
	}
}
