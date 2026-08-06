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
		private readonly XorShiftRandom _random;
		private readonly ScalesSystem _scalesSystem;
		private readonly EmployeeFactory _employeeFactory;
		private readonly RosterSystem _rosterSystem;
		private readonly EncyclopediaSystem _encyclopediaSystem;
		private readonly MissionResolver _resolver;
		private readonly IncidentScheduler _scheduler;
		private readonly ShiftDirector _director;
		private readonly HashSet<string> _timeFreezeOwners = new HashSet<string>(StringComparer.Ordinal);

		public KonturSimulation(ContentDatabase content, int seed)
		{
			Content = content ?? throw new ArgumentNullException(nameof(content));
			Seed = seed;

			_bus = new EventBus();
			_state = new GameState();
			_random = new XorShiftRandom(seed);

			_scalesSystem = new ScalesSystem(_state, content.Config.Scales, _bus);
			_employeeFactory = new EmployeeFactory(content, _random);
			_rosterSystem = new RosterSystem(_state, content, content.Config.Employees, _bus, _employeeFactory);
			_encyclopediaSystem = new EncyclopediaSystem(_state, content, _bus);
			_resolver = new MissionResolver(content, content.Config, _random);
			_scheduler = new IncidentScheduler(content, _state, _random);

			_director = new ShiftDirector(
				_state,
				content,
				_bus,
				_random,
				_scalesSystem,
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

		/// <summary>Истина, пока хотя бы один модальный интерфейс или внешний слой удерживает время.</summary>
		public bool IsTimeFrozen
		{
			get { return _timeFreezeOwners.Count > 0; }
		}

		public IReadOnlyCollection<string> TimeFreezeOwners
		{
			get { return _timeFreezeOwners; }
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
			_state.StartingChoice.Clear();
			_state.StartingRosterConfirmed = false;
			_state.Day = 0;

			_scalesSystem.Reset();

			// Автонабор идёт последним и вытесняет состав, расписанный в контенте.
			// Обратный порядок был бы логичнее, но тогда отключить генерацию можно
			// было бы только удалив людей из employees.json — а они там нужны как
			// готовый именной состав для отладки и как образец разметки.
			_rosterSystem.FillStartingRoster();
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
			if (IsTimeFrozen)
			{
				return;
			}

			_director.Tick(deltaSeconds);
		}

		/// <summary>Добавляет владельца паузы. Повторный вызов тем же ключом безопасен.</summary>
		public void FreezeTime(string owner)
		{
			SetTimeFreeze(owner, true);
		}

		/// <summary>Снимает паузу только указанного владельца, не затрагивая остальные модальные экраны.</summary>
		public void UnfreezeTime(string owner)
		{
			SetTimeFreeze(owner, false);
		}

		public void SetTimeFreeze(string owner, bool frozen)
		{
			if (string.IsNullOrWhiteSpace(owner))
			{
				throw new ArgumentException("Нужен непустой идентификатор владельца паузы.", nameof(owner));
			}

			bool changed = frozen ? _timeFreezeOwners.Add(owner) : _timeFreezeOwners.Remove(owner);
			if (changed)
			{
				_bus.Publish(new TimeFreezeChanged(IsTimeFrozen, _timeFreezeOwners.ToArray()));
			}
		}

		public void ForceEndShift()
		{
			_director.EndShift();
		}

		/// <summary>
		/// Сериализует партию в любой фазе, включая таймеры и маршруты активной смены.
		/// </summary>
		public string Save(string label = "")
		{
			return SaveSystem.ToJson(SaveSystem.Capture(_state, _director, Seed, _random.State, label));
		}

		public CommandResult Load(string json)
		{
			SaveData? data = SaveSystem.FromJson(json, out string error);
			if (data == null)
			{
				return CommandResult.Fail(error);
			}

			if (!SaveSystem.Validate(data, Content, out error))
			{
				return CommandResult.Fail(error);
			}

			ResetToNewGame();
			SaveSystem.Apply(data, _state);
			_random.RestoreState(data.RandomState);
			_director.RestoreShift(data.Shift);
			FreezeTime("load");
			return CommandResult.Ok();
		}

		/// <summary>Вызывается UI после восстановления кадров карты и интерфейсов.</summary>
		public void ResumeAfterLoad()
		{
			UnfreezeTime("load");
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

		public CommandResult OpenDispatchScreen(string incidentId)
		{
			CommandResult result = _director.OpenDispatchScreen(incidentId);
			if (result.IsSuccess) FreezeTime("dispatch:" + incidentId);
			return result;
		}

		public CommandResult CloseDispatchScreen(string incidentId)
		{
			UnfreezeTime("dispatch:" + incidentId);
			_bus.Publish(new DispatchScreenClosed(incidentId));
			return CommandResult.Ok();
		}

		public CommandResult DispatchSquad(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds)
		{
			CommandResult result = _director.DispatchSquad(incidentId, employeeIds, equipmentIds);
			if (result.IsSuccess) CloseDispatchScreen(incidentId);
			return result;
		}

	/// <summary>
	/// Отправляет группу с длительностью пути, рассчитанной внешним слоем
	/// представления (например, длиной маршрута на карте).
	/// </summary>
		public CommandResult DispatchSquad(
			string incidentId,
			IReadOnlyList<string> employeeIds,
			IReadOnlyList<string> equipmentIds,
			double travelSeconds)
		{
			CommandResult result = _director.DispatchSquad(incidentId, employeeIds, equipmentIds, travelSeconds);
			if (result.IsSuccess) CloseDispatchScreen(incidentId);
			return result;
		}

		/// <summary>Игрок поднял рацию: таймер решения останавливается в ядре.</summary>
		public CommandResult AnswerRadio(string incidentId)
		{
			for (int i = 0; i < _director.Incidents.Count; i++)
			{
				IncidentRuntime incident = _director.Incidents[i];
				if (!string.Equals(incident.Id, incidentId, StringComparison.OrdinalIgnoreCase)) continue;
				if (incident.Phase != IncidentPhase.RadioPending || incident.MissionEvent == null)
				{
					return CommandResult.Fail("Радио по этому вызову не активно.");
				}

				FreezeTime("radio:" + incidentId);
				_bus.Publish(new RadioAnswered(incidentId, incident.MissionEvent.Id));
				return CommandResult.Ok();
			}

			return CommandResult.Fail("Вызов не найден.");
		}

		public CommandResult CloseRadio(string incidentId)
		{
			UnfreezeTime("radio:" + incidentId);
			return CommandResult.Ok();
		}

		/// <summary>
		/// Шанс успеха выбранного по рации варианта, 0..1. Null — вызова или
		/// варианта нет. Считается тем же кодом, что и настоящий бросок.
		/// </summary>
		public double? EstimateRadioOptionChance(string incidentId, string optionId)
		{
			return _director.EstimateRadioOptionChance(incidentId, optionId);
		}

		public CommandResult ChooseRadioOption(string incidentId, string optionId)
		{
			CommandResult result = _director.ChooseRadioOption(incidentId, optionId);
			if (result.IsSuccess) CloseRadio(incidentId);
			return result;
		}

		public IReadOnlyList<RadioOptionOffer> GetRadioOptions(string incidentId)
		{
			for (int i = 0; i < _director.Incidents.Count; i++)
			{
				IncidentRuntime incident = _director.Incidents[i];
				if (string.Equals(incident.Id, incidentId, StringComparison.OrdinalIgnoreCase)) return _director.BuildOffers(incident);
			}
			return Array.Empty<RadioOptionOffer>();
		}

		/// <summary>Отладка: случайный запланированный вызов начинает звонить сейчас.</summary>
		public CommandResult DebugRingRandomCall()
		{
			return _director.DebugRingRandomCall();
		}

		/// <summary>Отладка: рация поднимается по случайной миссии с событием.</summary>
		public CommandResult DebugTriggerRandomRadio()
		{
			return _director.DebugTriggerRandomRadio();
		}

		public CommandResult OpenMissionOutcome(string incidentId)
		{
			if (string.IsNullOrWhiteSpace(incidentId)) return CommandResult.Fail("Не указан вызов.");
			FreezeTime("outcome:" + incidentId);
			return CommandResult.Ok();
		}

		public CommandResult CloseMissionOutcome(string incidentId)
		{
			if (string.IsNullOrWhiteSpace(incidentId)) return CommandResult.Fail("Не указан вызов.");
			UnfreezeTime("outcome:" + incidentId);
			return CommandResult.Ok();
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

		/// <summary>Явно обновляет фиксированный список найма между сменами.</summary>
		public void RefreshHireCandidates(int day)
		{
			_rosterSystem.RefreshHireOffers(day);
		}

		/// <summary>Creates a random employee for debug tools, ignoring the staff limit.</summary>
		public CommandResult DebugAddGeneratedEmployee(out string employeeName)
		{
			employeeName = string.Empty;
			int day = System.Math.Max(1, _state.Day);
			var names = new List<string>();
			var ids = new List<string>(_state.HiredCandidateIds);
			var portraits = new List<string>();

			CollectEmployeeIdentity(_state.Roster, names, ids, portraits);
			CollectEmployeeIdentity(_state.HireOffers, names, ids, portraits);
			CollectEmployeeIdentity(_state.StartingChoice, names, ids, portraits);

			IReadOnlyList<HireCandidate> generated = _employeeFactory.Generate(day, 1, names, ids, portraits);
			if (generated.Count == 0)
			{
				return CommandResult.Fail("Employee generation is unavailable.");
			}

			Employee employee = generated[0].Template.Clone();
			employee.Status = EmployeeStatus.Available;
			employee.CurrentIncidentId = null;
			_state.Roster.Add(employee);
			_state.HiredCandidateIds.Add(employee.Id);
			_bus.Publish(new EmployeeHired(employee.Id, employee.Name, day));
			employeeName = employee.Name;
			return CommandResult.Ok();
		}

		/// <summary>Removes a random available employee for debug tools.</summary>
		public CommandResult DebugRemoveRandomAvailableEmployee(out string employeeName)
		{
			employeeName = string.Empty;
			var removable = new List<int>();
			for (int i = 0; i < _state.Roster.Count; i++)
			{
				if (_state.Roster[i].Status == EmployeeStatus.Available)
				{
					removable.Add(i);
				}
			}

			if (removable.Count == 0)
			{
				return CommandResult.Fail("No available employees can be removed.");
			}

			Employee employee = _state.Roster[removable[_random.NextInt(0, removable.Count)]];
			employeeName = employee.Name;
			_state.Roster.Remove(employee);
			return CommandResult.Ok();
		}

		private static void CollectEmployeeIdentity(
			IReadOnlyList<Employee> employees,
			List<string> names,
			List<string> ids,
			List<string> portraits)
		{
			for (int i = 0; i < employees.Count; i++)
			{
				Employee employee = employees[i];
				names.Add(employee.Name);
				ids.Add(employee.Id);
				if (employee.PortraitId.Length > 0)
				{
					portraits.Add(employee.PortraitId);
				}
			}
		}

		private static void CollectEmployeeIdentity(
			IReadOnlyList<HireCandidate> candidates,
			List<string> names,
			List<string> ids,
			List<string> portraits)
		{
			for (int i = 0; i < candidates.Count; i++)
			{
				Employee employee = candidates[i].Template;
				names.Add(employee.Name);
				ids.Add(employee.Id);
				if (employee.PortraitId.Length > 0)
				{
					portraits.Add(employee.PortraitId);
				}
			}
		}

		public bool NeedsStartingChoice
		{
			get { return !_state.StartingRosterConfirmed && _rosterSystem.GetStartingChoice().Count > 0; }
		}

		public CommandResult ConfirmStartingRoster(IReadOnlyList<string> candidateIds)
		{
			if (_director.IsShiftActive) return CommandResult.Fail("Cannot change the roster during a shift.");
			string error;
			return _rosterSystem.TryConfirmStartingRoster(candidateIds, out error)
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
				_rosterSystem.GetStaffLimit(_state.Day),
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
					employee.PortraitId,
					employee.Age,
					new List<string>(employee.BioIds)));
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
					incident.BuildingId,
					incident.Mission.MissionEventId ?? string.Empty,
					incident.Mission.Tier,
					incident.Mission.EffectiveCap,
					incident.Phase,
					incident.RemainingSeconds,
					_director.GetCurrentRequirements(incident),
					incident.Mission.SquadLimit,
					incident.SquadEmployeeIds.ToArray(),
					incident.EquipmentIds.ToArray())
				{
				});
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
					template.PortraitId,
					template.Age,
					template.BioIds.ToArray()));
			}

			return result;
		}

		public IReadOnlyList<HireCandidateView> GetStartingChoice()
		{
			var result = new List<HireCandidateView>();
			IReadOnlyList<HireCandidate> candidates = _rosterSystem.GetStartingChoice();
			for (int i = 0; i < candidates.Count; i++)
			{
				Employee employee = candidates[i].Template;
				result.Add(new HireCandidateView(employee.Id, employee.Name, employee.RankTitle, employee.Level,
					employee.BaseStats, employee.AbilityIds.ToArray(), employee.PortraitId, employee.Age, employee.BioIds.ToArray()));
			}
			return result;
		}

		public IReadOnlyList<MissionReport> GetReports()
		{
			return _state.Reports.ToArray();
		}
	}
}
