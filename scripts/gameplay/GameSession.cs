using Godot;

public partial class GameSession : Node
{
	[Export(PropertyHint.Range, "1,4,1")]
	public int TotalDays { get; set; } = 4;

	public static GameSession Instance { get; private set; } = null!;

	public int CurrentDay { get; private set; } = 1;
	public double ElapsedShiftSeconds { get; private set; }
	public ShiftState ShiftState { get; private set; } = ShiftState.NotStarted;

	public override void _Ready()
	{
		Instance = this;
		EventBus.Instance.ShiftStartRequested += StartShift;
		ResetSession();
	}

	public override void _ExitTree()
	{
		EventBus.Instance.ShiftStartRequested -= StartShift;
	}

	public override void _Process(double delta)
	{
		AdvanceShiftTime(delta);
	}

	public void ResetSession()
	{
		CurrentDay = 1;
		ElapsedShiftSeconds = 0.0;
		SetShiftState(ShiftState.NotStarted);
		EventBus.Instance.PublishDayChanged(CurrentDay);
		PublishShiftTime();
	}

	public void StartShift()
	{
		if (ShiftState != ShiftState.NotStarted)
		{
			return;
		}

		ElapsedShiftSeconds = 0.0;
		SetShiftState(ShiftState.InProgress);
		PublishShiftTime();
	}

	public void AdvanceShiftTime(double deltaSeconds)
	{
		if (ShiftState != ShiftState.InProgress || deltaSeconds <= 0.0)
		{
			return;
		}

		ElapsedShiftSeconds += deltaSeconds;
		PublishShiftTime();
	}

	public void CompleteShift()
	{
		if (ShiftState == ShiftState.InProgress)
		{
			SetShiftState(ShiftState.Completed);
		}
	}

	public void BeginDayTransition()
	{
		if (ShiftState == ShiftState.Completed)
		{
			SetShiftState(ShiftState.DayTransition);
		}
	}

	public bool StartNextDay()
	{
		if (ShiftState != ShiftState.DayTransition || CurrentDay >= TotalDays)
		{
			return false;
		}

		CurrentDay++;
		ElapsedShiftSeconds = 0.0;
		EventBus.Instance.PublishDayChanged(CurrentDay);
		SetShiftState(ShiftState.NotStarted);
		PublishShiftTime();
		return true;
	}

	private void SetShiftState(ShiftState newState)
	{
		if (ShiftState == newState)
		{
			return;
		}

		ShiftState = newState;
		EventBus.Instance.PublishShiftStateChanged(ShiftState);
	}

	private void PublishShiftTime()
	{
		EventBus.Instance.PublishShiftTimeChanged(ElapsedShiftSeconds);
	}
}
