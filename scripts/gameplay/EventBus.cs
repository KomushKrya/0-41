using System;
using System.Collections.Generic;
using Godot;

public partial class EventBus : Node
{
	private const int MaxRecentEvents = 8;
	private readonly List<string> _recentEvents = new();

	public static EventBus Instance { get; private set; } = null!;
	public IReadOnlyList<string> RecentEvents => _recentEvents;

	public event Action ShiftStartRequested = delegate { };
	public event Action<int> DayChanged = delegate { };
	public event Action<ShiftState> ShiftStateChanged = delegate { };
	public event Action<double> ShiftTimeChanged = delegate { };

	public override void _Ready()
	{
		Instance = this;
	}

	public void PublishDayChanged(int day)
	{
		RecordEvent($"DayChanged: {day}");
		DayChanged.Invoke(day);
	}

	public void PublishShiftStateChanged(ShiftState shiftState)
	{
		RecordEvent($"ShiftStateChanged: {shiftState}");
		ShiftStateChanged.Invoke(shiftState);
	}

	public void PublishShiftTimeChanged(double elapsedSeconds)
	{
		ShiftTimeChanged.Invoke(elapsedSeconds);
	}

	public void RequestShiftStart()
	{
		RecordEvent("ShiftStartRequested");
		ShiftStartRequested.Invoke();
	}

	private void RecordEvent(string eventName)
	{
		_recentEvents.Add(eventName);

		if (_recentEvents.Count > MaxRecentEvents)
		{
			_recentEvents.RemoveAt(0);
		}
	}
}
