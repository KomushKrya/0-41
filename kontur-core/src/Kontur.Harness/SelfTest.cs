using System;
using System.Collections.Generic;
using Kontur.Core.Api;
using Kontur.Core.Content;
using Kontur.Core.Events;
using Kontur.Core.Model;
using Kontur.Core.Simulation;
using Kontur.Core.Systems;

namespace Kontur.Harness
{
	/// <summary>
	/// Проверки инвариантов ядра. Запуск: --selftest.
	/// Это не замена юнит-тестам, а быстрый барьер: если здесь красное,
	/// интегрировать ядро в Godot нельзя.
	/// </summary>
	public static class SelfTest
	{
		private static int _passed;
		private static int _failed;

		// Снимок детерминированного прогона: «раскрыто/всего» по существам в порядке id.
		// Меняется только вместе с балансом или контентом, но не при рефакторинге.
		private const string ExpectedRevealSignature = "1/3 1/3 1/3";
		private const int ExpectedRevealEvents = 3;

		public static bool Run(ContentDatabase content)
		{
			_passed = 0;
			_failed = 0;

			Console.WriteLine("SELFTEST ядра К.О.Н.Т.У.Р.");
			Console.WriteLine(new string('-', 78));

			TestCountdown();
			TestEventBus();
			TestCoverageMath();
			TestSuccessChanceCurve();
			TestTimings(content);
			TestMissedCallIsAutoFailure(content);
			TestExpiredMarkerIsAutoFailure(content);
			TestEquipmentSlotLimits(content);
			TestStaffLimit(content);
			TestFlags(content);
			TestEncyclopediaReveals(content);
			TestDeterminism(content);
			TestTutorialShift(content);
			TestFullShiftCompletes(content);

			Console.WriteLine(new string('-', 78));
			Console.WriteLine($"Пройдено: {_passed}, провалено: {_failed}");
			return _failed == 0;
		}

		// ------------------------------------------------------------------ проверки

		private static void TestCountdown()
		{
			Countdown countdown = Countdown.Start(1.0);
			Check("Countdown не срабатывает раньше времени", !countdown.Tick(0.5));
			Check("Countdown срабатывает при истечении", countdown.Tick(0.6));
			Check("Countdown срабатывает ровно один раз", !countdown.Tick(1.0));
			Check("Countdown останавливается", !countdown.IsRunning);
		}

		private static void TestEventBus()
		{
			var bus = new EventBus();
			var order = new List<string>();

			bus.Subscribe<ShiftEnded>(_ => order.Add("outer"));
			IDisposable token = bus.Subscribe<CallMissed>(_ => order.Add("inner"));

			bus.Subscribe<ShiftEnded>(_ => bus.Publish(new CallMissed("INC", "M")));
			bus.Publish(new ShiftEnded(1, "cut", new ShiftSummary(0, 0, 0, 0, 0, 0, 0)));

			Check("Вложенная публикация не рекурсивна", order.Count == 2 && order[0] == "outer" && order[1] == "inner");

			token.Dispose();
			order.Clear();
			bus.Publish(new CallMissed("INC", "M"));
			Check("Отписка работает", order.Count == 0);
		}

		private static void TestCoverageMath()
		{
			var requirements = new StatBlock(10, 0, 0, 0, 0);

			Check("Полное покрытие = 1", Math.Abs(MissionResolver.ComputeCoverage(requirements, new StatBlock(10, 0, 0, 0, 0)) - 1.0) < 1e-9);
			Check("Половина = 0.5", Math.Abs(MissionResolver.ComputeCoverage(requirements, new StatBlock(5, 0, 0, 0, 0)) - 0.5) < 1e-9);
			Check("Ноль = 0", Math.Abs(MissionResolver.ComputeCoverage(requirements, StatBlock.Zero)) < 1e-9);
			Check("Излишек не даёт > 1", Math.Abs(MissionResolver.ComputeCoverage(requirements, new StatBlock(50, 0, 0, 0, 0)) - 1.0) < 1e-9);
			Check(
				"Излишек по одной характеристике не компенсирует нехватку по другой",
				Math.Abs(MissionResolver.ComputeCoverage(new StatBlock(10, 10, 0, 0, 0), new StatBlock(20, 5, 0, 0, 0)) - 0.75) < 1e-9);
		}

		private static void TestSuccessChanceCurve()
		{
			ContentDatabase content = BuildMinimalContent();
			var resolver = new MissionResolver(content, content.Config, new XorShiftRandom(1));
			var empty = new List<Kontur.Core.Model.EquipmentDefinition>();

			double half = resolver.ComputeSuccessChance(0.5, empty, false);
			Check("Покрытие 0.5 -> шанс 0.25 (кривая ^2)", Math.Abs(half - 0.25) < 1e-9);

			double capped = resolver.ComputeSuccessChance(0.99, empty, false);
			Check("Шанс ограничен потолком 0.95", capped <= 0.95 + 1e-9);

			double missed = resolver.ComputeSuccessChance(0.5, empty, true);
			Check("Просроченное радио режет шанс вдвое", Math.Abs(missed - 0.125) < 1e-9);
		}

		private static void TestTimings(ContentDatabase content)
		{
			Check("Звонок 15 с", Math.Abs(content.Config.Timings.PhoneRingSeconds - 15.0) < 1e-9);
			Check("Метка 30 с", Math.Abs(content.Config.Timings.MapMarkerSeconds - 30.0) < 1e-9);
			Check("Радио 20 с", Math.Abs(content.Config.Timings.RadioSeconds - 20.0) < 1e-9);
			Check("Окно вызовов 5 минут", Math.Abs(content.Config.Timings.ShiftCallWindowSeconds - 300.0) < 1e-9);
			Check("Лимиты штата 3/4/5/6",
				content.Config.GetDay(1).StaffLimit == 3
				&& content.Config.GetDay(2).StaffLimit == 4
				&& content.Config.GetDay(3).StaffLimit == 5
				&& content.Config.GetDay(4).StaffLimit == 6);
		}

		private static void TestMissedCallIsAutoFailure(ContentDatabase content)
		{
			// День 2, а не 1: первый день — обучающий, таймеры игрока там отключены.
			var simulation = new GameSession(content, 7);

			MissionOutcome? outcome = null;
			bool missed = false;
			simulation.Events.Subscribe<CallMissed>(_ => missed = true);
			simulation.Events.Subscribe<MissionResolved>(e => outcome ??= e.Outcome);

			ScaleValues before = simulation.GetStatus().Scales;

			simulation.StartShift(2);
			RunSeconds(simulation, 60.0, 0.25);

			Check("Неотвеченный звонок помечается пропущенным", missed);
			Check("Пропуск звонка = автопровал", outcome != null && outcome.Reason == MissionResolutionReason.CallMissed);

			ScaleValues after = simulation.GetStatus().Scales;
			Check("Шкалы сразу меняются в худшую сторону",
				after.Infection > before.Infection && after.Publicity > before.Publicity && after.Loyalty < before.Loyalty);
		}

		private static void TestExpiredMarkerIsAutoFailure(ContentDatabase content)
		{
			var simulation = new GameSession(content, 11);

			string? ringingId = null;
			bool expired = false;
			MissionOutcome? outcome = null;

			simulation.Events.Subscribe<IncidentCreated>(e => ringingId ??= e.IncidentId);
			simulation.Events.Subscribe<MapMarkerExpired>(_ => expired = true);
			simulation.Events.Subscribe<MissionResolved>(e => outcome ??= e.Outcome);

			// День 2: на обучающей смене метка не истекает по определению.
			simulation.StartShift(2);

			// Отвечаем на звонок, но группу не отправляем.
			for (int i = 0; i < 400 && ringingId == null; i++)
			{
				simulation.Tick(0.25);
			}

			Check("Инцидент создан", ringingId != null);

			if (ringingId != null)
			{
				Check("Ответ на звонок принят", simulation.AnswerCall(ringingId).IsSuccess);
				Check("Кнопка ОК ставит метку", simulation.ConfirmBriefing(ringingId).IsSuccess);
			}

			RunSeconds(simulation, 35.0, 0.25);

			Check("Метка истекает через 30 с", expired);
			Check("Истечение метки = автопровал", outcome != null && outcome.Reason == MissionResolutionReason.MarkerExpired);
		}

		private static void TestEquipmentSlotLimits(ContentDatabase content)
		{
			var simulation = new GameSession(content, 13);

			string? incidentId = null;
			simulation.Events.Subscribe<MapMarkerSpawned>(e => incidentId ??= e.IncidentId);
			simulation.Events.Subscribe<IncidentCreated>(e =>
			{
				simulation.AnswerCall(e.IncidentId);
				simulation.ConfirmBriefing(e.IncidentId);
			});

			simulation.StartShift(1);
			for (int i = 0; i < 400 && incidentId == null; i++)
			{
				simulation.Tick(0.25);
			}

			if (incidentId == null)
			{
				Check("Метка появилась (проверка слотов)", false);
				return;
			}

			IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
			var squad = new List<string> { roster[0].Id };

			var threeConsumables = new List<string>();
			IReadOnlyList<EquipmentSlotView> stock = simulation.GetAvailableEquipment();
			for (int i = 0; i < stock.Count && threeConsumables.Count < 3; i++)
			{
				if (stock[i].Kind == EquipmentKind.Consumable)
				{
					for (int q = 0; q < stock[i].Quantity && threeConsumables.Count < 3; q++)
					{
						threeConsumables.Add(stock[i].Id);
					}
				}
			}

			if (threeConsumables.Count == 3)
			{
				CommandResult overflow = simulation.DispatchSquad(incidentId, squad, threeConsumables);
				Check("Больше двух расходников на группу — отказ", !overflow.IsSuccess);
			}

			CommandResult empty = simulation.DispatchSquad(incidentId, new List<string>(), new List<string>());
			Check("Пустая группа — отказ", !empty.IsSuccess);

			CommandResult ok = simulation.DispatchSquad(incidentId, squad, new List<string>());
			Check("Корректная отправка принимается", ok.IsSuccess);

			CommandResult twice = simulation.DispatchSquad(incidentId, squad, new List<string>());
			Check("Повторная отправка по тому же вызову — отказ", !twice.IsSuccess);
		}

		private static void TestStaffLimit(ContentDatabase content)
		{
			var simulation = new GameSession(content, 17);

			IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
			Check("Стартовый состав — 3 сотрудника", roster.Count == 3);

			IReadOnlyList<HireCandidateView> day1 = simulation.GetHireCandidates(1);
			Check("В первый день найма нет", day1.Count == 0);

			IReadOnlyList<HireCandidateView> day2 = simulation.GetHireCandidates(2);
			Check("Со второго дня появляются кандидаты", day2.Count > 0);

			if (day2.Count > 0)
			{
				Check("Найм в пределах лимита (3 -> 4)", simulation.HireEmployee(day2[0].Id, 2).IsSuccess);
			}

			IReadOnlyList<HireCandidateView> more = simulation.GetHireCandidates(2);
			if (more.Count > 0)
			{
				Check("Найм сверх лимита отклоняется", !simulation.HireEmployee(more[0].Id, 2).IsSuccess);
			}
		}

		private static void TestFlags(ContentDatabase content)
		{
			var simulation = new GameSession(content, 41);

			var changes = new List<FlagChanged>();
			simulation.Events.Subscribe<FlagChanged>(changes.Add);

			Check("Незнакомый флаг не установлен", !simulation.IsFlagSet("property_mimic_routine"));

			simulation.SetFlag("property_mimic_routine");
			Check("Флаг установлен", simulation.IsFlagSet("property_mimic_routine"));
			Check("Об установке пришло событие", changes.Count == 1 && changes[0].Value);

			simulation.SetFlag("property_mimic_routine");
			Check("Повторная установка события не даёт", changes.Count == 1);

			Check("Регистр имени не важен", simulation.IsFlagSet("PROPERTY_MIMIC_ROUTINE"));

			Check("Переключение снимает флаг", !simulation.ToggleFlag("property_mimic_routine"));
			Check("О снятии пришло событие", changes.Count == 2 && !changes[1].Value);

			simulation.SetFlag("flag_one");
			simulation.SetFlag("flag_two");
			Check("Флаги накапливаются", simulation.GetFlags().Count == 2);

			simulation.ResetToNewGame();
			Check("Новая партия обнуляет флаги", simulation.GetFlags().Count == 0);
		}

		private static void TestDeterminism(ContentDatabase content)
		{
			string first = RunSignature(content, 99);
			string second = RunSignature(content, 99);
			string third = RunSignature(content, 100);

			Check("Один seed — один и тот же прогон", first == second);
			Check("Разный seed — разный прогон", first != third);
		}

		/// <summary>Обучающая смена: сценарный порядок, вызовы по одному, таймеры игрока выключены.</summary>
		private static void TestTutorialShift(ContentDatabase content)
		{
			Kontur.Core.Config.DayConfig day1 = content.Config.GetDay(1);
			Check("День 1 помечен как сценарный", day1.IsScripted);
			Check("День 1 без таймеров игрока", day1.DisableTimers);

			var simulation = new GameSession(content, 5);

			var order = new List<string>();
			int maxSimultaneous = 0;
			double ringSeconds = -1.0;

			simulation.Events.Subscribe<IncidentCreated>(e =>
			{
				order.Add(e.MissionId);
				if (ringSeconds < 0.0)
				{
					ringSeconds = e.RingSeconds;
				}
			});

			simulation.StartShift(1);

			// Никаких команд: на обучающей смене без таймеров ничего не должно произойти само.
			RunSeconds(simulation, 120.0, 0.25);

			Check("Без действий игрока звонок не срывается", simulation.GetActiveIncidents().Count == 1);
			Check("Телефон звонит без обратного отсчёта", Math.Abs(ringSeconds) < 1e-9);
			Check("Первым идёт вызов из сценария", order.Count > 0 && order[0] == day1.MissionOrder[0]);

			// Теперь проходим смену автопилотом и смотрим порядок и наложение.
			var scripted = new GameSession(content, 5);
			var oper = new AutoOperator(scripted, content, RadioStrategy.Best, 5);
			var missionOrder = new List<string>();

			scripted.Events.Subscribe<IncidentCreated>(e => missionOrder.Add(e.MissionId));

			scripted.StartShift(1);

			double guard = 0.0;
			while (scripted.IsShiftActive && guard < 1800.0)
			{
				scripted.Tick(0.25);
				guard += 0.25;
				oper.Update();

				int open = scripted.GetActiveIncidents().Count;
				if (open > maxSimultaneous)
				{
					maxSimultaneous = open;
				}
			}

			Check("Сценарий пройден целиком", missionOrder.Count == day1.MissionOrder.Count);

			bool sameOrder = missionOrder.Count == day1.MissionOrder.Count;
			for (int i = 0; sameOrder && i < missionOrder.Count; i++)
			{
				sameOrder = missionOrder[i] == day1.MissionOrder[i];
			}

			Check("Порядок вызовов совпадает со сценарием", sameOrder);
			Check("Обучающая смена завершается", !scripted.IsShiftActive);
			Check("В конце смены вызовы накладываются", maxSimultaneous > 1);
		}

		/// <summary>
		/// Характеризационная проверка энциклопедии: фиксирует, сколько свойств каждого
		/// существа открывает детерминированный прогон. Считает через публичный
		/// IsPropertyRevealed и не завязана ни на номера абзацев, ни на конкретные id, —
		/// поэтому переживает переход раскрытий с индексов абзацев на id свойств.
		/// </summary>
		private static void TestEncyclopediaReveals(ContentDatabase content)
		{
			var simulation = new GameSession(content, 41);
			var oper = new AutoOperator(simulation, content, RadioStrategy.Best, 41);

			int revealEvents = 0;
			simulation.Events.Subscribe<CreatureRevealed>(_ => revealEvents++);

			simulation.StartShift(1);

			double guard = 0.0;
			while (simulation.IsShiftActive && guard < 1800.0)
			{
				simulation.Tick(0.25);
				guard += 0.25;
				oper.Update();
			}

			var signature = new System.Text.StringBuilder();
			var creatureIds = new List<string>(content.Creatures.Keys);
			creatureIds.Sort(StringComparer.Ordinal);

			for (int i = 0; i < creatureIds.Count; i++)
			{
				CreatureDefinition creature = content.FindCreature(creatureIds[i])!;
				int revealed = 0;
				for (int p = 0; p < creature.Properties.Count; p++)
				{
					if (simulation.IsPropertyRevealed(creature.Id, PropertyIdAt(creature, p)))
					{
						revealed++;
					}
				}

				signature.Append(revealed).Append('/').Append(creature.Properties.Count).Append(' ');
			}

			Console.WriteLine("       снимок раскрытий: " + signature.ToString().Trim() + $", событий {revealEvents}");
			Check("Раскрытия энциклопедии не изменились", signature.ToString().Trim() == ExpectedRevealSignature);
			Check("События раскрытия совпадают со снимком", revealEvents == ExpectedRevealEvents);
		}

		/// <summary>Прослойка на время перехода: до рефакторинга свойство — объект, после — id.</summary>
		private static string PropertyIdAt(CreatureDefinition creature, int index)
		{
			return creature.Properties[index];
		}

		private static void TestFullShiftCompletes(ContentDatabase content)
		{
			var simulation = new GameSession(content, 41);
			var oper = new AutoOperator(simulation, content, RadioStrategy.Best, 41);

			bool ended = false;
			bool gameOver = false;
			ShiftSummary? summary = null;

			simulation.Events.Subscribe<ShiftEnded>(e =>
			{
				ended = true;
				summary = e.Summary;
			});

			simulation.Events.Subscribe<GameOverTriggered>(_ => gameOver = true);

			simulation.StartShift(2);

			double guard = 0.0;
			while (simulation.IsShiftActive && !gameOver && guard < 1800.0)
			{
				simulation.Tick(0.25);
				guard += 0.25;
				oper.Update();
			}

			Check("Смена завершается сама", ended || gameOver);
			Check("Смена не длится бесконечно", guard < 1800.0);

			if (summary != null)
			{
				Check("За смену 5–10 вызовов", summary.TotalIncidents >= 5 && summary.TotalIncidents <= 10);
				Check("Все вызовы учтены",
					summary.Successes + summary.Failures == summary.TotalIncidents);
			}
		}

		// ------------------------------------------------------------------ инфраструктура

		private static string RunSignature(ContentDatabase content, int seed)
		{
			var simulation = new GameSession(content, seed);
			var oper = new AutoOperator(simulation, content, RadioStrategy.Random, seed);
			var signature = new System.Text.StringBuilder();

			simulation.Events.SubscribeAll(e =>
			{
				if (e is MissionResolved resolved)
				{
					signature.Append(resolved.Outcome.MissionId)
						.Append(':')
						.Append(resolved.Outcome.Kind)
						.Append(';');
				}
			});

			// День 2: на сценарной обучающей смене состав вызовов одинаков при любом seed,
			// и проверка «разный seed — разный прогон» потеряла бы смысл.
			simulation.StartShift(2);

			double guard = 0.0;
			while (simulation.IsShiftActive && guard < 1800.0)
			{
				simulation.Tick(0.25);
				guard += 0.25;
				oper.Update();
			}

			return signature.ToString();
		}

		private static void RunSeconds(GameSession simulation, double seconds, double delta)
		{
			double elapsed = 0.0;
			while (elapsed < seconds)
			{
				simulation.Tick(delta);
				elapsed += delta;
			}
		}

		private static ContentDatabase BuildMinimalContent()
		{
			return new ContentDatabase();
		}

		private static void Check(string name, bool condition)
		{
			if (condition)
			{
				_passed++;
				Console.WriteLine($"  [ OK ] {name}");
				return;
			}

			_failed++;
			Console.WriteLine($"  [FAIL] {name}");
		}
	}
}
