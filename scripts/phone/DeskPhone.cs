using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Представление входящих вызовов core на физическом телефоне оператора.
/// Выбирает самый ранний ещё звонящий инцидент, но не меняет его состояние
/// до явного взаимодействия игрока с трубкой.
/// </summary>
public partial class DeskPhone : Node3D
{
	[Export] public NodePath RingLightPath { get; set; } = new("VisualRoot/RingLight");

	private readonly List<string> _ringingIncidentIds = new();
	private IDisposable _incidentCreatedSubscription = null!;
	private IDisposable _callAnsweredSubscription = null!;
	private IDisposable _callMissedSubscription = null!;
	private IDisposable _shiftEndedSubscription = null!;
	private OmniLight3D _ringLight = null!;

	public bool IsRinging => _ringingIncidentIds.Count > 0;

	public override void _Ready()
	{
		_ringLight = GetNode<OmniLight3D>(RingLightPath);
		SetRingingVisual(false);

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			GD.PushWarning("DeskPhone: GameRuntime is not ready; incoming calls are disabled.");
			return;
		}

		_incidentCreatedSubscription = runtime.Session.Events.Subscribe<IncidentCreated>(OnIncidentCreated);
		_callAnsweredSubscription = runtime.Session.Events.Subscribe<CallAnswered>(call => RemoveRingingCall(call.IncidentId));
		_callMissedSubscription = runtime.Session.Events.Subscribe<CallMissed>(call => RemoveRingingCall(call.IncidentId));
		_shiftEndedSubscription = runtime.Session.Events.Subscribe<ShiftEnded>(_ => ClearRingingCalls());

		foreach (IncidentView incident in runtime.Session.GetActiveIncidents())
		{
			if (incident.Phase == IncidentPhase.Ringing)
			{
				AddRingingCall(incident.Id);
			}
		}
	}

	public override void _ExitTree()
	{
		_incidentCreatedSubscription?.Dispose();
		_callAnsweredSubscription?.Dispose();
		_callMissedSubscription?.Dispose();
		_shiftEndedSubscription?.Dispose();
	}

	public bool TryAnswerNextCall(out string error)
	{
		error = string.Empty;
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			error = "Симуляция ещё не готова.";
			return false;
		}

		while (_ringingIncidentIds.Count > 0)
		{
			string incidentId = _ringingIncidentIds[0];
			CommandResult result = runtime.Session.AnswerCall(incidentId);
			if (result.IsSuccess)
			{
				return true;
			}

			_ringingIncidentIds.RemoveAt(0);
		}

		SetRingingVisual(false);
		error = "Нет входящих звонков.";
		return false;
	}

	private void OnIncidentCreated(IncidentCreated incident)
	{
		AddRingingCall(incident.IncidentId);
	}

	private void AddRingingCall(string incidentId)
	{
		if (_ringingIncidentIds.Contains(incidentId))
		{
			return;
		}

		_ringingIncidentIds.Add(incidentId);
		SetRingingVisual(true);
	}

	private void RemoveRingingCall(string incidentId)
	{
		_ringingIncidentIds.Remove(incidentId);
		SetRingingVisual(IsRinging);
	}

	private void ClearRingingCalls()
	{
		_ringingIncidentIds.Clear();
		SetRingingVisual(false);
	}

	private void SetRingingVisual(bool isRinging)
	{
		_ringLight.Visible = isRinging;
	}
}
