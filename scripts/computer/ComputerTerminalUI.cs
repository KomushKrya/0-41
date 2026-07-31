using System;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;

public partial class ComputerTerminalUI : Control
{
	[Export] public NodePath StartShiftButtonPath { get; set; } = new("StartShiftButton");
	[Export] public NodePath StatusLabelPath { get; set; } = new("StatusLines");

	private Button _startShiftButton = null!;
	private Label _statusLabel = null!;
	private GameRuntime _runtime = null!;
	private IDisposable _shiftStartedSubscription;
	private IDisposable _shiftEndedSubscription;
	private IDisposable _gameOverSubscription;

	public override void _Ready()
	{
		_startShiftButton = GetNode<Button>(StartShiftButtonPath);
		_statusLabel = GetNode<Label>(StatusLabelPath);
		_startShiftButton.Pressed += StartNextShift;

		_runtime = GameRuntime.Get(this);
		if (_runtime == null || !_runtime.IsReady)
		{
			ShowCoreUnavailable();
			return;
		}

		_shiftStartedSubscription = _runtime.Session.Events.Subscribe<ShiftStarted>(_ => RefreshState());
		_shiftEndedSubscription = _runtime.Session.Events.Subscribe<ShiftEnded>(_ => RefreshState());
		_gameOverSubscription = _runtime.Session.Events.Subscribe<GameOverTriggered>(_ => RefreshState());
		RefreshState();
	}

	public override void _ExitTree()
	{
		_startShiftButton.Pressed -= StartNextShift;

		_shiftStartedSubscription?.Dispose();
		_shiftEndedSubscription?.Dispose();
		_gameOverSubscription?.Dispose();
		_shiftStartedSubscription = null;
		_shiftEndedSubscription = null;
		_gameOverSubscription = null;
	}

	private void StartNextShift()
	{
		if (_runtime == null || !_runtime.IsReady)
		{
			ShowCoreUnavailable();
			return;
		}

		GameSession session = _runtime.Session;
		ShiftStatusView status = session.GetStatus();
		int nextDay = status.Day == 0 ? 1 : status.Day + 1;
		CommandResult result = session.StartShift(nextDay);

		if (!result.IsSuccess)
		{
			_statusLabel.Text = $"TERMINAL: ERROR\n{result.Error}";
		}

		RefreshState();
	}

	private void RefreshState()
	{
		if (_runtime == null || !_runtime.IsReady)
		{
			ShowCoreUnavailable();
			return;
		}

		GameSession session = _runtime.Session;
		ShiftStatusView status = session.GetStatus();
		int totalDays = session.Config.Days.Count;
		int nextDay = status.Day == 0 ? 1 : status.Day + 1;

		if (status.IsGameOver)
		{
			_startShiftButton.Disabled = true;
			_startShiftButton.Text = "SESSION CLOSED";
			_statusLabel.Text = $"TERMINAL: LOCKED\nGAME OVER: {status.GameOverReason}";
			return;
		}

		if (status.IsShiftActive)
		{
			_startShiftButton.Disabled = true;
			_startShiftButton.Text = $"SHIFT {status.Day} ACTIVE";
			_statusLabel.Text =
				$"TERMINAL: ONLINE\n" +
				$"SHIFT {status.Day}: IN PROGRESS\n" +
				$"OPEN INCIDENTS: {status.OpenIncidents}";
			return;
		}

		if (status.Day >= totalDays)
		{
			_startShiftButton.Disabled = true;
			_startShiftButton.Text = "ALL SHIFTS COMPLETE";
			_statusLabel.Text = $"TERMINAL: ONLINE\nCAMPAIGN COMPLETED: {status.Day}/{totalDays}";
			return;
		}

		_startShiftButton.Disabled = false;
		_startShiftButton.Text = $"START SHIFT {nextDay}";
		_statusLabel.Text =
			$"TERMINAL: READY\n" +
			$"NEXT SHIFT: {nextDay}/{totalDays}";
	}

	private void ShowCoreUnavailable()
	{
		_startShiftButton.Disabled = true;
		_startShiftButton.Text = "CORE OFFLINE";
		_statusLabel.Text = $"TERMINAL: OFFLINE\n{_runtime?.LoadError ?? "KONTUR AUTOLOAD NOT FOUND"}";
	}
}
