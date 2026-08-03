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
	[Export] public NodePath PhoneCallAcceptanceUiPath { get; set; } = new("../../PhoneCallAcceptanceLayer/PhoneCallAcceptanceUI");

	private readonly List<string> _ringingIncidentIds = new();
	private IDisposable _incidentCreatedSubscription = null!;
	private IDisposable _callAnsweredSubscription = null!;
	private IDisposable _callMissedSubscription = null!;
	private IDisposable _shiftEndedSubscription = null!;
	private OmniLight3D _ringLight = null!;
	private PhoneCallAcceptanceUI _callAcceptanceUi = null!;

	public bool IsRinging => _ringingIncidentIds.Count > 0;

	public override void _Ready()
	{
		_ringLight = GetNode<OmniLight3D>(RingLightPath);
		_callAcceptanceUi = GetNode<PhoneCallAcceptanceUI>(PhoneCallAcceptanceUiPath);
		SetRingingVisual(false);

		KonturRuntime runtime = KonturRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			GD.PushWarning("DeskPhone: KonturRuntime is not ready; incoming calls are disabled.");
			return;
		}

		_incidentCreatedSubscription = runtime.Simulation.Events.Subscribe<IncidentCreated>(OnIncidentCreated);
		_callAnsweredSubscription = runtime.Simulation.Events.Subscribe<CallAnswered>(OnCallAnswered);
		_callMissedSubscription = runtime.Simulation.Events.Subscribe<CallMissed>(call => RemoveRingingCall(call.IncidentId));
		_shiftEndedSubscription = runtime.Simulation.Events.Subscribe<ShiftEnded>(_ => ClearRingingCalls());

		foreach (IncidentView incident in runtime.Simulation.GetActiveIncidents())
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
		return TryAnswerNextCall(null, out error);
	}

	public bool TryAnswerNextCall(FlyPlayer player, out string error)
	{
		error = string.Empty;
		KonturRuntime runtime = KonturRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			error = Content.Label("ui_sim_not_ready");
			return false;
		}

		if (_callAcceptanceUi.Visible)
		{
			return true;
		}

		while (_ringingIncidentIds.Count > 0)
		{
			string incidentId = _ringingIncidentIds[0];
			IncidentView incident = FindIncident(runtime, incidentId);
			if (incident != null && incident.Phase == IncidentPhase.Ringing)
			{
				string description =
					Content.Label("ui_phone_call_from", "caller", KonturUiText.CallerName(incident)) + "\n"
					+ Content.Label("ui_phone_call_incident", "title", KonturUiText.MissionTitle(incident)) + "\n\n"
					+ Content.Label("ui_phone_call_confirm");
				_callAcceptanceUi.ShowCallAcceptance(
					Content.Label("ui_computer_incoming"),
					description,
					null,
					() => ConfirmAnswer(runtime, incidentId));
				return true;
			}

			_ringingIncidentIds.RemoveAt(0);
		}

		SetRingingVisual(false);
		error = Content.Label("ui_phone_no_calls");
		return false;
	}

	private static IncidentView FindIncident(KonturRuntime runtime, string incidentId)
	{
		foreach (IncidentView incident in runtime.Simulation.GetActiveIncidents())
		{
			if (incident.Id == incidentId)
			{
				return incident;
			}
		}

		return null!;
	}

	private void ConfirmAnswer(KonturRuntime runtime, string incidentId)
	{
		CommandResult result = runtime.Simulation.AnswerCall(incidentId);
		if (!result.IsSuccess)
		{
			GD.PushWarning($"DeskPhone: {result.Error}");
			return;
		}

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

	private void OnCallAnswered(CallAnswered call)
	{
		RemoveRingingCall(call.IncidentId);
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