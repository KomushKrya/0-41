using Godot;

public partial class ComputerTerminalUI : Control
{
	[Export] public NodePath StartShiftButtonPath { get; set; } = new("StartShiftButton");
	[Export] public NodePath StatusLabelPath { get; set; } = new("StatusLines");

	private Button _startShiftButton = null!;
	private Label _statusLabel = null!;

	public override void _Ready()
	{
		_startShiftButton = GetNode<Button>(StartShiftButtonPath);
		_statusLabel = GetNode<Label>(StatusLabelPath);

		_startShiftButton.Text = "START SHIFT";
		_startShiftButton.Pressed += RequestShiftStart;
		EventBus.Instance.ShiftStateChanged += UpdateTerminalState;
		UpdateTerminalState(GameSession.Instance.ShiftState);
	}

	public override void _ExitTree()
	{
		_startShiftButton.Pressed -= RequestShiftStart;
		EventBus.Instance.ShiftStateChanged -= UpdateTerminalState;
	}

	private void RequestShiftStart()
	{
		EventBus.Instance.RequestShiftStart();
	}

	private void UpdateTerminalState(ShiftState shiftState)
	{
		_startShiftButton.Disabled = shiftState == ShiftState.InProgress;
		_statusLabel.Text = shiftState switch
		{
			ShiftState.NotStarted => "TERMINAL: READY\nCOMMAND: START SHIFT",
			ShiftState.InProgress => "TERMINAL: ONLINE\nSHIFT: IN PROGRESS",
			ShiftState.Completed => "TERMINAL: ONLINE\nSHIFT: COMPLETED",
			ShiftState.DayTransition => "TERMINAL: ONLINE\nDAY TRANSITION",
			_ => "TERMINAL: UNKNOWN"
		};
	}
}
