using System;
using System.Collections.Generic;
using Kontur.Core.Api;
using Kontur.Core.Config;
using Kontur.Core.Content;
using Kontur.Core.Events;
using Kontur.Core.Model;
using Kontur.Core.Persistence;
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
		private readonly ZoneSystem _zones;
		private readonly RosterSystem _roster;
		private readonly EncyclopediaSystem _encyclopedia;
		private readonly MissionResolver _resolver;
		private readonly IncidentScheduler _scheduler;

		private readonly List<IncidentRuntime> _incidents = new List<IncidentRuntime>();
		private readonly List<IncidentRuntime> _pending = new List<IncidentRuntime>();

		/// <summary>
		/// Кто сейчас держит мир остановленным. Множество, а не флаг: экранов может быть
		/// открыто несколько (метка по одному вызову, радио по другому), и время должно
		/// пойти только когда закрыт последний.
		/// </summary>
		private readonly HashSet<string> _timeHolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private double _shiftTime;

		/// <summary>Момент, раньше которого следующий вызов из очереди звонить не начнёт.</summary>
		private double _lineFreeAt;

		private bool _callWindowClosed;
		private int _spawnedCount;
		private ShiftCounters _counters = new ShiftCounters();

		public ShiftDirector(
			GameState state,
			ContentDatabase content,
			IEventBus bus,
			IRandomSource random,
			ScalesSystem scales,
			ZoneSystem zones,
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
			_zones = zones;
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

		/// <summary>Мир остановлен: Tick ничего не продвигает, пока игрок не закроет экран.</summary>
		public bool IsTimeFrozen
		{
			get { return _timeHolders.Count > 0; }
		}

		private void HoldTime(string holder)
		{
			bool wasFrozen = IsTimeFrozen;
			if (!_timeHolders.Add(holder) || wasFrozen)
			{
				return;
			}

			_bus.Publish(new TimeFreezeChanged(true, holder));
		}

		private void ReleaseTime(string holder)
		{
			if (!_timeHolders.Remove(holder) || IsTimeFrozen)
			{
				return;
			}

			_bus.Publish(new TimeFreezeChanged(false, holder));
		}

		/// <summary>Снять все удержания разом — при обрыве смены и game over.</summary>
		private void ReleaseAllTime()
		{
			if (_timeHolders.Count == 0)
			{
				return;
			}

			_timeHolders.Clear();
			_bus.Publish(new TimeFreezeChanged(false, "смена завершена"));
		}

		private static string DispatchHolder(string incidentId)
		{
			return "dispatch:" + incidentId;
		}

		private static string RadioHolder(string incidentId)
		{
			return "radio:" + incidentId;
		}

		private static string OutcomeHolder(string incidentId)
		{
			return "outcome:" + incidentId;
		}

		/// <summary>Держатель времени на время загрузки — пока интерфейс не перерисовался.</summary>
		private const string LoadHolder = "load";

		public void HoldAfterLoad()
		{
			HoldTime(LoadHolder);
		}

		public void ReleaseAfterLoad()
		{
			ReleaseTime(LoadHolder);
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
			_lineFreeAt = 0.0;
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

			DayConfig startedDay = _content.Config.GetDay(day);

			_bus.Publish(new ShiftStarted(day, _roster.GetStaffLimit(day), startedDay.ShiftNoteId));
		}

		public void Tick(double delta)
		{
			if (!IsShiftActive || _state.IsGameOver || delta <= 0.0)
			{
				return;
			}

			// Мир остановлен: не двигаются ни таймеры вызовов, ни дорога группы,
			// ни часы смены — то есть новые звонки тоже не поступают.
			if (IsTimeFrozen)
			{
				return;
			}

			_shiftTime += delta;

			SpawnDueIncidents();
			PromoteQueuedCall();

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

				// В очередь, а не сразу в звонок: телефон один, и два разговора
				// одновременно превратились бы в кашу из наложенных реплик.
				//
				// Ждать вызову придётся не только когда линия занята. Если несколько
				// вызовов назначены на один момент, они попадают сюда все разом, и никто
				// из них ещё не звонит — но зазвонит только первый. Смотреть на одну лишь
				// IsLineBusy() здесь было ошибкой: событие об ожидании не публиковалось
				// вообще никогда, а интерфейсу нечем было зажечь индикатор очереди.
				bool lineBusy = IsLineBusy();
				bool gapPending = _shiftTime < _lineFreeAt;

				incident.SetPhase(IncidentPhase.Queued, null);

				int queued = CountQueued();
				if (lineBusy || gapPending || queued > 1)
				{
					_bus.Publish(new IncidentQueued(incident.Id, incident.Mission.CallId, queued));
				}

				// Сценарная часть закончилась — остаток расписания сдвигаем от текущего момента,
				// иначе давно просроченные времена выпустили бы все вызовы разом.
				if (isSequential && _spawnedCount == dayConfig.SequentialCallCount)
				{
					RebaseRemainingSchedule();
				}
			}
		}

		/// <summary>
		/// Линия занята: телефон звонит либо игрок читает бланк по уже снятой трубке.
		/// Оба состояния — один и тот же разговор с точки зрения игрока.
		/// </summary>
		private bool IsLineBusy()
		{
			for (int i = 0; i < _incidents.Count; i++)
			{
				IncidentPhase phase = _incidents[i].Phase;
				if (phase == IncidentPhase.Ringing || phase == IncidentPhase.Briefing)
				{
					return true;
				}
			}

			return false;
		}

		public int CountQueued()
		{
			int count = 0;
			for (int i = 0; i < _incidents.Count; i++)
			{
				if (_incidents[i].Phase == IncidentPhase.Queued)
				{
					count++;
				}
			}

			return count;
		}

		/// <summary>
		/// Пускает следующий вызов из очереди, когда линия освободилась и прошла пауза.
		///
		/// Очередь строго по времени поступления: пришедший раньше зазвонит раньше.
		/// Отсчёт 15 секунд начинается здесь, а не в момент попадания в очередь — иначе
		/// игрок терял бы вызовы, которых ещё не слышал.
		/// </summary>
		private void PromoteQueuedCall()
		{
			if (IsLineBusy() || _shiftTime < _lineFreeAt)
			{
				return;
			}

			IncidentRuntime? next = null;
			for (int i = 0; i < _incidents.Count; i++)
			{
				IncidentRuntime candidate = _incidents[i];
				if (candidate.Phase != IncidentPhase.Queued)
				{
					continue;
				}

				if (next == null || candidate.ScheduledAtSeconds < next.ScheduledAtSeconds)
				{
					next = candidate;
				}
			}

			if (next == null)
			{
				return;
			}

			DayConfig dayConfig = _content.Config.GetDay(_state.Day);
			double ringSeconds = GetTimer(dayConfig, _content.Config.Timings.PhoneRingSeconds);
			next.SetPhase(IncidentPhase.Ringing, ringSeconds > 0.0 ? ringSeconds : (double?)null);

			_bus.Publish(new IncidentCreated(
				next.Id,
				next.Mission.Id,
				next.Mission.ZoneId,
				next.BuildingId,
				next.Mission.CallId,
				ringSeconds));
		}

		/// <summary>Линия освободилась — следующий звонок не раньше, чем через паузу.</summary>
		private void ReleaseLine()
		{
			_lineFreeAt = _shiftTime + _content.Config.Timings.CallQueueGapSeconds;
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
			ReleaseAllTime();
			IsShiftActive = false;
			_shiftTime = 0.0;
			_lineFreeAt = 0.0;
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

			ReleaseAllTime();
			IsShiftActive = false;

			DayConfig dayConfig = _content.Config.GetDay(_state.Day);
			string cutscene = dayConfig.OutroCutsceneId;

			_bus.Publish(new ShiftEnded(_state.Day, cutscene, _counters.ToSummary()));
			OpenHiring();
		}

		/// <summary>
		/// Между сменами: кого можно взять на завтра.
		///
		/// Молчим, если брать некого — партия проиграна или мест в штате нет.
		/// Открытое меню найма с пустым списком игрок читает как поломку, а не как «мест нет».
		/// </summary>
		private void OpenHiring()
		{
			if (_state.IsGameOver)
			{
				return;
			}

			int nextDay = _state.Day + 1;
			int freeSlots = _roster.CountFreeSlots(nextDay);
			if (freeSlots <= 0)
			{
				return;
			}

			IReadOnlyList<HireCandidate> candidates = _roster.GetAvailableCandidates(nextDay);
			if (candidates.Count == 0)
			{
				return;
			}

			var ids = new List<string>(candidates.Count);
			for (int i = 0; i < candidates.Count; i++)
			{
				ids.Add(candidates[i].Template.Id);
			}

			_bus.Publish(new HiringOpened(
				nextDay,
				_roster.GetStaffLimit(nextDay),
				_roster.CountLiving(),
				freeSlots,
				ids));
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
			// Трубку не сняли — линия свободна, следующий из очереди пойдёт после паузы.
			ReleaseLine();
			_counters.MissedCalls++;
			_bus.Publish(new CallMissed(incident.Id, incident.Mission.Id));
			ResolveAutoFailure(incident, MissionResolutionReason.CallMissed, incident.Mission.ScalesOnMissedCall, "пропущен звонок");
		}

		private void HandleMarkerExpired(IncidentRuntime incident)
		{
			_counters.ExpiredMarkers++;
			_bus.Publish(new MapMarkerExpired(incident.Id, incident.Mission.ZoneId, incident.BuildingId));
			ResolveAutoFailure(incident, MissionResolutionReason.MarkerExpired, incident.Mission.ScalesOnExpiredMarker, "группа не отправлена");
		}

		private void HandleSquadArrived(IncidentRuntime incident)
		{
			_bus.Publish(new SquadArrived(incident.Id, incident.Mission.ZoneId, incident.BuildingId));

			MissionEventDefinition? missionEvent = incident.Mission.HasMissionEvent
				? _content.FindMissionEvent(incident.Mission.MissionEventId)
				: null;

			if (missionEvent == null)
			{
				incident.SetPhase(IncidentPhase.OnSite, incident.Mission.OnSiteSeconds);
				return;
			}

			double radioSeconds = GetTimer(
				_content.Config.GetDay(_state.Day),
				_content.Config.Timings.RadioSeconds);

			incident.MissionEvent = missionEvent;
			incident.RadioWasTriggered = true;
			incident.SetPhase(IncidentPhase.RadioPending, radioSeconds > 0.0 ? radioSeconds : (double?)null);

			// В событие уходят ключи и доступность: формулировки интерфейс возьмёт
			// из текстового движка по MissionEventId, в том же порядке.
			_bus.Publish(new RadioTriggered(incident.Id, missionEvent.Id, BuildOffers(incident), radioSeconds));
		}

		/// <summary>
		/// Что игрок может выбрать по этому вызову. Считается по уже отправленной группе:
		/// состав, собранный на экране отправки, решает, какие варианты останутся открыты.
		/// </summary>
		public IReadOnlyList<RadioOptionOffer> BuildOffers(IncidentRuntime incident)
		{
			var offers = new List<RadioOptionOffer>();
			if (incident.MissionEvent == null)
			{
				return offers;
			}

			StatBlock squadStats = GetSquadStats(incident);

			for (int i = 0; i < incident.MissionEvent.Options.Count; i++)
			{
				MissionEventOption option = incident.MissionEvent.Options[i];
				StatBlock shortfall = option.GetShortfall(squadStats);

				offers.Add(new RadioOptionOffer(
					option.Id,
					shortfall.Total == 0,
					option.Requirements,
					shortfall));
			}

			return offers;
		}

		/// <summary>Сумма характеристик отправленной группы — та же, что пойдёт в расчёт миссии.</summary>
		private StatBlock GetSquadStats(IncidentRuntime incident)
		{
			List<Employee> squad = ResolveSquad(incident);
			List<EquipmentDefinition> equipment = ResolveEquipment(incident);
			CreatureDefinition? creature = string.IsNullOrEmpty(incident.Mission.CreatureId)
				? null
				: _content.FindCreature(incident.Mission.CreatureId);

			return _resolver.ComputeSquadStats(squad, equipment, creature);
		}

		private void HandleRadioMissed(IncidentRuntime incident)
		{
			// ДД, раздел 4: не автопровал — бросок с повышенным шансом провала.
			incident.RadioWasMissed = true;
			_bus.Publish(new RadioMissed(incident.Id));
			incident.SetPhase(IncidentPhase.OnSite, incident.Mission.OnSiteSeconds);
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
			// Экран закрытого вызова держать мир не должен ни при каких обстоятельствах.
			ReleaseTime(DispatchHolder(incident.Id));
			ReleaseTime(RadioHolder(incident.Id));

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
				ZoneId = incident.Mission.ZoneId,
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

			Zone? zone = _state.FindZone(incident.Mission.ZoneId);
			if (zone != null)
			{
				_zones.ApplyMissionResult(zone, false);
			}

			CloseIncident(incident, false);
		}

		private void ResolveMission(IncidentRuntime incident)
		{
			MissionDefinition mission = incident.Mission;
			Zone? zone = _state.FindZone(mission.ZoneId);

			List<Employee> squad = ResolveSquad(incident);
			List<EquipmentDefinition> equipment = ResolveEquipment(incident);
			CreatureDefinition? creature = _content.FindCreature(mission.CreatureId);

			StatBlock requirements = _resolver.ComputeEffectiveRequirements(
				mission,
				zone,
				_zones,
				incident.ChosenOption,
				_state.Day);

			StatBlock squadStats = _resolver.ComputeSquadStats(squad, equipment, creature);

			var request = new ResolutionRequest
			{
				IncidentId = incident.Id,
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

			if (zone != null)
			{
				_zones.ApplyMissionResult(zone, outcome.IsSuccess);

				if (incident.ChosenOption != null && incident.ChosenOption.AppliesQuarantine)
				{
					_zones.ApplyQuarantine(zone);
				}
			}

			_roster.GrantExperience(
				outcome.EmployeeIds,
				outcome.IsSuccess ? mission.ExperienceOnSuccess : mission.ExperienceOnFailure);

			List<string> revealed = _encyclopedia.ProcessMissionResult(mission, outcome, incident.ChosenOption);

			if (outcome.IsSuccess)
			{
				TryFindConsumable();
			}

			incident.Report = BuildReport(incident, outcome, creature, revealed);

			// Экран итога показывается до возвращения: игрок узнаёт, чем кончилось,
			// в момент, когда это произошло, а не через минуту дороги обратно.
			double returnSeconds = outcome.SquadWiped ? 0.0 : mission.ReturnSeconds;
			PublishOutcomeScreen(incident, outcome, returnSeconds);

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

			incident.SetPhase(IncidentPhase.Returning, returnSeconds);
		}

		/// <summary>
		/// Сигнал под экран итога. Текст берётся тот же, что потом ляжет в архив отчётов
		/// на компьютере: писать две версии одного и того же исхода автору незачем,
		/// а игрок в архиве перечитывает ровно то, что видел на экране.
		/// </summary>
		private void PublishOutcomeScreen(IncidentRuntime incident, MissionOutcome outcome, double returnSeconds)
		{
			var returning = new List<string>();
			for (int i = 0; i < outcome.EmployeeIds.Count; i++)
			{
				string employeeId = outcome.EmployeeIds[i];
				if (!outcome.KilledEmployeeIds.Contains(employeeId))
				{
					returning.Add(employeeId);
				}
			}

			_bus.Publish(new MissionOutcomeReady(
				incident.Id,
				incident.Mission.Id,
				incident.Mission.ZoneId,
				outcome.IsSuccess,
				outcome.Reason,
				incident.Report == null ? string.Empty : incident.Report.ReportId,
				incident.Report == null ? string.Empty : incident.Report.CreatureId,
				returning,
				outcome.InjuredEmployeeIds.ToArray(),
				outcome.KilledEmployeeIds.ToArray(),
				returnSeconds,
				outcome.SquadWiped));
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
			string optionId = incident.ChosenOption == null ? string.Empty : incident.ChosenOption.Id;

			var report = new MissionReport
			{
				IncidentId = incident.Id,
				MissionId = incident.Mission.Id,
				ReportId = incident.Mission.ResolveReportId(optionId, outcome.IsSuccess),
				ChosenOptionId = optionId,
				IsSuccess = outcome.IsSuccess
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

		/// <summary>Требования миссии с учётом дня, штриховки зоны и уже выбранного радио-варианта — для экрана отправки.</summary>
		public StatBlock GetCurrentRequirements(IncidentRuntime incident)
		{
			Zone? zone = _state.FindZone(incident.Mission.ZoneId);
			return _resolver.ComputeEffectiveRequirements(incident.Mission, zone, _zones, incident.ChosenOption, _state.Day);
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
			CreatureDefinition? creature = string.IsNullOrEmpty(incident.Mission.CreatureId)
				? null
				: _content.FindCreature(incident.Mission.CreatureId);

			StatBlock squadStats = _resolver.ComputeSquadStats(squad, equipment, creature);

			IReadOnlyList<StatMatch> matches = _resolver.EvaluateMatches(
				requirements,
				squadStats,
				incident.Mission.PrimaryStat);

			double matchScore = _resolver.ComputeMatchScore(matches);
			bool isPerfect = MissionResolver.IsPerfectMatch(matches);
			double chance = isPerfect
				? 1.0
				: _resolver.ComputeSuccessChance(matchScore, equipment, incident.RadioWasMissed);

			return new DispatchEstimateView(requirements, squadStats, matches, matchScore, chance, isPerfect);
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

			incident.SetPhase(IncidentPhase.Briefing, null);
			_bus.Publish(new CallAnswered(incident.Id, incident.Mission.Id, incident.Mission.CallId));

			return CommandResult.Ok();
		}

		/// <summary>Кнопка ОК на экране задания: закрывает экран и ставит метку на карте (ДД, раздел 2).</summary>
		public CommandResult ConfirmBriefing(string incidentId)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			if (incident.Phase != IncidentPhase.Briefing)
			{
				return CommandResult.Fail("Экран задания не открыт.");
			}

			double markerSeconds = GetTimer(
				_content.Config.GetDay(_state.Day),
				_content.Config.Timings.MapMarkerSeconds);

			// Бланк закрыт, разговор окончен — телефон снова свободен.
			ReleaseLine();

			incident.SetPhase(IncidentPhase.MarkerActive, markerSeconds > 0.0 ? markerSeconds : (double?)null);
			_bus.Publish(new MapMarkerSpawned(incident.Id, incident.Mission.ZoneId, incident.BuildingId, markerSeconds));

			return CommandResult.Ok();
		}

		/// <summary>Нажатие на метку карты — компьютер открывает экран отправки.</summary>
		/// <summary>
		/// Игрок нажал на метку. Экран отправки открыт — мир останавливается: игрок
		/// сравнивает характеристики и читает досье, а не гонится за секундомером.
		/// Таймер метки при этом не сбрасывается: он давит на то, чтобы заметить вызов,
		/// а не на то, чтобы быстро решить.
		/// </summary>
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

			HoldTime(DispatchHolder(incident.Id));
			_bus.Publish(new DispatchScreenRequested(incident.Id, incident.Mission.Id));
			return CommandResult.Ok();
		}

		/// <summary>
		/// Экран отправки закрыт без отправки. Время идёт дальше с того же места —
		/// заглянуть в досье и передумать можно без штрафа.
		/// </summary>
		public CommandResult CloseDispatchScreen(string incidentId)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			ReleaseTime(DispatchHolder(incident.Id));
			_bus.Publish(new DispatchScreenClosed(incident.Id));
			return CommandResult.Ok();
		}

		/// <summary>
		/// Игрок взял радио. Экран вариантов открыт — мир останавливается.
		/// До этого момента тикают отведённые на реакцию секунды: не успел взять —
		/// RadioMissed и бросок с повышенным риском.
		/// </summary>
		public CommandResult AnswerRadio(string incidentId)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			if (incident.Phase != IncidentPhase.RadioPending || incident.MissionEvent == null)
			{
				return CommandResult.Fail("Радио по этому вызову не активно.");
			}

			HoldTime(RadioHolder(incident.Id));
			_bus.Publish(new RadioAnswered(incident.Id, incident.MissionEvent.Id));
			return CommandResult.Ok();
		}

		/// <summary>
		/// Игрок раскрыл экран итога миссии — мир останавливается, пока он читает.
		///
		/// Открывается по желанию, а не само собой: сигнал MissionOutcomeReady приходит
		/// в момент разрешения, и если в этот миг звонит телефон, отнимать у игрока
		/// управление ради текста, который никуда не денется, неправильно.
		///
		/// Инцидент искать не обязательно — читать итог можно и после того, как вызов
		/// закрылся: при полной гибели группы он закрывается сразу.
		/// </summary>
		public CommandResult OpenMissionOutcome(string incidentId)
		{
			if (string.IsNullOrEmpty(incidentId))
			{
				return CommandResult.Fail("Не указан вызов.");
			}

			HoldTime(OutcomeHolder(incidentId));
			return CommandResult.Ok();
		}

		/// <summary>Экран итога закрыт, время идёт дальше.</summary>
		public CommandResult CloseMissionOutcome(string incidentId)
		{
			if (string.IsNullOrEmpty(incidentId))
			{
				return CommandResult.Fail("Не указан вызов.");
			}

			ReleaseTime(OutcomeHolder(incidentId));
			return CommandResult.Ok();
		}

		/// <summary>Радио отложено без выбора: отсчёт продолжается с того же места.</summary>
		public CommandResult CloseRadio(string incidentId)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			ReleaseTime(RadioHolder(incident.Id));
			return CommandResult.Ok();
		}

		public CommandResult DispatchSquad(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds)
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

			// Сколько человек берёт вызов — решает автор миссии, а не игрок.
			// Характеристики отряда складываются, поэтому без этого предела
			// на любой вызов было бы выгодно отправлять всех свободных.
			int squadLimit = incident.Mission.SquadLimit;
			if (employeeIds.Count > squadLimit)
			{
				return CommandResult.Fail(squadLimit == 1
					? "На этот вызов едет только один оперативник."
					: $"На этот вызов можно отправить не больше {squadLimit}.");
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

			// Группа ушла — экран отправки закрылся, мир снова идёт.
			ReleaseTime(DispatchHolder(incident.Id));

			_roster.MarkOnMission(squad, incident.Id);
			incident.SetPhase(IncidentPhase.Travelling, incident.Mission.TravelSeconds);

			_bus.Publish(new SquadDispatched(
				incident.Id,
				incident.SquadEmployeeIds.ToArray(),
				incident.EquipmentIds.ToArray(),
				incident.Mission.TravelSeconds));

			return CommandResult.Ok();
		}

		public CommandResult ChooseRadioOption(string incidentId, string optionId)
		{
			IncidentRuntime? incident = FindIncident(incidentId);
			if (incident == null)
			{
				return CommandResult.Fail("Вызов не найден.");
			}

			if (incident.Phase != IncidentPhase.RadioPending || incident.MissionEvent == null)
			{
				return CommandResult.Fail("Радио по этому вызову не активно.");
			}

			MissionEventOption? option = incident.MissionEvent.FindOption(optionId);
			if (option == null)
			{
				return CommandResult.Fail($"Вариант '{optionId}' не найден.");
			}

			// Вариант закрыт составом группы. Отказ с причиной, а не молчаливое игнорирование:
			// интерфейс покажет игроку, чего не хватило, и в следующий раз он отправит другого.
			StatBlock shortfall = option.GetShortfall(GetSquadStats(incident));
			if (shortfall.Total > 0)
			{
				return CommandResult.Fail($"Группе не хватает: {shortfall}.");
			}

			// Решение принято — экран закрылся, мир пошёл дальше.
			ReleaseTime(RadioHolder(incident.Id));

			incident.ChosenOption = option;
			_bus.Publish(new RadioOptionChosen(incident.Id, incident.MissionEvent.Id, option.Id));

			incident.SetPhase(IncidentPhase.OnSite, incident.Mission.OnSiteSeconds);
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

		// ------------------------------------------------------------------ сохранение

		/// <summary>
		/// Снимок смены. Берётся как есть, без попытки «округлить до ближайшей фазы»:
		/// игрок сохранился на восьмой секунде звонка — значит, и загрузится на восьмой.
		/// </summary>
		public SavedShift CaptureShift()
		{
			var saved = new SavedShift
			{
				IsActive = IsShiftActive,
				ShiftTime = _shiftTime,
				LineFreeAt = _lineFreeAt,
				CallWindowClosed = _callWindowClosed,
				SpawnedCount = _spawnedCount,
				TotalIncidents = _counters.TotalIncidents,
				Successes = _counters.Successes,
				Failures = _counters.Failures,
				MissedCalls = _counters.MissedCalls,
				ExpiredMarkers = _counters.ExpiredMarkers,
				Injuries = _counters.Injuries,
				Deaths = _counters.Deaths
			};

			for (int i = 0; i < _pending.Count; i++)
			{
				saved.Pending.Add(CaptureIncident(_pending[i]));
			}

			for (int i = 0; i < _incidents.Count; i++)
			{
				saved.Incidents.Add(CaptureIncident(_incidents[i]));
			}

			return saved;
		}

		private static SavedIncident CaptureIncident(IncidentRuntime incident)
		{
			var saved = new SavedIncident
			{
				Id = incident.Id,
				MissionId = incident.Mission.Id,
				BuildingId = incident.BuildingId,
				Phase = incident.Phase.ToString(),
				ScheduledAtSeconds = incident.ScheduledAtSeconds,
				HasTimer = incident.Timer != null,
				MissionEventId = incident.MissionEvent == null ? string.Empty : incident.MissionEvent.Id,
				ChosenOptionId = incident.ChosenOption == null ? string.Empty : incident.ChosenOption.Id,
				RadioWasTriggered = incident.RadioWasTriggered,
				RadioWasMissed = incident.RadioWasMissed,
				HasOutcome = incident.Outcome != null,
				OutcomeWasSuccess = incident.Outcome != null && incident.Outcome.IsSuccess
			};

			if (incident.Timer != null)
			{
				saved.TimerDuration = incident.Timer.Duration;
				saved.TimerRemaining = incident.Timer.Remaining;
				saved.TimerRunning = incident.Timer.IsRunning;
			}

			saved.SquadEmployeeIds.AddRange(incident.SquadEmployeeIds);
			saved.EquipmentIds.AddRange(incident.EquipmentIds);

			if (incident.Report != null)
			{
				saved.Report = SaveSystem.CaptureReport(incident.Report);
			}

			return saved;
		}

		/// <summary>
		/// Восстановление смены. Событий не публикует: интерфейс после загрузки
		/// перерисовывается по снимкам Get*, а не по потоку событий, которого уже не было.
		/// </summary>
		public void RestoreShift(SavedShift? saved)
		{
			AbortShift();

			if (saved == null)
			{
				return;
			}

			IsShiftActive = saved.IsActive;
			_shiftTime = saved.ShiftTime;
			_lineFreeAt = saved.LineFreeAt;
			_callWindowClosed = saved.CallWindowClosed;
			_spawnedCount = saved.SpawnedCount;
			_counters = new ShiftCounters
			{
				TotalIncidents = saved.TotalIncidents,
				Successes = saved.Successes,
				Failures = saved.Failures,
				MissedCalls = saved.MissedCalls,
				ExpiredMarkers = saved.ExpiredMarkers,
				Injuries = saved.Injuries,
				Deaths = saved.Deaths
			};

			for (int i = 0; i < saved.Pending.Count; i++)
			{
				IncidentRuntime? incident = RestoreIncident(saved.Pending[i]);
				if (incident != null)
				{
					_pending.Add(incident);
				}
			}

			for (int i = 0; i < saved.Incidents.Count; i++)
			{
				IncidentRuntime? incident = RestoreIncident(saved.Incidents[i]);
				if (incident != null)
				{
					_incidents.Add(incident);
				}
			}
		}

		private IncidentRuntime? RestoreIncident(SavedIncident saved)
		{
			MissionDefinition? mission = _content.FindMission(saved.MissionId);
			if (mission == null)
			{
				// До сюда доходить не должно: SaveSystem сверяет ссылки до начала восстановления.
				return null;
			}

			var incident = new IncidentRuntime(saved.Id, mission)
			{
				BuildingId = saved.BuildingId,
				ScheduledAtSeconds = saved.ScheduledAtSeconds,
				RadioWasTriggered = saved.RadioWasTriggered,
				RadioWasMissed = saved.RadioWasMissed
			};

			IncidentPhase phase;
			incident.Phase = Enum.TryParse<IncidentPhase>(saved.Phase, true, out phase)
				? phase
				: IncidentPhase.Scheduled;

			incident.Timer = saved.HasTimer
				? Countdown.Restore(saved.TimerDuration, saved.TimerRemaining, saved.TimerRunning)
				: null;

			incident.SquadEmployeeIds.AddRange(saved.SquadEmployeeIds);
			incident.EquipmentIds.AddRange(saved.EquipmentIds);

			if (!string.IsNullOrEmpty(saved.MissionEventId))
			{
				incident.MissionEvent = _content.FindMissionEvent(saved.MissionEventId);
			}

			if (incident.MissionEvent != null && !string.IsNullOrEmpty(saved.ChosenOptionId))
			{
				for (int i = 0; i < incident.MissionEvent.Options.Count; i++)
				{
					if (string.Equals(
						incident.MissionEvent.Options[i].Id,
						saved.ChosenOptionId,
						StringComparison.OrdinalIgnoreCase))
					{
						incident.ChosenOption = incident.MissionEvent.Options[i];
						break;
					}
				}
			}

			if (saved.Report != null)
			{
				incident.Report = SaveSystem.RestoreReport(saved.Report);
			}

			// Полный MissionOutcome не восстанавливается: он нужен только внутри одного
			// разрешения миссии. После него от исхода остаётся ровно один вопрос —
			// с успехом закрывать вызов или с провалом.
			if (saved.HasOutcome)
			{
				incident.Outcome = new MissionOutcome
				{
					IncidentId = saved.Id,
					MissionId = mission.Id,
					ZoneId = mission.ZoneId,
					CreatureId = mission.CreatureId,
					Kind = saved.OutcomeWasSuccess ? MissionResultKind.Success : MissionResultKind.Failure
				};

				incident.Outcome.EmployeeIds.AddRange(saved.SquadEmployeeIds);
			}

			return incident;
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
