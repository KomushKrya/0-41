using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Привязывает физическую рацию к ожидающим решениям по радио из core.
/// Среди нескольких запросов открывает тот, у которого осталось меньше всего времени.
/// </summary>
public partial class DeskRadio : Node3D
{
	[Export] public NodePath RadioDecisionUiPath { get; set; } = new("../../RadioDecisionLayer/RadioDecisionUI");

	private readonly Dictionary<string, RadioTriggered> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);
	private RadioDecisionUI _decisionUi = null!;
	private GameRuntime _runtime = null!;
	private IDisposable _radioTriggeredSubscription = null!;
	private IDisposable _radioMissedSubscription = null!;
	private IDisposable _radioChosenSubscription = null!;
	private IDisposable _missionOutcomeSubscription = null!;
	private IDisposable _shiftEndedSubscription = null!;

	public bool IsActive => FindMostUrgentRequest(out _, out _);

	public override void _Ready()
	{
		_decisionUi = GetNodeOrNull<RadioDecisionUI>(RadioDecisionUiPath)
			?? GetTree().GetFirstNodeInGroup("radio_decision_ui") as RadioDecisionUI;
		if (_decisionUi == null)
		{
			GD.PushError("DeskRadio: radio decision UI is not available.");
			return;
		}

		_runtime = GameRuntime.Get(this);
		if (_runtime == null || !_runtime.IsReady)
		{
			GD.PushWarning("DeskRadio: GameRuntime is not ready; radio requests are disabled.");
			return;
		}

		_radioTriggeredSubscription = _runtime.Session.Events.Subscribe<RadioTriggered>(OnRadioTriggered);
		_radioMissedSubscription = _runtime.Session.Events.Subscribe<RadioMissed>(radio => RemoveRequest(radio.IncidentId));
		_radioChosenSubscription = _runtime.Session.Events.Subscribe<RadioOptionChosen>(radio => RemoveRequest(radio.IncidentId));
		_missionOutcomeSubscription = _runtime.Session.Events.Subscribe<MissionOutcomeReady>(outcome =>
		{
			if (!_decisionUi.Visible) return;
			_runtime.Session.OpenMissionOutcome(outcome.IncidentId);
			_decisionUi.ShowOutcome(outcome);
		});
		_shiftEndedSubscription = _runtime.Session.Events.Subscribe<ShiftEnded>(_ => ClearRequests());
	}

	public override void _ExitTree()
	{
		_radioTriggeredSubscription?.Dispose();
		_radioMissedSubscription?.Dispose();
		_radioChosenSubscription?.Dispose();
		_missionOutcomeSubscription?.Dispose();
		_shiftEndedSubscription?.Dispose();
	}

	public bool TryOpenMostUrgentDecision(out string error)
	{
		error = string.Empty;
		if (_runtime == null || !_runtime.IsReady)
		{
			error = "Симуляция ещё не готова.";
			return false;
		}

		if (_decisionUi.Visible)
		{
			return true;
		}

		if (!FindMostUrgentRequest(out IncidentView incident, out RadioTriggered request))
		{
			error = "Нет ожидающих ответов по рации.";
			return false;
		}

		CommandResult answered = _runtime.Session.AnswerRadio(incident.Id);
		if (!answered.IsSuccess)
		{
			error = answered.Error;
			return false;
		}

		_decisionUi.ShowRadioDecision(
			incident.Id,
			incident.MissionId,
			ContentTextResolver.ResolveEntryName(incident.MissionId, incident.MissionId),
			request.Options,
			request.MissionEventId);
		return true;
	}

	private void OnRadioTriggered(RadioTriggered request)
	{
		_pendingRequests[request.IncidentId] = request;
	}

	private void RemoveRequest(string incidentId)
	{
		_pendingRequests.Remove(incidentId);
	}

	private void ClearRequests()
	{
		_pendingRequests.Clear();
	}

	private bool FindMostUrgentRequest(out IncidentView incident, out RadioTriggered request)
	{
		incident = null!;
		request = null!;
		if (_runtime == null || !_runtime.IsReady)
		{
			return false;
		}

		double bestRemainingSeconds = double.MaxValue;
		foreach (IncidentView candidate in _runtime.Session.GetActiveIncidents())
		{
			if (candidate.Phase != IncidentPhase.RadioPending
				|| !_pendingRequests.TryGetValue(candidate.Id, out RadioTriggered pending))
			{
				continue;
			}

			if (candidate.RemainingSeconds < bestRemainingSeconds)
			{
				bestRemainingSeconds = candidate.RemainingSeconds;
				incident = candidate;
				request = pending;
			}
		}

		return incident != null;
	}
}
