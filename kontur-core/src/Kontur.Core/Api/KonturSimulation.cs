using System;
using System.Collections.Generic;
using System.Linq;
using Kontur.Core.Config;
using Kontur.Core.Content;
using Kontur.Core.Events;
using Kontur.Core.Model;
using Kontur.Core.Persistence;
using Kontur.Core.Simulation;
using Kontur.Core.Systems;

namespace Kontur.Core.Api
{
	/// <summary>
	/// Единственная точка входа для внешнего слоя (Godot, консольный харнесс, тесты).
	///
	/// Контракт:
	///   • состояние меняется только через команды и Tick;
	///   • всё, что произошло, приходит подписчику через Events;
	///   • ни одного обращения к движку — ядро собирается и запускается без Godot.
	/// </summary>
	public sealed class KonturSimulation
	{
		private readonly EventBus _bus;
		private readonly GameState _state;
		/// <summary>
		/// Конкретный тип, а не IRandomSource: при сохранении нужно снять и вернуть
		/// внутреннее состояние генератора, иначе загруженная партия пойдёт по другим броскам.
		/// </summary>
		private readonly XorShiftRandom _random;
		private readonly ScalesSystem _scalesSystem;
		private readonly ZoneSystem _zoneSystem;
		private readonly EmployeeFactory _employeeFactory;
		private readonly RosterSystem _rosterSystem;
		private readonly EncyclopediaSystem _encyclopediaSystem;
		private readonly MissionResolver _resolver;
		private readonly IncidentScheduler _scheduler;
		private readonly ShiftDirector _director;

		public KonturSimulation(ContentDatabase content, int seed)
		{
			Content = content ?? throw new ArgumentNullException(nameof(content));
			Seed = seed;

			_bus = new EventBus();
			_state = new GameState();
			_random = new XorShiftRandom(seed);

			_scalesSystem = new ScalesSystem(_state, content.Config.Scales, _bus);
			_zoneSystem = new ZoneSystem(_state, content.Config.Zones, _bus);
			_employeeFactory = new EmployeeFactory(content, _random);
			_rosterSystem = new RosterSystem(_state, content, content.Config.Employees, _bus, _employeeFactory);
			_encyclopediaSystem = new EncyclopediaSystem(_state, content, _bus);
			_resolver = new MissionResolver(content, content.Config, _random);
			_scheduler = new IncidentScheduler(content, _state, _zoneSystem, _random);

			_director = new ShiftDirector(
				_state,
				content,
				_bus,
				_random,
				_scalesSystem,
				_zoneSystem,
				_rosterSystem,
				_encyclopediaSystem,
				_resolver,
				_scheduler);

			ResetToNewGame();
		}

		public ContentDatabase Content { get; }

		public int Seed { get; }

		public IEventBus Events
		{
			get { return _bus; }
		}

		public SimulationConfig Config
		{
			get { return Content.Config; }
		}

		public bool IsGameOver
		{
			get { return _state.IsGameOver; }
		}

		public bool IsShiftActive
		{
			get { return _director.IsShiftActive; }
		}

		/// <summary>
		/// Мир остановлен: открыт экран отправки или радио. Tick в этом состоянии
		/// ничего не продвигает — ни таймеры, ни дорогу группы, ни приём новых звонков.
		/// </summary>
		public bool IsTimeFrozen
		{
			get { return _director.IsTimeFrozen; }
		}

		public int Day
		{
			get { return _state.Day; }
		}

		/// <summary>Только для отладочных инструментов и тестов; UI должен работать через View-методы.</summary>
		public GameState DebugState
		{
			get { return _state; }
		}

		// ------------------------------------------------------------------ жизненный цикл

		public void ResetToNewGame()
		{
			// Смену обрываем первой: иначе оставшиеся инциденты продолжат тикать
			// и закроют уже несуществующую партию событием ShiftEnded с Day = 0.
			_director.AbortShift();

			_state.Encyclopedia.Clear();
			_state.Flags.Clear();
			_state.Inventory.Clear();

			_state.Zones.Clear();
			foreach (KeyValuePair<string, Zone> pair in Content.Zones)
			{
				_state.Zones[pair.Key] = new Zone
				{
					Id = pair.Value.Id,
					Name = pair.Value.Name,
					State = pair.Value.State,
					BaseWeight = pair.Value.BaseWeight,
					MapX = pair.Value.MapX,
					MapY = pair.Value.MapY
				};
			}

			_state.Roster.Clear();
			for (int i = 0; i < Content.StartingRoster.Count; i++)
			{
				_state.Roster.Add(Content.StartingRoster[i].Clone());
			}

			_state.Reports.Clear();
			_state.UsedMissionIds.Clear();
			_state.HiredCandidateIds.Clear();
			_state.HireOffers.Clear();
			_state.HireOffersDay = -1;
			_state.Day = 0;

			_scalesSystem.Reset();
		}

		public CommandResult StartShift(int day)
		{
			if (_state.IsGameOver)
			{
				return CommandResult.Fail("Партия завершена.");
			}

			if (_director.IsShiftActive)
			{
				return CommandResult.Fail("Смена уже идёт.");
			}

			_director.StartShift(day);
			return CommandResult.Ok();
		}

		/// <summary>Продвинуть симуляцию. delta — секунды. В Godot вызывается из _Process.</summary>
		public void Tick(double deltaSeconds)
		{
			_director.Tick(deltaSeconds);
		}

		public void ForceEndShift()
		{
			_director.EndShift();
		}

		// ------------------------------------------------------------------ команды игрока

		public CommandResult AnswerCall(string incidentId)
		{
			return _director.AnswerCall(incidentId);
		}

		public CommandResult ConfirmBriefing(string incidentId)
		{
			return _director.ConfirmBriefing(incidentId);
		}

		/// <summary>Открыть экран отправки. Глобальное время останавливается.</summary>
		public CommandResult OpenDispatchScreen(string incidentId)
		{
			return _director.OpenDispatchScreen(incidentId);
		}

		/// <summary>Закрыть экран отправки, никого не отправив. Время идёт дальше.</summary>
		public CommandResult CloseDispatchScreen(string incidentId)
		{
			return _director.CloseDispatchScreen(incidentId);
		}

		/// <summary>Взять радио. Глобальное время останавливается до выбора варианта.</summary>
		public CommandResult AnswerRadio(string incidentId)
		{
			return _director.AnswerRadio(incidentId);
		}

		/// <summary>Отложить радио без выбора. Отсчёт до RadioMissed продолжается.</summary>
		public CommandResult CloseRadio(string incidentId)
		{
			return _director.CloseRadio(incidentId);
		}

		/// <summary>
		/// Раскрыть экран итога миссии. Глобальное время останавливается, пока он открыт.
		/// Вызывать не обязательно: если итог показывается всплывающей плашкой, которая
		/// не мешает играть, эту пару команд можно не трогать.
		/// </summary>
		public CommandResult OpenMissionOutcome(string incidentId)
		{
			return _director.OpenMissionOutcome(incidentId);
		}

		/// <summary>Закрыть экран итога. Обязателен, если открывали: иначе мир так и стоит.</summary>
		public CommandResult CloseMissionOutcome(string incidentId)
		{
			return _director.CloseMissionOutcome(incidentId);
		}

		public CommandResult DispatchSquad(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds)
		{
			return _director.DispatchSquad(incidentId, employeeIds, equipmentIds);
		}

		public CommandResult ChooseRadioOption(string incidentId, string optionId)
		{
			return _director.ChooseRadioOption(incidentId, optionId);
		}

		/// <summary>
		/// Варианты по радио с их доступностью. То же, что пришло в RadioTriggered, —
		/// на случай, если экран открылся позже события или перерисовывается.
		/// Пустой список, если радио по этому вызову не активно.
		/// </summary>
		public IReadOnlyList<RadioOptionOffer> GetRadioOptions(string incidentId)
		{
			IncidentRuntime? incident = _director.FindIncident(incidentId);
			return incident == null
				? new List<RadioOptionOffer>()
				: _director.BuildOffers(incident);
		}

		/// <summary>
		/// Предпросмотр состава для экрана отправки: требования, сумма группы, покрытие, шанс.
		/// Ничего не меняет — можно дёргать на каждое переключение галочки.
		/// </summary>
		public DispatchEstimateView? EstimateDispatch(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds)
		{
			return _director.Estimate(incidentId, employeeIds, equipmentIds);
		}

		public CommandResult SpendSkillPoint(string employeeId, StatKind stat)
		{
			Employee? employee = _state.FindEmployee(employeeId);
			if (employee == null)
			{
				return CommandResult.Fail("Сотрудник не найден.");
			}

			string error;
			return _rosterSystem.TrySpendSkillPoint(employee, stat, out error)
				? CommandResult.Ok()
				: CommandResult.Fail(error);
		}

		/// <summary>Найм между сменами, в пределах лимита штата текущего дня (ДД, раздел 5).</summary>
		public CommandResult HireEmployee(string candidateId, int day)
		{
			if (_director.IsShiftActive)
			{
				return CommandResult.Fail("Найм доступен только между сменами.");
			}

			string error;
			return _rosterSystem.TryHire(candidateId, day, out error)
				? CommandResult.Ok()
				: CommandResult.Fail(error);
		}

		// ------------------------------------------------------------------ снимки для UI

		public ShiftStatusView GetStatus()
		{
			int open = 0;
			IReadOnlyList<IncidentRuntime> incidents = _director.Incidents;
			for (int i = 0; i < incidents.Count; i++)
			{
				if (!incidents[i].IsClosed)
				{
					open++;
				}
			}

			return new ShiftStatusView(
				_state.Day,
				_director.IsShiftActive,
				_director.ShiftTime,
				_director.IsCallWindowClosed,
				open,
				_director.PendingCallCount,
				_director.CountQueued(),
				_rosterSystem.GetStaffLimit(_state.Day),
				_director.IsTimeFrozen,
				_state.Scales,
				_state.IsGameOver,
				_state.GameOverReason);
		}

		public IReadOnlyList<EmployeeView> GetRoster()
		{
			var result = new List<EmployeeView>();
			for (int i = 0; i < _state.Roster.Count; i++)
			{
				Employee employee = _state.Roster[i];

				result.Add(new EmployeeView(
					employee.Id,
					employee.Name,
					employee.RankTitle,
					employee.Level,
					employee.BaseStats,
					employee.Experience,
					_rosterSystem.GetExperienceForNextLevel(employee),
					employee.UnspentSkillPoints,
					employee.Status,
					employee.IsInjured,
					employee.CurrentIncidentId,
					new List<string>(employee.AbilityIds),
					employee.PortraitId));
			}

			return result;
		}

		public IReadOnlyList<IncidentView> GetActiveIncidents()
		{
			var result = new List<IncidentView>();
			IReadOnlyList<IncidentRuntime> incidents = _director.Incidents;

			for (int i = 0; i < incidents.Count; i++)
			{
				IncidentRuntime incident = incidents[i];
				if (incident.IsClosed)
				{
					continue;
				}

				result.Add(new IncidentView(
					incident.Id,
					incident.Mission.Id,
					incident.Mission.CallId,
					incident.Mission.ZoneId,
					incident.BuildingId,
					incident.Mission.MissionEventId,
					incident.Mission.Tier,
					incident.Mission.EffectiveCap,
					incident.Phase,
					incident.RemainingSeconds,
					_director.GetCurrentRequirements(incident),
					incident.SquadEmployeeIds.ToArray(),
					incident.EquipmentIds.ToArray()));
			}

			return result;
		}

		public IReadOnlyList<ZoneView> GetZones()
		{
			var result = new List<ZoneView>();
			foreach (KeyValuePair<string, Zone> pair in _state.Zones)
			{
				Zone zone = pair.Value;
				result.Add(new ZoneView(zone.Id, zone.Name, zone.State, zone.MapX, zone.MapY));
			}

			return result;
		}

		public IReadOnlyList<EquipmentSlotView> GetAvailableEquipment()
		{
			var result = new List<EquipmentSlotView>();
			foreach (KeyValuePair<string, EquipmentStack> pair in _state.Inventory.Stacks)
			{
				if (pair.Value.Quantity <= 0)
				{
					continue;
				}

				EquipmentDefinition? definition = Content.FindEquipment(pair.Key);
				if (definition == null)
				{
					continue;
				}

				result.Add(new EquipmentSlotView(
					definition.Id,
					definition.Name,
					definition.Description,
					definition.Kind,
					pair.Value.Quantity,
					pair.Value.IsShiftOnly));
			}

			return result;
		}

		// ------------------------------------------------------------------ сюжетные флаги

		public bool IsFlagSet(string flag)
		{
			return _state.Flags.IsSet(flag);
		}

		public IReadOnlyCollection<string> GetFlags()
		{
			return _state.Flags.All;
		}

		/// <summary>Ставит или снимает флаг. Событие уходит только при реальном изменении.</summary>
		public void SetFlag(string flag, bool value = true)
		{
			if (_state.Flags.Set(flag, value))
			{
				_bus.Publish(new FlagChanged(flag, value));
			}
		}

		/// <summary>Переключает флаг и возвращает новое значение.</summary>
		public bool ToggleFlag(string flag)
		{
			if (string.IsNullOrEmpty(flag))
			{
				return false;
			}

			bool value = _state.Flags.Toggle(flag);
			_bus.Publish(new FlagChanged(flag, value));
			return value;
		}

		/// <summary>
		/// Раскрыто ли конкретное свойство существа. Нужно текстовым виджетам: в текстах
		/// условные абзацы помечены id свойства, а не номером абзаца, поэтому
		/// GetEncyclopedia() с готовыми абзацами им не подходит.
		/// </summary>
		public bool IsPropertyRevealed(string creatureId, string propertyId)
		{
			return _state.Encyclopedia.IsPropertyRevealed(creatureId, propertyId);
		}

		public IReadOnlyList<EncyclopediaEntryView> GetEncyclopedia()
		{
			var result = new List<EncyclopediaEntryView>();

			foreach (string creatureId in _state.Encyclopedia.GetKnownCreatureIds())
			{
				CreatureDefinition? creature = Content.FindCreature(creatureId);
				if (creature == null)
				{
					continue;
				}

				// Порядок свойств берём из определения существа, а не из множества
				// раскрытых: снимок должен быть стабильным между вызовами.
				var revealedIds = new List<string>();
				for (int i = 0; i < creature.Properties.Count; i++)
				{
					if (_state.Encyclopedia.IsPropertyRevealed(creature.Id, creature.Properties[i]))
					{
						revealedIds.Add(creature.Properties[i]);
					}
				}

				result.Add(new EncyclopediaEntryView(
					creature.Id,
					creature.IllustrationId,
					revealedIds,
					creature.Properties.Count));
			}

			return result;
		}

		/// <summary>
		/// Кандидаты на найм. Набор фиксируется на день: повторный вызов вернёт тех же
		/// людей в том же порядке, поэтому список можно спрашивать хоть каждый кадр.
		/// </summary>
		public IReadOnlyList<HireCandidateView> GetHireCandidates(int day)
		{
			var result = new List<HireCandidateView>();
			IReadOnlyList<HireCandidate> candidates = _rosterSystem.GetAvailableCandidates(day);

			for (int i = 0; i < candidates.Count; i++)
			{
				Employee template = candidates[i].Template;
				result.Add(new HireCandidateView(
					template.Id,
					template.Name,
					template.RankTitle,
					template.Level,
					template.BaseStats,
					template.AbilityIds.ToArray(),
					template.PortraitId));
			}

			return result;
		}

		/// <summary>
		/// Пересобрать список кандидатов на день заново. Нужно редко — набор и так
		/// обновляется сам при переходе на новый день. Годится для отладки и для
		/// сюжетного «список обновили».
		/// </summary>
		public void RefreshHireCandidates(int day)
		{
			_rosterSystem.RefreshHireOffers(day);
		}

		public IReadOnlyList<MissionReport> GetReports()
		{
			return _state.Reports.ToArray();
		}

		// ------------------------------------------------------------------ сохранение

		/// <summary>
		/// Снимок партии в JSON. Работает в любой момент, в том числе посреди смены:
		/// фазы вызовов, недотикавшие таймеры и состояние генератора случайных чисел
		/// попадают в файл, поэтому после загрузки прогон продолжается тем же, каким был.
		///
		/// Побочных эффектов нет — ни событий, ни остановки времени. Если сохранение
		/// по кнопке должно приостанавливать игру, это решает интерфейс.
		/// </summary>
		public string Save(string label = "")
		{
			SaveData data = SaveSystem.Capture(_state, _director, Seed, _random.State);
			data.Label = label ?? string.Empty;
			return SaveSystem.ToJson(data);
		}

		/// <summary>
		/// Загрузка партии из JSON.
		///
		/// После успешной загрузки мир **остановлен** и ждёт <see cref="ResumeAfterLoad"/>.
		/// Сделано нарочно: интерфейсу нужно время перерисовать стол по снимкам Get*,
		/// а игроку — сообразить, где он. Иначе таймер звонка, на котором сохранились,
		/// продолжил бы тикать под ещё не нарисованным телефоном.
		///
		/// Модальные экраны — отправки, радио, итога — после загрузки считаются закрытыми.
		/// Что открыть заново, видно по фазам вызовов в GetActiveIncidents().
		///
		/// При отказе состояние не трогается: партия остаётся ровно такой, какой была.
		/// </summary>
		public CommandResult Load(string json)
		{
			string error;
			SaveData? data = SaveSystem.FromJson(json, out error);
			if (data == null)
			{
				return CommandResult.Fail(error);
			}

			if (!SaveSystem.Validate(data, Content, out error))
			{
				return CommandResult.Fail(error);
			}

			_director.AbortShift();
			SaveSystem.Apply(data, _state, Content);
			_random.State = data.RandomState;
			_director.RestoreShift(data.Shift);
			_director.HoldAfterLoad();

			_bus.Publish(new GameLoaded(_state.Day, data.SavedAtUtc, data.Label));
			return CommandResult.Ok();
		}

		/// <summary>
		/// Пустить время после загрузки. Вызывается, когда интерфейс закончил перерисовку.
		/// Без этого вызова мир так и стоит.
		/// </summary>
		public void ResumeAfterLoad()
		{
			_director.ReleaseAfterLoad();
		}
	}
}
