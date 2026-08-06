using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Привязывает физическую рацию к ожидающим обращениям по радио из core.
///
/// Обращения бывают двух видов: запрос решения (у него есть варианты и таймер)
/// и готовый отчёт по миссии, которая обошлась без выбора — такой ждёт сколько
/// угодно. Снаружи они неразличимы: рация шумит одинаково, а очередь идёт по
/// времени сигнала, поэтому клик отвечает тому, кто позвал первым.
/// </summary>
public partial class DeskRadio : Node3D
{
	[Export] public NodePath SignalLightPath { get; set; } = new("VisualRoot/SignalLight");
	[Export] public NodePath RadioDecisionUiPath { get; set; } = new("../../RadioDecisionLayer/RadioDecisionUI");

	/// <summary>
	/// Одно обращение в очереди. Заполнено ровно одно из двух полей: <see cref="Decision"/>
	/// у запроса решения, <see cref="Outcome"/> у отчёта без выбора.
	/// </summary>
	private sealed class RadioRequest
	{
		public string IncidentId = string.Empty;
		public RadioTriggered Decision;
		public MissionOutcomeReady Outcome;

		public bool IsOutcome => Decision == null;
	}

	/// <summary>Очередь по времени сигнала: список, а не словарь, ради порядка.</summary>
	private readonly List<RadioRequest> _pendingRequests = new();

	/// <summary>
	/// Вызовы, по которым рация уже спрашивала решение. Их итог в очередь не попадает:
	/// его показывает открытый экран, а если игрок пропустил связь — это и есть цена
	/// пропуска. Очередь ждёт только те миссии, где выбора не было вовсе.
	/// </summary>
	private readonly HashSet<string> _askedIncidents = new(StringComparer.OrdinalIgnoreCase);
	private OmniLight3D _signalLight = null!;
	private RadioDecisionUI _decisionUi = null!;
	private GameRuntime _runtime = null!;
	private IDisposable _radioTriggeredSubscription = null!;
	private IDisposable _radioMissedSubscription = null!;
	private IDisposable _radioChosenSubscription = null!;
	private IDisposable _missionOutcomeSubscription = null!;
	private IDisposable _shiftEndedSubscription = null!;

	public bool IsActive => FindNextRequest(out _, out _);

	public override void _Ready()
	{
		_signalLight = GetNode<OmniLight3D>(SignalLightPath);
		_decisionUi = GetNodeOrNull<RadioDecisionUI>(RadioDecisionUiPath)
			?? GetTree().GetFirstNodeInGroup("radio_decision_ui") as RadioDecisionUI;
		SetActiveVisual(false);
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
		_missionOutcomeSubscription = _runtime.Session.Events.Subscribe<MissionOutcomeReady>(OnMissionOutcome);
		_shiftEndedSubscription = _runtime.Session.Events.Subscribe<ShiftEnded>(_ => ClearRequests());
		RefreshActiveVisual();
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

		if (!FindNextRequest(out RadioRequest next, out IncidentView incident))
		{
			error = "Нет ожидающих ответов по рации.";
			RefreshActiveVisual();
			return false;
		}

		if (next.IsOutcome)
		{
			return TryOpenOutcome(next, out error);
		}

		CommandResult answered = _runtime.Session.AnswerRadio(incident.Id);
		if (!answered.IsSuccess)
		{
			error = answered.Error;
			RefreshActiveVisual();
			return false;
		}

		_decisionUi.ShowRadioDecision(
			incident.Id,
			incident.MissionId,
			ContentTextResolver.ResolveEntryName(incident.MissionId, incident.MissionId),
			next.Decision.Options,
			next.Decision.MissionEventId);
		return true;
	}

	/// <summary>
	/// Показывает отчёт по миссии, прошедшей без выбора. Из очереди он уходит
	/// сразу: переспрашивать нечего, а остальные обращения обязаны шуметь дальше.
	/// </summary>
	private bool TryOpenOutcome(RadioRequest request, out string error)
	{
		error = string.Empty;

		CommandResult opened = _runtime.Session.OpenMissionOutcome(request.IncidentId);
		if (!opened.IsSuccess)
		{
			error = opened.Error;
			RefreshActiveVisual();
			return false;
		}

		_pendingRequests.Remove(request);
		RefreshActiveVisual();

		_decisionUi.ShowOutcomeReport(request.Outcome);
		return true;
	}

	private void OnRadioTriggered(RadioTriggered request)
	{
		_askedIncidents.Add(request.IncidentId);
		Enqueue(new RadioRequest { IncidentId = request.IncidentId, Decision = request });
	}

	/// <summary>
	/// Итог миссии. Экран открыт на этом же вызове — показываем прямо в нём,
	/// иначе рация зовёт игрока сама, но только если решения по ней не спрашивали.
	/// </summary>
	private void OnMissionOutcome(MissionOutcomeReady outcome)
	{
		if (_decisionUi.Visible
			&& string.Equals(_decisionUi.CurrentIncidentId, outcome.IncidentId, StringComparison.OrdinalIgnoreCase))
		{
			_runtime.Session.OpenMissionOutcome(outcome.IncidentId);
			_decisionUi.ShowOutcome(outcome);
			return;
		}

		if (_askedIncidents.Contains(outcome.IncidentId))
		{
			return;
		}

		Enqueue(new RadioRequest { IncidentId = outcome.IncidentId, Outcome = outcome });
	}

	private void Enqueue(RadioRequest request)
	{
		RemoveRequest(request.IncidentId);
		_pendingRequests.Add(request);
		RefreshActiveVisual();
	}

	private void RemoveRequest(string incidentId)
	{
		for (int index = _pendingRequests.Count - 1; index >= 0; index--)
		{
			if (string.Equals(_pendingRequests[index].IncidentId, incidentId, StringComparison.OrdinalIgnoreCase))
			{
				_pendingRequests.RemoveAt(index);
			}
		}

		RefreshActiveVisual();
	}

	private void ClearRequests()
	{
		_pendingRequests.Clear();
		_askedIncidents.Clear();
		SetActiveVisual(false);
	}

	/// <summary>
	/// Первое живое обращение в очереди. Отчёт годен всегда, запрос решения — пока
	/// вызов действительно ждёт ответа: таймер мог истечь между сигналом и кликом.
	/// </summary>
	private bool FindNextRequest(out RadioRequest request, out IncidentView incident)
	{
		request = null!;
		incident = null!;
		if (_runtime == null || !_runtime.IsReady)
		{
			return false;
		}

		foreach (RadioRequest candidate in _pendingRequests)
		{
			if (candidate.IsOutcome)
			{
				request = candidate;
				return true;
			}

			foreach (IncidentView active in _runtime.Session.GetActiveIncidents())
			{
				if (active.Phase != IncidentPhase.RadioPending
					|| !string.Equals(active.Id, candidate.IncidentId, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				request = candidate;
				incident = active;
				return true;
			}
		}

		return false;
	}

	private void RefreshActiveVisual()
	{
		SetActiveVisual(IsActive);
	}

	private void SetActiveVisual(bool isActive)
	{
		_signalLight.Visible = isActive;
	}
}
