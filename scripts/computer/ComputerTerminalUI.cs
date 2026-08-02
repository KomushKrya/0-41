using System;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;

public partial class ComputerTerminalUI : Control
{
	[Export] public NodePath StartShiftButtonPath { get; set; } = new("StartShiftButton");
	[Export] public NodePath StatusLabelPath { get; set; } = new("StatusLines");
	[Export] public NodePath ShiftSelectorPath { get; set; } = new("ShiftSelector");

	private Button _startShiftButton = null!;
	private Label _statusLabel = null!;
	private OptionButton _shiftSelector = null!;
	private KonturRuntime _runtime = null!;
	private IDisposable? _shiftStartedSubscription;
	private IDisposable? _shiftEndedSubscription;
	private IDisposable? _gameOverSubscription;

	public override void _Ready()
	{
		_startShiftButton = GetNode<Button>(StartShiftButtonPath);
		_statusLabel = GetNode<Label>(StatusLabelPath);
		_shiftSelector = GetNode<OptionButton>(ShiftSelectorPath);
		_startShiftButton.Pressed += StartNextShift;
		_shiftSelector.ItemSelected += OnShiftSelected;

		_runtime = KonturRuntime.Get(this);
		if (_runtime == null || !_runtime.IsReady)
		{
			ShowCoreUnavailable();
			return;
		}

		_shiftStartedSubscription = _runtime.Simulation.Events.Subscribe<ShiftStarted>(_ => RefreshState());
		_shiftEndedSubscription = _runtime.Simulation.Events.Subscribe<ShiftEnded>(_ => RefreshState());
		_gameOverSubscription = _runtime.Simulation.Events.Subscribe<GameOverTriggered>(_ => RefreshState());
		PopulateShiftSelector();
		RefreshState();
	}

	public override void _ExitTree()
	{
		_startShiftButton.Pressed -= StartNextShift;
		_shiftSelector.ItemSelected -= OnShiftSelected;
		_shiftStartedSubscription?.Dispose();
		_shiftEndedSubscription?.Dispose();
		_gameOverSubscription?.Dispose();
	}

	private void StartNextShift()
	{
		if (_runtime == null || !_runtime.IsReady)
		{
			ShowCoreUnavailable();
			return;
		}

		CommandResult result = _runtime.Simulation.StartShift(GetSelectedShiftDay());
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

		KonturSimulation simulation = _runtime.Simulation;
		ShiftStatusView status = simulation.GetStatus();
		int selectedDay = GetSelectedShiftDay();

		if (status.IsGameOver)
		{
			_startShiftButton.Disabled = true;
			_shiftSelector.Disabled = true;
			_startShiftButton.Text = "SESSION CLOSED";
			_statusLabel.Text = $"TERMINAL: LOCKED\nGAME OVER: {status.GameOverReason}";
			return;
		}

		if (status.IsShiftActive)
		{
			_startShiftButton.Disabled = true;
			_shiftSelector.Disabled = true;
			_startShiftButton.Text = $"SHIFT {status.Day} ACTIVE";
			_statusLabel.Text = $"TERMINAL: ONLINE\nSHIFT {status.Day}: IN PROGRESS\nOPEN INCIDENTS: {status.OpenIncidents}";
			return;
		}

		_startShiftButton.Disabled = false;
		_shiftSelector.Disabled = false;
		_startShiftButton.Text = $"START SHIFT {selectedDay}";
		_statusLabel.Text = $"TERMINAL: READY\nSELECTED SHIFT: {selectedDay}/{simulation.Config.Days.Count}";
	}

	private void ShowCoreUnavailable()
	{
		_startShiftButton.Disabled = true;
		_shiftSelector.Disabled = true;
		_startShiftButton.Text = "CORE OFFLINE";
		_statusLabel.Text = $"TERMINAL: OFFLINE\n{_runtime?.LoadError ?? "KONTUR AUTOLOAD NOT FOUND"}";
	}

	private void PopulateShiftSelector()
	{
		_shiftSelector.Clear();
		foreach (var day in _runtime.Simulation.Config.Days)
		{
			_shiftSelector.AddItem($"SHIFT {day.Day}", day.Day);
		}

		if (_shiftSelector.ItemCount > 0)
		{
			_shiftSelector.Select(0);
		}
	}

	private int GetSelectedShiftDay()
	{
		return _shiftSelector.Selected >= 0 ? _shiftSelector.GetItemId(_shiftSelector.Selected) : 1;
	}

	private void OnShiftSelected(long _)
	{
		RefreshState();
	}
}
