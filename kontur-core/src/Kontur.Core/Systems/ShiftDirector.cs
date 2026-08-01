using System;
using System.Collections.Generic;
using Kontur.Core.Api;
using Kontur.Core.Config;
using Kontur.Core.Content;
using Kontur.Core.Events;
using Kontur.Core.Model;
using Kontur.Core.Simulation;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Оркестратор смены — реализация раздела 3 ДД целиком.
	/// Держит расписание вызовов, продвигает конечные автоматы инцидентов
	/// и принимает команды игрока. Все побочные эффекты идут через шину событий.
	/// </summary>
	public sealed class ShiftDirector
	{
		private readonly GameState _state;
		private readonly ContentDatabase _content;
		private readonly IEventBus _bus;
		private readonly IRandomSource _random;
		private readonly ScalesSystem _scales;
		private readonly RosterSystem _roster;
		private readonly EncyclopediaSystem _encyclopedia;
		private readonly MissionResolver _resolver;
		private readonly IncidentScheduler _scheduler;

		private readonly List<IncidentRuntime> _incidents = new List<IncidentRuntime>();
		private readonly List<IncidentRuntime> _pending = new List<IncidentRuntime>();

		private double _shiftTime;
		private bool _callWindowClosed;
		private int _spawnedCount;
		private ShiftCounters _counters = new ShiftCounters();

		public ShiftDirector(
			GameState state,
			ContentDatabase content,
			IEventBus bus,
			IRandomSource random,
			ScalesSystem scales,
			RosterSystem roster,
			EncyclopediaSystem encyclopedia,
			MissionResolver resolver,
			IncidentScheduler scheduler)
		{
			_state = state;
			_content = content;
			_bus = bus;
			_random = random;
			_scales = scales;
			_roster = roster;
			_encyclopedia = encyclopedia;
			_resolver = resolver;
			_scheduler = scheduler;
		}

		public bool IsShiftActive { get; private set; }

		public double ShiftTime
		{
			get { return _shiftTime; }
		}

		public bool IsCallWindowClosed
		{
			get { return _callWindowClosed; }
		}

		public IReadOnlyList<IncidentRuntime> Incidents
		{
			get { return _incidents; }
		}

		public int PendingCallCount
		{
			get { return _pending.Count; }
		}

		// ------------------------------------------------------------------ смена

		public void StartShift(int day)
		{
			_state.Day = day;
			_shiftTime = 0.0;
			_callWindowClosed = false;
			_spawnedCount = 0;
			_counters = new ShiftCounters();
			_incidents.Clear();
			_pending.Clear();

			_roster.BeginShift();
			RefillEquipmentForShift(day);

			_pending.AddRange(_scheduler.BuildSchedule(day));
			_counters.TotalIncidents = _pending.Count;

			IsShiftActive = true;

			ShiftNoteDto? note;
			_content.ShiftNotes.TryGetValue(day, out note);

			_bus.Publish(new ShiftStarted(
				day,
				_roster.GetStaffLimit(day),
				note == null ? string.Empty : note.Title,
				note == null ? string.Empty : note.Text));
		}

		public void Tick(double delta)
		{
			if (!IsShiftActive || _state.IsGameOver || delta <= 0.0)
			{
				return;
			}

			_shiftTime += delta;

			SpawnDueIncidents();

			// Копия списка: обработчики событий могут закрывать инциденты.
			IncidentRuntime[] snapshot = _incidents.ToArray();
			for (int i = 0; i < snapshot.Length; i++)
			{
				TickIncident(snapshot[i], delta);

				if (_state.IsGameOver)
				{
					return;
				}
			}

			if (!_callWindowClosed && _shiftTime >= _content.Config.Timings.ShiftCallWindowSeconds)
			{
				_callWindowClosed = true;
				_bus.Publish(new CallWindowClosed(_state.Day, CountOpenIncidents()));
			}

			TryFinishShift();
		}

		private void SpawnDueIncidents()
		{
			DayConfig dayConfig = _content.Config.GetDay(_state.Day);

			// _pending отсортирован по времени, поэтому всегда берём с головы.
			while (_pending.Count > 0)
			{
				bool isSequential = _spawnedCount < dayConfig.SequentialCallCount;

				if (isSequential)
				{
					// Обучающий режим: следующий вызов не поступит, пока не закрыт предыдущий.
					if (CountOpenIncidents() > 0)
					{
						break;
					}
				}
				else if (_pending[0].ScheduledAtSeconds > _shiftTime)
				{
					break;
				}

				IncidentRuntime incident = _pending[0];
				_pending.RemoveAt(0);
				_incidents.Add(incident);
				_spawnedCount++;

				double ringSeconds = GetTimer(dayConfig, _content.Config.Timings.PhoneRingSeconds);
				incident.SetPhase(IncidentPhase.Ringing, ringSeconds > 0.0 ? ringSeconds : (double?)null);

				_bus.Publish(new IncidentCreated(
					incident.Id,
					incident.Mission.Id,
					incident.BuildingId,
					incident.Mission.CallerName,
					ringSeconds));

				// Сценарная часть закончилась — остаток расписания сдвигаем от текущего момента,
				// иначе давно просроченные времена выпустили бы все вызовы разом.
				if (isSequential && _spawnedCount == dayConfig.SequentialCallCount)
				{
					RebaseRemainingSchedule();
				}
			}
		}

		private void RebaseRemainingSchedule()
		{
			double gap = _content.Config.Timings.MinSecondsBetweenCalls;
			for (int i = 0; i < _pending.Count; i++)
			{
				_pending[i].ScheduledAtSeconds = _shiftTime + (gap * (i + 1));
			}
		}

		/// <summary>
		/// 0 означает «без ограничения»: в обучающей смене таймеры игрока отключены,
		/// и это же значение уходит в событие, чтобы интерфейс не рисовал обратный отсчёт.
		/// </summary>
		private static double GetTimer(DayConfig dayConfig, double seconds)
		{
			return dayConfig.DisableTimers ? 0.0 : seconds;
		}

		private void TryFinishShift()
		{
			if (!IsShiftActive || _pending.Count > 0)
			{
				return;
			}

			// У сценарной смены нет смысла ждать окончания пятиминутного окна:
			// список вызовов конечен и уже исчерпан.
			if (!_callWindowClosed && !_content.Config.GetDay(_state.Day).IsScripted)
			{
				return;
			}

			if (CountOpenIncidents() > 0)
			{
				return;
			}

			EndShift();
		}

		/// <summary>
		/// Жёсткая остановка смены без события ShiftEnded — для сброса партии и загрузки сейва.
		/// Именно жёсткая: EndShift означает «смена доработана», а это «смены больше нет».
		/// </summary>
		public void AbortShift()
		{
			IsShiftActive = false;
			_shiftTime = 0.0;
			_callWindowClosed = false;
			_spawnedCount = 0;
			_counters = new ShiftCounters();
			_incidents.Clear();
			_pending.Clear();
		}

		public void EndShift()
		{
			if (!IsShiftActive)
			{
				return;
			}

			IsShiftActive = false;

			DayConfig dayConfig = _content.Config.GetDay(_state.Day);
			ShiftNoteDto? note;
			_content.ShiftNotes.TryGetValue(_state.Day, out note);

			string cutscene = note != null && !string.IsNullOrEmpty(note.OutroCutsceneId)
				? note.OutroCutsceneId
				: dayConfig.OutroCutsceneId;

			_bus.Publish(new ShiftEnded(_state.Day, cutscene, _counters.ToSummary()));
		}

		private int CountOpenIncidents()
		{
			int count = 0;
			for (int i = 0; i < _incidents.Count; i++)
			{
				if (!_incidents[i].IsClosed)
				{
					count++;
				}
			}

			return count;
		}

		// ------------------------------------------------------------------ фазы инцидента

		private void TickIncident(IncidentRuntime incident, double delta)
		{
			if (incident.Timer == null || !incident.Timer.Tick(delta))
			{
				return;
			}

			switch (incident.Phase)
			{
				case IncidentPhase.Ringing:
					HandleCallMissed(incident);
					break;

				case IncidentPhase.MarkerActive:
					HandleMarkerExpired(incident);
					break;

				case IncidentPhase.Travelling:
					HandleSquadArrived(incident);
					break;

				case IncidentPhase.RadioPending:
					HandleRadioMissed(incident);
					break;

				case IncidentPhase.OnSite:
					ResolveMission(incident);
					break;

				case IncidentPhase.Returning:
					HandleSquadReturned(incident);
					break;
			}
		}

		private void HandleCallMissed(IncidentRuntime incident)
		{
			_counters.MissedCalls++;
			_bus.Publish(new CallMissed(incident.Id, incident.Mission.Id));
			ResolveAutoFailure(incident, MissionResolutionReason.CallMissed, incident.Mission.ScalesOnMissedCall, "пропущен звонок");
		}

		private void HandleMarkerExpired(IncidentRuntime incident)
		{
			_counters.ExpiredMarkers++;
			_bus.Publish(new MapMarkerExpired(incident.Id, incident.BuildingId));
			ResolveAutoFailure(incident, MissionResolutionReason.MarkerExpired, incident.Mission.ScalesOnExpiredMarker, "группа не отправлена");
		}

		private void HandleSquadArrived(IncidentRuntime incident)
		{
			_bus.Publish(new SquadArrived(incident.Id, incident.BuildingId));

			RadioEncounter? encounter = incident.Mission.RadioEncounterId == null
				? null
				: _content.FindRadioEncounter(incident.Mission.RadioEncounterId);

			if (encounter == null)
			{
				BeginMissionExecution(incident);
				return;
			}

			double radioSeconds = GetTimer(
				_content.Config.GetDay(_state.Day),
				_content.Config.Timings.RadioSeconds);

			incident.Radio = encounter;
			incident.RadioWasTriggered = true;
			incident.SetPhase(IncidentPhase.RadioPending, radioSeconds > 0.0 ? radioSeconds : (double?)null);

			var views = new List<RadioOptionView>();
			for (int i = 0; i < encounter.Options.Count; i++)
			{
				views.Add(new RadioOptionView(encounter.Options[i].Id, encounter.Options[i].Text));
			}

			_bus.Publish(new RadioTriggered(incident.Id, encounter.SituationText, views, radioSeconds));
		}

		private void HandleRadioMissed(IncidentRuntime incident)
		{
			// ДД, раздел 4: не автопровал — бросок с повышенным шансом провала.
			incident.RadioWasMissed = true;
			_bus.Publish(new RadioMissed(incident.Id));
			BeginMissionExecution(incident);
		}

		private void BeginMissionExecution(IncidentRuntime incident)
		{
			incident.SetPhase(IncidentPhase.OnSite, incident.Mission.OnSiteSeconds);
			_bus.Publish(new MissionExecutionStarted(incident.Id, incident.BuildingId, incident.Mission.OnSiteSeconds));
		}

		private void HandleSquadReturned(IncidentRuntime incident)
		{
			_roster.MarkReturned(incident.SquadEmployeeIds);
			_bus.Publish(new SquadReturned(incident.Id, incident.SquadEmployeeIds.ToArray()));

			if (incident.Report != null)
			{
				_state.Reports.Add(incident.Report);
				_bus.Publish(new MissionReportReady(incident.Report));
			}

			CloseIncident(incident, incident.Outcome != null && incident.Outcome.IsSuccess);
		}

		private void CloseIncident(IncidentRuntime incident, bool wasSuccess)
		{
			incident.SetPhase(IncidentPhase.Closed, null);
			_bus.Publish(new IncidentClosed(incident.Id, wasSuccess));
		}

		// ------------------------------------------------------------------ разрешение миссии

		private void ResolveAutoFailure(
			IncidentRuntime incident,
			MissionResolutionReason reason,
			ScaleDelta scaleDelta,
			string reasonText)
		{
			var outcome = new MissionOutcome
			{
				IncidentId = incident.Id,
				MissionId = incident.Mission.Id,
				BuildingId = incident.BuildingId,
				CreatureId = incident.Mission.CreatureId,
				Kind = MissionResultKind.Failure,
				Reason = reason,
				EffectiveRequirements = incident.Mission.Requirements,
				SquadStats = StatBlock.Zero,
				Coverage = 0.0,
				SuccessChance = 0.0,
				ScaleDelta = scaleDelta
			};

			incident.Outcome = outcome;
			_counters.Failures++;

			_bus.Publish(new MissionResolved(outcome));
			_scales.Apply(scaleDelta, reasonText);

			CloseIncident(incident, false);
		}

		private void ResolveMission(IncidentRuntime incident)
		{
			MissionDefinition mission = incident.Mission;

			List<Employee> squad = ResolveSquad(incident);
			List<EquipmentDefinition> equipment = ResolveEquipment(incident);
			CreatureDefinition? creature = _content.FindCreature(mission.CreatureId);

			StatBlock requirements = _resolver.ComputeEffectiveRequirements(
				mission,
				incident.ChosenOption,
				_state.Day);

			StatBlock squadStats = _resolver.ComputeSquadStats(squad, equipment, creature);

			var request = new ResolutionRequest
			{
				IncidentId = incident.Id,
				BuildingId = incident.BuildingId,
				Mission = mission,
				Squad = squad,
				Equipment = equipment,
				EffectiveRequirements = requirements,
				SquadStats = squadStats,
				ChosenOption = incident.ChosenOption,
				RadioWasTriggered = incident.RadioWasTriggered,
				RadioWasMissed = incident.RadioWasMissed
			};

			MissionOutcome outcome = _resolver.Resolve(request);

			ScaleDelta delta = outcome.IsSuccess ? mission.ScalesOnSuccess : mission.ScalesOnFailure;
			if (incident.ChosenOption != null)
			{
				delta = delta.Add(incident.ChosenOption.ExtraScales);
			}

			outcome.ScaleDelta = delta;
			incident.Outcome = outcome;

			if (outcome.IsSuccess)
			{
				_counters.Successes++;
			}
			else
			{
				_counters.Failures++;
			}

			_bus.Publish(new MissionResolved(outcome));

			ApplyCasualties(incident, outcome);
			ReturnOrConsumeEquipment(incident, equipment, outcome);
			_scales.Apply(delta, outcome.IsSuccess ? "успешный вызов" : "провал вызова");

			_roster.GrantExperience(
				outcome.EmployeeIds,
				outcome.IsSuccess ? mission.ExperienceOnSuccess : mission.ExperienceOnFailure);

			List<string> revealed = _encyclopedia.ProcessMissionResult(mission, outcome, incident.ChosenOption);

			if (outcome.IsSuccess)
			{
				TryFindConsumable();
			}

			incident.Report = BuildReport(incident, outcome, creature, revealed);

			if (outcome.SquadWiped)
			{
				// Возвращаться некому — отчёт всё равно фиксируется, но без опознания существа.
				if (incident.Report != null)
				{
					_state.Reports.Add(incident.Report);
					_bus.Publish(new MissionReportReady(incident.Report));
				}

				CloseIncident(incident, false);
				return;
			}

			double returnSeconds = incident.OutboundTravelSeconds > 0.0
				? incident.OutboundTravelSeconds
				: mission.ReturnSeconds;
			incident.SetPhase(IncidentPhase.Returning, returnSeconds);
			_bus.Publish(new SquadReturning(incident.Id, incident.BuildingId, returnSeconds));
		}

		private void ApplyCasualties(IncidentRuntime incident, MissionOutcome outcome)
		{
			for (int i = 0; i < outcome.KilledEmployeeIds.Count; i++)
			{
				Employee? employee = _state.FindEmployee(outcome.KilledEmployeeIds[i]);
				if (employee != null)
				{
					_roster.ApplyDeath(employee, incident.Id);
					_counters.Deaths++;
				}
			}

			for (int i = 0; i < outcome.InjuredEmployeeIds.Count; i++)
			{
				Employee? employee = _state.FindEmployee(outcome.InjuredEmployeeIds[i]);
				if (employee != null)
				{
					_roster.ApplyInjury(employee, incident.Id);
					_counters.Injuries++;
				}
			}
		}

		private MissionReport BuildReport(
			IncidentRuntime incident,
			MissionOutcome outcome,
			CreatureDefinition? creature,
			List<string> revealedProperties)
		{
			var report = new MissionReport
			{
				IncidentId = incident.Id,
				MissionId = incident.Mission.Id,
				Title = incident.Mission.Title,
				IsSuccess = outcome.IsSuccess,
				Text = outcome.IsSuccess ? incident.Mission.ReportSuccessText : incident.Mission.ReportFailureText
			};

			if (!outcome.SquadWiped && creature != null)
			{
				report.CreatureId = creature.Id;
			}

			report.RevealedPropertyIds.AddRange(revealedProperties);
			return report;
		}

		// ------------------------------------------------------------------ снаряжение

		private void RefillEquipmentForShift(int day)
		{
			DayConfig dayConfig = _content.Config.GetDay(day);

			// Найденное в прошлую смену не переносится (ДД, раздел 6).
			_state.Inventory.RemoveShiftOnlyItems();
			_state.Inventory.RemoveAllOfKind(_content.Equipment, EquipmentKind.Consumable);
			_state.Inventory.RemoveAllOfKind(_content.Equipment, EquipmentKind.Standard);

			GrantRandomOfKind(EquipmentKind.Consumable, dayConfig.ConsumablesPerShift);
			GrantRandomOfKind(EquipmentKind.Standard, dayConfig.StandardPerShift);
		}

		private void GrantRandomOfKind(EquipmentKind kind, int count)
		{
			var pool = new List<EquipmentDefinition>();
			foreach (KeyValuePair<string, EquipmentDefinition> pair in _content.Equipment)
			{
				if (pair.Value.Kind == kind)
				{
					pool.Add(pair.Value);
				}
			}

			if (pool.Count == 0 || count <= 0)
			{
				return;
			}

			pool.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

			for (int i = 0; i < count; i++)
			{
				EquipmentDefinition definition = _random.Pick(pool);
				_state.Inventory.Add(definition.Id, 1, false);
			}
		}

		private void TryFindConsumable()
		{
			if (!_random.Chance(_content.Config.Loot.ConsumableFindChance))
			{
				return;
			}

			var pool = new List<EquipmentDefinition>();
			foreach (KeyValuePair<string, EquipmentDefinition> pair in _content.Equipment)
			{
				if (pair.Value.Kind == EquipmentKind.Consumable)
				{
					pool.Add(pair.Value);
				}
			}

			if (pool.Count == 0)
			{
				return;
			}

			pool.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
			EquipmentDefinition found = _random.Pick(pool);

			_state.Inventory.Add(found.Id, 1, true);
			_bus.Publish(new EquipmentAcquired(found.Id, found.Name, true));
		}

		private void ReturnOrConsumeEquipment(
			IncidentRuntime incident,
			List<EquipmentDefinition> equipment,
			MissionOutcome outcome)
		{
			for (int i = 0; i < equipment.Count; i++)
			{
				EquipmentDefinition definition = equipment[i];

				switch (definition.Kind)
				{
					case EquipmentKind.Consumable:
						// Расходник тратится всегда при использовании на вызове.
						_bus.Publish(new EquipmentConsumed(
							definition.Id,
							definition.Name,
							_state.Inventory.GetQuantity(definition.Id)));
						break;

					case EquipmentKind.Standard:
						// Не тратится после успешно завершённого вызова.
						if (outcome.IsSuccess)
						{
							_state.Inventory.Return(definition.Id);
						}
						else
						{
							_bus.Publish(new EquipmentLost(definition.Id, definition.Name, "провал вызова"));
						}

						break;

					case EquipmentKind.Story:
						// Теряется, только если вся отправленная группа погибла.
						if (outcome.SquadWiped)
						{
							_bus.Publish(new EquipmentLost(definition.Id, definition.Name, "группа погибла"));
						}
						else
						{
							_state.Inventory.Return(definition.Id);
						}

						break;
				}
			}
		}

		private List<Employee> ResolveSquad(IncidentRuntime incident)
		{
			var squad = new List<Employee>();
			for (int i = 0; i < incident.SquadEmployeeIds.Count; i++)
			{
				Employee? employee = _state.FindEmployee(incident.SquadEmployeeIds[i]);
				if (employee != null)
				{
					squad.Add(employee);
				}
			}

			return squad;
		}

		private List<EquipmentDefinition> ResolveEquipment(IncidentRuntime incident)
		{
			var equipment = new List<EquipmentDefinition>();
			for (int i = 0; i < incident.EquipmentIds.Count; i++)
			{
				EquipmentDefinition? definition = _content.FindEquipment(incident.EquipmentIds[i]);
				if (definition != null)
				{
					equipment.Add(definition);
				}
			}

			return equipment;
		}

		// ------------------------------------------------------------------ команды игрока

		public IncidentRuntime? FindIncident(string incidentId)
		{
			for (int i = 0; i < _incidents.Count; i++)
			{
				if (string.Equals(_incidents[i].Id, incidentId, StringComparison.OrdinalIgnoreCase))
				{
					return _incidents[i];
				}
			}

			return null;
		}

		/// <summary>Требования миссии с учётом дня и уже выбранного радио-варианта — для экрана отправки.</summary>
		public StatBlock GetCurrentRequirements(IncidentRuntime incident)
		{
			return _resolver.ComputeEffectiveRequirements(incident.Mission, incident.ChosenOption, _state.Day);
		}

		/// <summary>
		/// Предпросмотр для экрана отправки. Побочных эффектов нет: кубик не бросается,
		/// снаряжение со склада не снимается.
		/// </summary>
		public DispatchEstimateView? Estimate(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return null;
			}

			var squad = new List<Employee>();
			for (int i = 0; i < employeeIds.Count; i++)
			{
				Employee? employee = _state.FindEmployee(employeeIds[i]);
				if (employee != null)
				{
					squad.Add(employee);
				}
			}

			var equipment = new List<EquipmentDefinition>();
			for (int i = 0; i < equipmentIds.Count; i++)
			{
				EquipmentDefinition? definition = _content.FindEquipment(equipmentIds[i]);
				if (definition != null)
				{
					equipment.Add(definition);
				}
			}

			StatBlock requirements = GetCurrentRequirements(incident);
			CreatureDefinition? creature = _content.FindCreature(incident.Mission.CreatureId);
			StatBlock squadStats = _resolver.ComputeSquadStats(squad, equipment, creature);

			double coverage = MissionResolver.ComputeCoverage(requirements, squadStats);
			bool isAutoSuccess = coverage >= 1.0;
			double chance = isAutoSuccess
				? 1.0
				: _resolver.ComputeSuccessChance(coverage, equipment, incident.RadioWasMissed);

			return new DispatchEstimateView(requirements, squadStats, coverage, chance, isAutoSuccess);
		}

		public CommandResult AnswerCall(string incidentId)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			if (incident.Phase != IncidentPhase.Ringing)
			{
				return CommandResult.Fail("Телефон по этому вызову не звонит.");
			}

			double markerSeconds = GetTimer(
				_content.Config.GetDay(_state.Day),
				_content.Config.Timings.MapMarkerSeconds);

			incident.SetPhase(IncidentPhase.MarkerActive, markerSeconds > 0.0 ? markerSeconds : (double?)null);
			_bus.Publish(new CallAnswered(
				incident.Id,
				incident.Mission.Id,
				incident.Mission.Title,
				incident.Mission.BriefingText));
			_bus.Publish(new MapMarkerSpawned(incident.Id, incident.BuildingId, markerSeconds));

			return CommandResult.Ok();
		}

		/// <summary>Нажатие на метку карты — компьютер открывает экран отправки.</summary>
		public CommandResult OpenDispatchScreen(string incidentId)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			if (incident.Phase != IncidentPhase.MarkerActive)
			{
				return CommandResult.Fail("Метка этого вызова неактивна.");
			}

			_bus.Publish(new DispatchScreenRequested(incident.Id, incident.Mission.Id));
			return CommandResult.Ok();
		}

		public CommandResult DispatchSquad(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds)
		{
			return DispatchSquadInternal(incidentId, employeeIds, equipmentIds, null);
		}

		public CommandResult DispatchSquad(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds,
			double travelSeconds)
		{
			if (double.IsNaN(travelSeconds) || double.IsInfinity(travelSeconds) || travelSeconds < 0.0)
			{
				return CommandResult.Fail("Время пути должно быть неотрицательным числом.");
			}

			return DispatchSquadInternal(incidentId, employeeIds, equipmentIds, travelSeconds);
		}

		private CommandResult DispatchSquadInternal(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds,
			double? travelSecondsOverride)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			if (incident.Phase != IncidentPhase.MarkerActive)
			{
				return CommandResult.Fail("Отправка возможна только пока метка активна.");
			}

			if (employeeIds == null || employeeIds.Count == 0)
			{
				return CommandResult.Fail("Не выбран ни один сотрудник.");
			}

			var squad = new List<Employee>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			for (int i = 0; i < employeeIds.Count; i++)
			{
				string employeeId = employeeIds[i];
				if (!seen.Add(employeeId))
				{
					return CommandResult.Fail($"Сотрудник '{employeeId}' указан дважды.");
				}

				Employee? employee = _state.FindEmployee(employeeId);
				if (employee == null)
				{
					return CommandResult.Fail($"Сотрудник '{employeeId}' не найден.");
				}

				if (!employee.IsAlive)
				{
					return CommandResult.Fail($"{employee.Name}: сотрудник погиб.");
				}

				if (!employee.IsAvailableForDispatch)
				{
					return CommandResult.Fail($"{employee.Name}: уже на выезде.");
				}

				squad.Add(employee);
			}

			CommandResult equipmentCheck = ValidateAndTakeEquipment(equipmentIds, out List<string> takenEquipment);
			if (!equipmentCheck.IsSuccess)
			{
				return equipmentCheck;
			}

			incident.SquadEmployeeIds.Clear();
			for (int i = 0; i < squad.Count; i++)
			{
				incident.SquadEmployeeIds.Add(squad[i].Id);
			}

			incident.EquipmentIds.Clear();
			incident.EquipmentIds.AddRange(takenEquipment);

			_roster.MarkOnMission(squad, incident.Id);
			double travelSeconds = travelSecondsOverride ?? incident.Mission.TravelSeconds;
			incident.OutboundTravelSeconds = travelSeconds;
			incident.SetPhase(IncidentPhase.Travelling, travelSeconds);

			_bus.Publish(new SquadDispatched(
				incident.Id,
				incident.SquadEmployeeIds.ToArray(),
				incident.EquipmentIds.ToArray(),
				travelSeconds));

			return CommandResult.Ok();
		}

		public CommandResult ChooseRadioOption(string incidentId, string optionId)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			if (incident.Phase != IncidentPhase.RadioPending || incident.Radio == null)
			{
				return CommandResult.Fail("Радио по этому вызову не активно.");
			}

			RadioOption? option = incident.Radio.FindOption(optionId);
			if (option == null)
			{
				return CommandResult.Fail($"Вариант '{optionId}' не найден.");
			}

			incident.ChosenOption = option;
			_bus.Publish(new RadioOptionChosen(incident.Id, option.Id, option.Text));

			BeginMissionExecution(incident);
			return CommandResult.Ok();
		}

		private CommandResult ValidateAndTakeEquipment(IReadOnlyList<string>? equipmentIds, out List<string> taken)
		{
			taken = new List<string>();

			if (equipmentIds == null || equipmentIds.Count == 0)
			{
				return CommandResult.Ok();
			}

			LootConfig loot = _content.Config.Loot;
			int heavySlots = 0;
			int consumableSlots = 0;

			var validated = new List<EquipmentDefinition>();

			for (int i = 0; i < equipmentIds.Count; i++)
			{
				EquipmentDefinition? definition = _content.FindEquipment(equipmentIds[i]);
				if (definition == null)
				{
					return CommandResult.Fail($"Снаряжение '{equipmentIds[i]}' не найдено.");
				}

				if (definition.Kind == EquipmentKind.Consumable)
				{
					consumableSlots++;
				}
				else
				{
					heavySlots++;
				}

				validated.Add(definition);
			}

			if (heavySlots > loot.StandardOrStorySlots)
			{
				return CommandResult.Fail(
					$"Слотов под обычное/сюжетное снаряжение: {loot.StandardOrStorySlots}.");
			}

			if (consumableSlots > loot.ConsumableSlots)
			{
				return CommandResult.Fail($"Слотов под расходники: {loot.ConsumableSlots}.");
			}

			for (int i = 0; i < validated.Count; i++)
			{
				if (!_state.Inventory.TryTake(validated[i].Id))
				{
					// Откатываем уже занятое, чтобы команда была атомарной.
					for (int j = 0; j < taken.Count; j++)
					{
						_state.Inventory.Return(taken[j]);
					}

					taken.Clear();
					return CommandResult.Fail($"{validated[i].Name}: нет на складе.");
				}

				taken.Add(validated[i].Id);
			}

			return CommandResult.Ok();
		}

		private sealed class ShiftCounters
		{
			public int TotalIncidents;
			public int Successes;
			public int Failures;
			public int MissedCalls;
			public int ExpiredMarkers;
			public int Injuries;
			public int Deaths;

			public ShiftSummary ToSummary()
			{
				return new ShiftSummary(TotalIncidents, Successes, Failures, MissedCalls, ExpiredMarkers, Injuries, Deaths);
			}
		}
	}
}
