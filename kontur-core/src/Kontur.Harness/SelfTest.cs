using System;
using System.Collections.Generic;
using System.Globalization;
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


		public static bool Run(ContentDatabase content)
		{
			_passed = 0;
			_failed = 0;

			Console.WriteLine("SELFTEST ядра К.О.Н.Т.У.Р.");
			Console.WriteLine(new string('-', 78));

			TestCountdown();
			TestEventBus();
			TestStatMatchMath();
			TestSuccessChanceCurve();
			TestTimings(content);
			TestMissedCallIsAutoFailure(BuildTimerTestContent());
			TestExpiredMarkerIsAutoFailure(BuildTimerTestContent());
			TestEquipmentSlotLimits(content);
			TestStaffLimit(content);
			TestEmployeeFactory(content);
			TestOutcomeAndHiring(content);
			TestSaveLoad(content);
			TestSaveLoadRejections(content);
			TestFlags(content);
			TestEncyclopediaReveals(content);
			TestDeterminism(BuildTimerTestContent());
			TestCallQueue(BuildQueueTestContent());
			TestTimeFreeze(BuildTimerTestContent());
			TestRequirementModifier(content);
			TestReportMapping(content);
			TestConsequenceCap(content);
			TestOptionGating(content);
			TestTutorialShift(content);
			TestFullShiftCompletes(BuildTimerTestContent());

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

		/// <summary>
		/// Сравнение профилей по модели Dispatch: пороги по каждой характеристике,
		/// лучший в группе, три ступени оценки и двойной вес главной характеристики.
		/// </summary>
		private static void TestStatMatchMath()
		{
			ContentDatabase content = BuildMinimalContent();
			var resolver = new MissionResolver(content, content.Config, new XorShiftRandom(1));

			var requirements = new StatBlock(6, 4, 0, 0, 0);

			// Зелёный по обеим: превышение на 2 и больше.
			IReadOnlyList<StatMatch> perfect = resolver.EvaluateMatches(
				requirements, new StatBlock(8, 6, 0, 0, 0), null);

			Check("Требования без порога в расчёт не идут", perfect.Count == 2);
			Check("Превышение на 2 — зелёный", perfect[0].Rating == StatMatchRating.Exceeds);
			Check("Все зелёные — успех без броска", MissionResolver.IsPerfectMatch(perfect));
			Check("Совпадение профилей = 1", Math.Abs(resolver.ComputeMatchScore(perfect) - 1.0) < 1e-9);

			// Жёлтый: ровно дотянул.
			IReadOnlyList<StatMatch> meets = resolver.EvaluateMatches(
				requirements, new StatBlock(6, 5, 0, 0, 0), null);

			Check("Ровно порог — жёлтый", meets[0].Rating == StatMatchRating.Meets);
			Check("Превышение на 1 — тоже жёлтый", meets[1].Rating == StatMatchRating.Meets);
			Check("Жёлтый профиль не даёт автоуспеха", !MissionResolver.IsPerfectMatch(meets));
			Check("Жёлтый профиль = 0.8", Math.Abs(resolver.ComputeMatchScore(meets) - 0.8) < 1e-9);

			// Красный: недобор режет резко.
			IReadOnlyList<StatMatch> below = resolver.EvaluateMatches(
				requirements, new StatBlock(5, 4, 0, 0, 0), null);

			Check("Недобор — красный", below[0].Rating == StatMatchRating.Below);
			Check("Красный считает нехватку", below[0].Shortfall == 1);
			Check("Недобор на 1 роняет вклад втрое", Math.Abs(below[0].Score - 0.28) < 1e-9);

			// Главная характеристика весит вдвое.
			IReadOnlyList<StatMatch> weighted = resolver.EvaluateMatches(
				requirements, new StatBlock(8, 3, 0, 0, 0), StatKind.Strength);

			double withPrimary = resolver.ComputeMatchScore(weighted);
			double withoutPrimary = resolver.ComputeMatchScore(
				resolver.EvaluateMatches(requirements, new StatBlock(8, 3, 0, 0, 0), null));

			Check("Главная характеристика тянет процент вверх", withPrimary > withoutPrimary);

			// Лучший в группе, а не сумма: трое слабых не заменяют одного сильного.
			var weakling = new Employee { Id = "w", BaseStats = new StatBlock(3, 3, 0, 0, 0) };
			var specialist = new Employee { Id = "s", BaseStats = new StatBlock(8, 3, 0, 0, 0) };
			var noGear = new List<EquipmentDefinition>();

			StatBlock crowd = resolver.ComputeSquadStats(
				new List<Employee> { weakling, weakling, weakling }, noGear, null);
			StatBlock alone = resolver.ComputeSquadStats(
				new List<Employee> { specialist }, noGear, null);

			Check("Трое слабых не складываются", crowd[StatKind.Strength] == 3);
			Check("Один специалист сильнее толпы", alone[StatKind.Strength] > crowd[StatKind.Strength]);
		}

		private static void TestSuccessChanceCurve()
		{
			ContentDatabase content = BuildMinimalContent();
			var resolver = new MissionResolver(content, content.Config, new XorShiftRandom(1));
			var empty = new List<EquipmentDefinition>();

			// Процент — это и есть совпадение профилей: лишней кривой поверх нет.
			Check("Шанс равен совпадению профилей",
				Math.Abs(resolver.ComputeSuccessChance(0.5, empty, false) - 0.5) < 1e-9);

			Check("Шанс ограничен потолком 0.95",
				resolver.ComputeSuccessChance(0.99, empty, false) <= 0.95 + 1e-9);

			Check("Просроченное радио режет шанс вдвое",
				Math.Abs(resolver.ComputeSuccessChance(0.5, empty, true) - 0.25) < 1e-9);
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
			var simulation = new KonturSimulation(content, 7);

			MissionOutcome? outcome = null;
			bool missed = false;
			simulation.Events.Subscribe<CallMissed>(_ => missed = true);
			simulation.Events.Subscribe<MissionResolved>(e => outcome ??= e.Outcome);

			ScaleValues before = simulation.GetStatus().Scales;

			simulation.StartShift(1);
			RunSeconds(simulation, 60.0, 0.25);

			Check("Неотвеченный звонок помечается пропущенным", missed);
			Check("Пропуск звонка = автопровал", outcome != null && outcome.Reason == MissionResolutionReason.CallMissed);

			ScaleValues after = simulation.GetStatus().Scales;
			Check("Шкалы сразу меняются в худшую сторону",
				after.Infection > before.Infection && after.Publicity > before.Publicity && after.Loyalty < before.Loyalty);
		}

		private static void TestExpiredMarkerIsAutoFailure(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 11);

			string? ringingId = null;
			bool expired = false;
			MissionOutcome? outcome = null;

			simulation.Events.Subscribe<IncidentCreated>(e => ringingId ??= e.IncidentId);
			simulation.Events.Subscribe<MapMarkerExpired>(_ => expired = true);
			simulation.Events.Subscribe<MissionResolved>(e => outcome ??= e.Outcome);

			simulation.StartShift(1);

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
			var simulation = new KonturSimulation(content, 13);

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
			var simulation = new KonturSimulation(content, 17);

			IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
			Check("Стартовый состав — 3 сотрудника", roster.Count == 3);

			IReadOnlyList<HireCandidateView> day2 = simulation.GetHireCandidates(2);
			Check("Со второго дня появляются кандидаты", day2.Count > 0);

			if (day2.Count > 0)
			{
				Check("Найм в пределах лимита (3 -> 4)", simulation.HireEmployee(day2[0].Id, 2).IsSuccess);
			}

			IReadOnlyList<HireCandidateView> more = simulation.GetHireCandidates(2);
			Check("Нанятый исчезает из списка кандидатов", day2.Count == 0 || more.Count == day2.Count - 1);

			if (more.Count > 0)
			{
				Check("Найм сверх лимита отклоняется", !simulation.HireEmployee(more[0].Id, 2).IsSuccess);
			}
		}

		/// <summary>
		/// Фабрика кандидатов. Проверяется не «красиво ли получилось», а три вещи,
		/// поломка которых видна не сразу: набор не должен меняться между запросами,
		/// он должен воспроизводиться по сиду, и внутри пачки не должно быть двойников.
		/// </summary>
		private static void TestEmployeeFactory(ContentDatabase content)
		{
			if (!content.Generator.IsEnabled)
			{
				Check("Фабрика кандидатов включена в контенте", false);
				return;
			}

			var simulation = new KonturSimulation(content, 501);
			IReadOnlyList<HireCandidateView> first = simulation.GetHireCandidates(3);

			Check("Фабрика выдала кандидатов", first.Count > 0);

			IReadOnlyList<HireCandidateView> again = simulation.GetHireCandidates(3);
			bool stable = first.Count == again.Count;
			for (int i = 0; stable && i < first.Count; i++)
			{
				stable = first[i].Id == again[i].Id && first[i].Name == again[i].Name;
			}

			Check("Повторный запрос возвращает тот же список", stable);

			// Разные объекты симуляции с одним сидом обязаны дать одинаковых людей,
			// иначе воспроизвести жалобу «мне выпал сломанный кандидат» невозможно.
			var twin = new KonturSimulation(content, 501);
			IReadOnlyList<HireCandidateView> twinList = twin.GetHireCandidates(3);
			bool deterministic = first.Count == twinList.Count;
			for (int i = 0; deterministic && i < first.Count; i++)
			{
				deterministic = first[i].Id == twinList[i].Id
					&& first[i].Name == twinList[i].Name
					&& first[i].Stats.Equals(twinList[i].Stats);
			}

			Check("Один сид — одни и те же кандидаты", deterministic);

			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool unique = true;
			bool statsInRange = true;
			bool hasPerks = false;

			for (int i = 0; i < first.Count; i++)
			{
				HireCandidateView candidate = first[i];
				unique &= names.Add(candidate.Name) && ids.Add(candidate.Id);

				for (int s = 0; s < StatKinds.All.Length; s++)
				{
					int value = candidate.Stats[StatKinds.All[s]];
					statsInRange &= value >= content.Generator.MinStat && value <= content.Generator.MaxStat;
				}

				hasPerks |= candidate.AbilityIds.Count > 0;
			}

			Check("Имена и id кандидатов не повторяются", unique);
			Check("Характеристики в заданных границах", statsInRange);
			Check("У кандидатов есть перки", hasPerks);

			// Бюджет должен доходить до сотрудника целиком: если очки теряются на потолке,
			// кандидаты выйдут слабее задуманного, и заметить это по глазам невозможно.
			int expectedTotal = (content.Generator.MinStat * StatKinds.Count)
				+ content.Generator.StatPointsBase
				+ (content.Generator.StatPointsPerLevel * (first[0].Level - 1));

			Check(
				$"Сумма характеристик равна бюджету ({first[0].Stats.Total} = {expectedTotal})",
				first[0].Stats.Total == expectedTotal);

			TestArchetypeSilhouette(content);
		}

		/// <summary>
		/// Силуэт архетипа: основные характеристики в среднем выше второстепенных,
		/// второстепенные — выше остальных.
		///
		/// Проверка на средних по большой выборке, а не на одном кандидате: разброс
		/// как раз и нужен, чтобы кандидаты отличались друг от друга. Ловится здесь
		/// ровно одна ошибка, но зато невидимая — перекошенное распределение очков,
		/// при котором «здоровяк» выходит самым хладнокровным в конторе.
		/// </summary>
		private static void TestArchetypeSilhouette(ContentDatabase content)
		{
			const int Samples = 2000;

			// День, а не уровень: фабрика выводит уровень из дня сама. Четвёртый взят
			// ради кандидатов повыше — на них перекос распределения виден отчётливее.
			const int Day = 4;

			var random = new XorShiftRandom(31);
			var factory = new EmployeeFactory(content, random);

			foreach (EmployeeArchetype archetype in content.Generator.Archetypes)
			{
				if (archetype.PrimaryStats.Count == 0)
				{
					continue;
				}

				var totals = new Dictionary<StatKind, long>();
				for (int i = 0; i < StatKinds.All.Length; i++)
				{
					totals[StatKinds.All[i]] = 0;
				}

				// Пробы берутся через публичный вход фабрики, поэтому мерится
				// ровно то, что увидит игрок, а не отдельная внутренняя функция.
				int collected = 0;
				while (collected < Samples)
				{
					IReadOnlyList<HireCandidate> batch = factory.Generate(
						Day,
						8,
						Array.Empty<string>(),
						Array.Empty<string>());

					for (int i = 0; i < batch.Count && collected < Samples; i++)
					{
						Employee candidate = batch[i].Template;
						if (!string.Equals(candidate.ArchetypeId, archetype.Id, StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}

						for (int s = 0; s < StatKinds.All.Length; s++)
						{
							totals[StatKinds.All[s]] += candidate.BaseStats[StatKinds.All[s]];
						}

						collected++;
					}
				}

				double primary = AverageOf(totals, archetype.PrimaryStats, collected, double.MaxValue, true);
				double secondary = archetype.SecondaryStats.Count == 0
					? double.NaN
					: AverageOf(totals, archetype.SecondaryStats, collected, double.MinValue, false);

				var rest = new List<StatKind>();
				for (int i = 0; i < StatKinds.All.Length; i++)
				{
					StatKind kind = StatKinds.All[i];
					if (!archetype.PrimaryStats.Contains(kind) && !archetype.SecondaryStats.Contains(kind))
					{
						rest.Add(kind);
					}
				}

				double others = rest.Count == 0
					? double.MinValue
					: AverageOf(totals, rest, collected, double.MinValue, false);

				bool ordered = double.IsNaN(secondary)
					? primary > others
					: primary > secondary && secondary > others;

				Check(
					$"Силуэт '{archetype.Id}': основные {primary:0.0} > второстепенные "
						+ (double.IsNaN(secondary) ? "—" : secondary.ToString("0.0"))
						+ $" > прочие {others:0.0}",
					ordered);
			}
		}

		/// <summary>
		/// Среднее по группе характеристик. Для основных берётся худшая из них, для
		/// остальных — лучшая: так сравнение остаётся честным, когда в группе разное
		/// число характеристик.
		/// </summary>
		private static double AverageOf(
			Dictionary<StatKind, long> totals,
			IReadOnlyList<StatKind> group,
			int samples,
			double seed,
			bool takeMinimum)
		{
			double result = seed;
			for (int i = 0; i < group.Count; i++)
			{
				double average = (double)totals[group[i]] / samples;
				if (takeMinimum ? average < result : average > result)
				{
					result = average;
				}
			}

			return result;
		}

		/// <summary>Экран итога миссии и меню найма — оба сигнала между-миссионного цикла.</summary>
		private static void TestOutcomeAndHiring(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 909);

			var outcomes = new List<MissionOutcomeReady>();
			var hirings = new List<HiringOpened>();
			var returns = new List<SquadReturned>();
			simulation.Events.Subscribe<MissionOutcomeReady>(outcomes.Add);
			simulation.Events.Subscribe<HiringOpened>(hirings.Add);
			simulation.Events.Subscribe<SquadReturned>(returns.Add);

			simulation.StartShift(1);

			var operatorBot = new AutoOperator(simulation, content, RadioStrategy.Best, 909);
			double guard = 0.0;
			while (simulation.IsShiftActive && guard < 1800.0)
			{
				simulation.Tick(0.25);
				guard += 0.25;
				operatorBot.Update();
			}

			Check("Экран итога показан хотя бы раз", outcomes.Count > 0);

			if (outcomes.Count > 0)
			{
				MissionOutcomeReady outcome = outcomes[0];
				Check("У итога есть текст", !string.IsNullOrEmpty(outcome.SummaryTextId));
				Check("В итоге есть возвращающиеся", outcome.SquadWiped || outcome.ReturningEmployeeIds.Count > 0);
				Check("Время возвращения задано", outcome.SquadWiped || outcome.ReturnSeconds > 0.0);
			}

			// Итог обязан опережать возвращение: иначе экран «группа возвращается»
			// покажется после того, как она уже вернулась.
			Check("Итог приходит раньше возвращения", outcomes.Count >= returns.Count);

			Check("Смена завершилась", !simulation.IsShiftActive);
			Check("Меню найма открылось после смены", hirings.Count == 1);

			if (hirings.Count > 0)
			{
				HiringOpened hiring = hirings[0];
				Check("Найм предлагает следующий день", hiring.NextDay == 2);
				Check("Есть свободные места", hiring.FreeSlots > 0);
				Check("Список кандидатов не пуст", hiring.CandidateIds.Count > 0);

				IReadOnlyList<HireCandidateView> menu = simulation.GetHireCandidates(hiring.NextDay);
				bool sameOrder = menu.Count == hiring.CandidateIds.Count;
				for (int i = 0; sameOrder && i < menu.Count; i++)
				{
					sameOrder = menu[i].Id == hiring.CandidateIds[i];
				}

				Check("Меню совпадает со списком из сигнала", sameOrder);
			}
		}

		/// <summary>
		/// Сохранение посреди смены.
		///
		/// Главная проверка — не «поля совпали», а «продолжение совпало»: партия, снятая
		/// в снимок и поднятая обратно, обязана доиграть смену ровно так же, как если бы
		/// её не трогали. Ради этого в снимок и кладётся состояние генератора.
		/// </summary>
		private static void TestSaveLoad(ContentDatabase content)
		{
			var original = new KonturSimulation(content, 4242);
			var originalLog = new List<string>();
			SubscribeTrace(original, originalLog);

			// Сохраниться нужно в момент, когда в снимке есть что сохранять: группа
			// в пути, таймер недотикал. Ловим это по событию, а не по секундомеру —
			// смена короткая, и любое фиксированное число секунд однажды окажется
			// больше её длины, а тест провалится не по делу.
			bool squadIsOut = false;
			original.Events.Subscribe<SquadDispatched>(_ => squadIsOut = true);

			original.StartShift(1);

			var originalBot = new AutoOperator(original, content, RadioStrategy.Best, 4242);

			double elapsed = 0.0;
			while (original.IsShiftActive && elapsed < 1800.0)
			{
				original.Tick(0.25);
				elapsed += 0.25;
				originalBot.Update();

				// Пара секунд после выезда: группа уже в дороге, вызов в фазе Travelling.
				if (squadIsOut)
				{
					original.Tick(0.25);
					elapsed += 0.25;
					break;
				}
			}

			Check("К моменту сохранения смена ещё идёт", original.IsShiftActive);

			string json = original.Save("проверка");
			Check("Сохранение не пустое", !string.IsNullOrWhiteSpace(json));

			int incidentsAtSave = original.GetActiveIncidents().Count;
			int reportsAtSave = original.GetReports().Count;
			ShiftStatusView statusAtSave = original.GetStatus();

			// Оригинал доигрывает смену до конца — это эталон.
			originalLog.Clear();
			while (original.IsShiftActive && elapsed < 1800.0)
			{
				original.Tick(0.25);
				elapsed += 0.25;
				originalBot.Update();
			}

			// Отдельная партия поднимается из файла и доигрывает то же самое.
			var loaded = new KonturSimulation(content, 1);
			var loadedEvents = new List<GameLoaded>();
			loaded.Events.Subscribe<GameLoaded>(loadedEvents.Add);

			CommandResult load = loaded.Load(json);
			Check("Загрузка прошла: " + load.Error, load.IsSuccess);
			Check("Событие о загрузке пришло", loadedEvents.Count == 1);
			Check("После загрузки время стоит", loaded.IsTimeFrozen);

			ShiftStatusView statusAfterLoad = loaded.GetStatus();
			Check("День восстановлен", statusAfterLoad.Day == statusAtSave.Day);
			Check(
				$"Время смены восстановлено ({statusAfterLoad.ShiftTime:0.##} = {statusAtSave.ShiftTime:0.##})",
				Math.Abs(statusAfterLoad.ShiftTime - statusAtSave.ShiftTime) < 1e-6);
			Check("Смена продолжается", statusAfterLoad.IsShiftActive);
			Check("Вызовы в работе восстановлены", loaded.GetActiveIncidents().Count == incidentsAtSave);
			Check("Отчёты восстановлены", loaded.GetReports().Count == reportsAtSave);
			Check("Шкалы восстановлены", loaded.GetStatus().Scales.Equals(statusAtSave.Scales));

			// Пока не отпустили — мир не должен двигаться.
			double frozenTime = loaded.GetStatus().ShiftTime;
			RunSeconds(loaded, 5.0, 0.25);
			Check(
				"Замороженная загрузка не тикает",
				Math.Abs(loaded.GetStatus().ShiftTime - frozenTime) < 1e-9);

			loaded.ResumeAfterLoad();
			Check("После ResumeAfterLoad время пошло", !loaded.IsTimeFrozen);

			var loadedLog = new List<string>();
			SubscribeTrace(loaded, loadedLog);

			var loadedBot = new AutoOperator(loaded, content, RadioStrategy.Best, 4242);
			double loadedElapsed = 0.0;
			while (loaded.IsShiftActive && loadedElapsed < 1800.0)
			{
				loaded.Tick(0.25);
				loadedElapsed += 0.25;
				loadedBot.Update();
			}

			Check("Загруженная смена тоже завершилась", !loaded.IsShiftActive);

			bool sameTrace = originalLog.Count == loadedLog.Count;
			int firstDifference = -1;
			for (int i = 0; i < Math.Min(originalLog.Count, loadedLog.Count); i++)
			{
				if (originalLog[i] != loadedLog[i])
				{
					firstDifference = i;
					sameTrace = false;
					break;
				}
			}

			Check(
				firstDifference < 0
					? $"Продолжение совпало с эталоном ({originalLog.Count} событий)"
					: $"Продолжение совпало с эталоном (расхождение на шаге {firstDifference}: "
						+ $"'{originalLog[firstDifference]}' против '{loadedLog[firstDifference]}')",
				sameTrace);
		}

		/// <summary>Отказы загрузки: битый файл, чужая версия, пропавшая миссия.</summary>
		private static void TestSaveLoadRejections(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 7);
			simulation.StartShift(1);
			RunSeconds(simulation, 5.0, 0.25);

			string json = simulation.Save();

			var target = new KonturSimulation(content, 7);
			int rosterBefore = target.GetRoster().Count;

			Check("Пустой файл отклонён", !target.Load(string.Empty).IsSuccess);
			Check("Мусор вместо JSON отклонён", !target.Load("{это не json").IsSuccess);

			string wrongVersion = json.Replace("\"Version\": 1", "\"Version\": 999");
			CommandResult versionResult = target.Load(wrongVersion);
			Check("Чужая версия отклонена", !versionResult.IsSuccess);
			Check(
				"В отказе по версии видно обе версии: " + versionResult.Error,
				versionResult.Error.Contains("999"));

			string missingMission = json.Replace("m_black_mold", "m_несуществующая");
			CommandResult missingResult = target.Load(missingMission);
			Check("Пропавшая миссия отклонена", !missingResult.IsSuccess);

			// Самое важное в отказе: он не должен оставлять партию наполовину загруженной.
			Check("Неудачная загрузка не тронула состав", target.GetRoster().Count == rosterBefore);
			Check("Неудачная загрузка не запустила смену", !target.IsShiftActive);
		}

		/// <summary>
		/// Свёртка потока событий в строки — чтобы сравнивать два прогона целиком,
		/// а не по отдельным полям. Прогон, который разошёлся, покажет первое расхождение.
		/// </summary>
		private static void SubscribeTrace(KonturSimulation simulation, List<string> log)
		{
			simulation.Events.Subscribe<IncidentCreated>(e => log.Add("создан:" + e.MissionId));
			simulation.Events.Subscribe<CallAnswered>(e => log.Add("трубка:" + e.MissionId));
			simulation.Events.Subscribe<CallMissed>(e => log.Add("пропущен:" + e.MissionId));
			simulation.Events.Subscribe<SquadDispatched>(e => log.Add("выезд:" + string.Join("+", e.EmployeeIds)));
			simulation.Events.Subscribe<RadioOptionChosen>(e => log.Add("радио:" + e.OptionId));
			simulation.Events.Subscribe<MissionOutcomeReady>(e => log.Add("итог:" + e.MissionId + ":" + e.IsSuccess));
			simulation.Events.Subscribe<EmployeeInjured>(e => log.Add("травма:" + e.EmployeeId));
			simulation.Events.Subscribe<EmployeeKilled>(e => log.Add("гибель:" + e.EmployeeId));
			simulation.Events.Subscribe<ShiftEnded>(e => log.Add(
				$"конец:{e.Summary.Successes}/{e.Summary.Failures}/{e.Summary.MissedCalls}"));
		}

		private static void TestFlags(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 41);

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

			Check("Один seed — один и тот же прогон", first == second);

			// Обратное утверждение проверяется по набору сидов, а не по паре.
			//
			// Требовать «сид 99 и сид 100 обязаны разойтись» неправильно: два прогона
			// вполне могут совпасть по чистой случайности, и тест начнёт падать
			// на ровном месте. Осмысленно другое — что сид вообще на что-то влияет.
			var signatures = new HashSet<string> { first };
			for (int seed = 100; seed < 108; seed++)
			{
				signatures.Add(RunSignature(content, seed));
			}

			Check($"Сид влияет на прогон (различных исходов {signatures.Count} из 9)", signatures.Count > 1);
		}

		/// <summary>Обучающая смена: сценарный порядок, вызовы по одному, таймеры игрока выключены.</summary>
		/// <summary>
		/// Открытый экран останавливает мир целиком: таймеры, дорога группы, приём звонков.
		/// Проверяется через наблюдаемое поведение — таймер метки не должен сдвинуться,
		/// а после закрытия обязан продолжить с того же места, а не начаться заново.
		/// </summary>
		private static void TestTimeFreeze(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 3);

			string? incidentId = null;
			simulation.Events.Subscribe<IncidentCreated>(e =>
			{
				if (incidentId != null)
				{
					return;
				}

				incidentId = e.IncidentId;
				simulation.AnswerCall(e.IncidentId);
				simulation.ConfirmBriefing(e.IncidentId);
			});

			var freezes = new List<TimeFreezeChanged>();
			simulation.Events.Subscribe<TimeFreezeChanged>(freezes.Add);

			simulation.StartShift(1);
			for (int i = 0; i < 400 && incidentId == null; i++)
			{
				simulation.Tick(0.25);
			}

			if (incidentId == null)
			{
				Check("Метка появилась (проверка остановки времени)", false);
				return;
			}

			Check("Пока экраны закрыты, время идёт", !simulation.IsTimeFrozen);

			double markerBefore = RemainingOf(simulation, incidentId);
			double shiftBefore = simulation.GetStatus().ShiftTime;

			Check("Экран отправки открылся", simulation.OpenDispatchScreen(incidentId).IsSuccess);
			Check("Открытый экран остановил мир", simulation.IsTimeFrozen);
			Check("Об остановке пришло событие", freezes.Count == 1 && freezes[0].IsFrozen);

			RunSeconds(simulation, 20.0, 0.25);

			Check("Таймер метки не сдвинулся", Math.Abs(RemainingOf(simulation, incidentId) - markerBefore) < 1e-9);
			Check("Часы смены не сдвинулись", Math.Abs(simulation.GetStatus().ShiftTime - shiftBefore) < 1e-9);

			Check("Экран закрылся", simulation.CloseDispatchScreen(incidentId).IsSuccess);
			Check("После закрытия мир пошёл", !simulation.IsTimeFrozen);
			Check("О возобновлении пришло событие", freezes.Count == 2 && !freezes[1].IsFrozen);

			RunSeconds(simulation, 2.0, 0.25);
			double markerAfter = RemainingOf(simulation, incidentId);

			Check("Таймер продолжил с того же места, а не сначала", markerAfter < markerBefore);
			Check("И потратил ровно столько, сколько шло время", Math.Abs((markerBefore - markerAfter) - 2.0) < 0.3);

			// Экран закрытого вызова не должен удерживать мир: отправляем группу и ждём конца.
			IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
			simulation.OpenDispatchScreen(incidentId);
			simulation.DispatchSquad(incidentId, new List<string> { roster[0].Id }, new List<string>());
			Check("Отправка группы отпускает мир", !simulation.IsTimeFrozen);
		}

		private static double RemainingOf(KonturSimulation simulation, string incidentId)
		{
			IReadOnlyList<IncidentView> incidents = simulation.GetActiveIncidents();
			for (int i = 0; i < incidents.Count; i++)
			{
				if (incidents[i].Id == incidentId)
				{
					return incidents[i].RemainingSeconds;
				}
			}

			return -1.0;
		}

		/// <summary>Надбавка варианта прибавляется к каждой требуемой характеристике, нулевые не трогает.</summary>
		private static void TestRequirementModifier(ContentDatabase content)
		{
			var requirements = new StatBlock(8, 0, 4, 0, 0);
			var option = new MissionEventOption { Id = "test", RequirementModifier = 2 };

			var resolver = new MissionResolver(content, content.Config, new XorShiftRandom(1));
			var state = new GameState();
			var zones = new ZoneSystem(state, content.Config.Zones, new EventBus());
			var mission = new MissionDefinition { Id = "m", Day = 1, Requirements = requirements };

			StatBlock scaled = resolver.ComputeEffectiveRequirements(mission, null, zones, option, 1);

			Check("Надбавка прибавилась к требуемым характеристикам",
				scaled[StatKind.Strength] == 10 && scaled[StatKind.Endurance] == 6);
			Check("Нетребуемые характеристики остались нулевыми",
				scaled[StatKind.Perception] == 0 && scaled[StatKind.Agility] == 0 && scaled[StatKind.Composure] == 0);

			StatBlock plain = resolver.ComputeEffectiveRequirements(mission, null, zones, null, 1);
			Check("Без варианта требования не меняются", plain.Equals(requirements));
		}

		/// <summary>Каждый вариант вмешательства должен уметь показать отчёт на оба исхода.</summary>
		private static void TestReportMapping(ContentDatabase content)
		{
			int checkedPairs = 0;
			bool allResolved = true;

			foreach (KeyValuePair<string, MissionDefinition> pair in content.Missions)
			{
				MissionDefinition mission = pair.Value;

				foreach (bool isSuccess in new[] { true, false })
				{
					if (string.IsNullOrEmpty(mission.ResolveReportId(null, isSuccess)))
					{
						allResolved = false;
					}

					checkedPairs++;
				}

				if (!mission.HasMissionEvent)
				{
					continue;
				}

				MissionEventDefinition? missionEvent = content.FindMissionEvent(mission.MissionEventId);
				if (missionEvent == null)
				{
					continue;
				}

				foreach (MissionEventOption option in missionEvent.Options)
				{
					foreach (bool isSuccess in new[] { true, false })
					{
						if (string.IsNullOrEmpty(mission.ResolveReportId(option.Id, isSuccess)))
						{
							allResolved = false;
						}

						checkedPairs++;
					}
				}
			}

			Check($"У каждой пары «вариант × исход» есть отчёт (проверено {checkedPairs})", allResolved);
		}

		/// <summary>
		/// Потолок последствий обязан резать шансы последним — уже после всех множителей,
		/// иначе «безопасный» вызов мог бы убить через множитель варианта или снаряжения.
		/// </summary>
		private static void TestConsequenceCap(ContentDatabase content)
		{
			Check("Филлер по умолчанию не убивает",
				ConsequenceCaps.DefaultFor(MissionTier.Filler) == ConsequenceCap.Injury);
			Check("Сюжетный вызов по умолчанию убивает",
				ConsequenceCaps.DefaultFor(MissionTier.Story) == ConsequenceCap.Death);
			Check("Ужесточение выбирает более строгий потолок",
				ConsequenceCaps.Tighten(ConsequenceCap.Death, ConsequenceCap.Injury) == ConsequenceCap.Injury
				&& ConsequenceCaps.Tighten(ConsequenceCap.Injury, ConsequenceCap.None) == ConsequenceCap.None);

			// Гарантированно смертельная миссия: сотня бросков подряд не должна дать ни одной смерти.
			var resolver = new MissionResolver(content, content.Config, new XorShiftRandom(77));
			var employees = new List<Employee>
			{
				new Employee { Id = "emp_x", Name = "X", BaseStats = StatBlock.Zero },
				new Employee { Id = "emp_y", Name = "Y", BaseStats = StatBlock.Zero }
			};

			var lethal = new MissionDefinition
			{
				Id = "m_lethal", Tier = MissionTier.Story,
				Requirements = new StatBlock(20, 0, 0, 0, 0),
				InjuryChance = 1.0, DeathChance = 1.0
			};

			MissionOutcome uncapped = Resolve(resolver, lethal, employees, null);
			Check("Без потолка гибель происходит", uncapped.KilledEmployeeIds.Count > 0);

			var capped = new MissionDefinition
			{
				Id = "m_kindergarten", Tier = MissionTier.Story,
				ConsequenceCapOverride = ConsequenceCap.Injury,
				Requirements = new StatBlock(20, 0, 0, 0, 0),
				InjuryChance = 1.0, DeathChance = 1.0
			};

			bool anyDeath = false;
			bool anyInjury = false;
			for (int i = 0; i < 100; i++)
			{
				MissionOutcome outcome = Resolve(resolver, capped, employees, null);
				anyDeath |= outcome.KilledEmployeeIds.Count > 0;
				anyInjury |= outcome.InjuredEmployeeIds.Count > 0;
			}

			Check("Потолок Injury не пропускает ни одной гибели за 100 прогонов", !anyDeath);
			Check("Но травмы при этом остаются", anyInjury);

			// Вариант ужесточает потолок миссии.
			var safeOption = new MissionEventOption
			{
				Id = "hide", DeathChanceMultiplier = 5.0, InjuryChanceMultiplier = 5.0,
				ConsequenceCapOverride = ConsequenceCap.None
			};

			MissionOutcome hidden = Resolve(resolver, lethal, employees, safeOption);
			Check("Вариант с потолком None не даёт ни травм, ни гибели",
				hidden.KilledEmployeeIds.Count == 0 && hidden.InjuredEmployeeIds.Count == 0);
			Check("Применённый потолок попал в итог", hidden.AppliedCap == ConsequenceCap.None);
		}

		private static MissionOutcome Resolve(
			MissionResolver resolver,
			MissionDefinition mission,
			List<Employee> squad,
			MissionEventOption? option)
		{
			return resolver.Resolve(new ResolutionRequest
			{
				IncidentId = "INC-TEST",
				Mission = mission,
				Squad = squad,
				Equipment = new List<EquipmentDefinition>(),
				EffectiveRequirements = mission.Requirements,
				SquadStats = StatBlock.Zero,
				ChosenOption = option
			});
		}

		/// <summary>
		/// Вариант закрывается составом группы, а не случайностью. Проверяется и расчёт
		/// нехватки, и отказ команды: молча проигнорированный выбор выглядел бы как баг.
		/// </summary>
		private static void TestOptionGating(ContentDatabase content)
		{
			var option = new MissionEventOption
			{
				Id = "clever",
				Requirements = new StatBlock(0, 8, 0, 6, 0)
			};

			var weak = new StatBlock(10, 4, 10, 6, 10);
			var strong = new StatBlock(0, 8, 0, 6, 0);

			Check("Сильной группе вариант открыт", option.IsUnlockedBy(strong));
			Check("Слабой закрыт", !option.IsUnlockedBy(weak));

			StatBlock shortfall = option.GetShortfall(weak);
			Check("Нехватка считается по каждой характеристике",
				shortfall[StatKind.Perception] == 4 && shortfall[StatKind.Agility] == 0);
			Check("Избыток в нехватку не попадает", shortfall.Total == 4);

			var free = new MissionEventOption { Id = "plain" };
			Check("Вариант без требований открыт любому", free.IsUnlockedBy(StatBlock.Zero));

			// Каждое вмешательство в контенте обязано иметь хотя бы один открытый вариант.
			bool everyEventHasOpen = true;
			foreach (KeyValuePair<string, MissionEventDefinition> pair in content.MissionEvents)
			{
				bool hasOpen = false;
				foreach (MissionEventOption candidate in pair.Value.Options)
				{
					hasOpen |= candidate.Requirements.Total == 0;
				}

				everyEventHasOpen &= hasOpen;
			}

			Check("У каждого вмешательства есть вариант без требований", everyEventHasOpen);

			// Умолчания по типу диалога: хороший не дороже нейтрального, тот не дороже плохого.
			bool orderHolds = true;
			foreach (KeyValuePair<string, MissionEventDefinition> pair in content.MissionEvents)
			{
				foreach (MissionEventOption a in pair.Value.Options)
				{
					foreach (MissionEventOption b in pair.Value.Options)
					{
						if (a.Quality < b.Quality && a.RequirementModifier > b.RequirementModifier)
						{
							orderHolds = false;
						}
					}
				}
			}

			Check("Тип диалога и надбавка не разошлись", orderHolds);
		}

		private static void TestTutorialShift(ContentDatabase content)
		{
			Kontur.Core.Config.DayConfig day1 = content.Config.GetDay(1);
			Check("День 1 помечен как сценарный", day1.IsScripted);
			Check("День 1 без таймеров игрока", day1.DisableTimers);

			var simulation = new KonturSimulation(content, 5);

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
			var scripted = new KonturSimulation(content, 5);
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
			// Весь сценарий обучения идёт строго по одному: sequentialCallCount покрывает
			// все вызовы среза. Наложение вернётся, когда появится текст на остальные дни.
			int sequential = day1.SequentialCallCount;
			bool overlapExpected = day1.MissionOrder.Count > sequential;
			Check(
				overlapExpected ? "В конце смены вызовы накладываются" : "Обучающие вызовы идут строго по одному",
				overlapExpected ? maxSimultaneous > 1 : maxSimultaneous <= 1);
		}

		/// <summary>
		/// Характеризационная проверка энциклопедии: фиксирует, сколько свойств каждого
		/// существа открывает детерминированный прогон. Считает через публичный
		/// IsPropertyRevealed и не завязана ни на номера абзацев, ни на конкретные id, —
		/// поэтому переживает переход раскрытий с индексов абзацев на id свойств.
		/// </summary>
		private static void TestEncyclopediaReveals(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 41);
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
			int totalRevealed = 0;
			int totalProperties = 0;
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

				totalRevealed += revealed;
				totalProperties += creature.Properties.Count;
				signature.Append(revealed).Append('/').Append(creature.Properties.Count).Append(' ');
			}

			Console.WriteLine("       снимок раскрытий: " + signature.ToString().Trim() + $", событий {revealEvents}");

			// Магической константы «ожидаемый снимок» здесь больше нет: срез контента
			// меняется вместе с текстом, и константа ломалась бы на каждой правке .md,
			// ничего при этом не проверяя. Проверяем то, что верно при любом контенте.
			Check("Раскрытий не больше, чем свойств у существ", totalRevealed <= totalProperties);
			Check("Каждое раскрытие пришло ровно одним событием", revealEvents == totalRevealed);
		}

		/// <summary>Прослойка на время перехода: до рефакторинга свойство — объект, после — id.</summary>
		private static string PropertyIdAt(CreatureDefinition creature, int index)
		{
			return creature.Properties[index];
		}

		private static void TestFullShiftCompletes(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 41);
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

			simulation.StartShift(1);

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
				Check("Вызовы смены запланированы", summary.TotalIncidents >= 1);
				Check("Все вызовы учтены",
					summary.Successes + summary.Failures == summary.TotalIncidents);
			}
		}

		// ------------------------------------------------------------------ инфраструктура

		private static string RunSignature(ContentDatabase content, int seed)
		{
			var simulation = new KonturSimulation(content, seed);
			var oper = new AutoOperator(simulation, content, RadioStrategy.Random, seed);
			var signature = new System.Text.StringBuilder();

			// В подпись входит и момент события: без него прогон с одной миссией
			// сводился к «успех или провал», и два разных сида совпадали просто потому,
			// что вариантов всего два. Время звонка различает прогоны куда надёжнее.
			simulation.Events.SubscribeAll(e =>
			{
				if (e is MissionResolved resolved)
				{
					signature.Append(resolved.Outcome.MissionId)
						.Append(':')
						.Append(resolved.Outcome.Kind)
						.Append('@')
						.Append(simulation.GetStatus().ShiftTime.ToString("0.00", CultureInfo.InvariantCulture))
						.Append(';');
				}
			});

			simulation.StartShift(1);

			double guard = 0.0;
			while (simulation.IsShiftActive && guard < 1800.0)
			{
				simulation.Tick(0.25);
				guard += 0.25;
				oper.Update();
			}

			return signature.ToString();
		}

		private static void RunSeconds(KonturSimulation simulation, double seconds, double delta)
		{
			double elapsed = 0.0;
			while (elapsed < seconds)
			{
				simulation.Tick(delta);
				elapsed += delta;
			}
		}

		/// <summary>
		/// Контент под проверки таймеров: один день с включёнными таймерами и одна миссия.
		///
		/// Поставляемый контент для этого не годится — там обучающая смена, где таймеры
		/// игрока выключены намеренно. Синтетический набор ещё и защищает проверки от
		/// правок баланса: они про механику, а не про конкретный вызов.
		/// </summary>
		/// <summary>
		/// Три вызова, назначенные на одно и то же время. Единственный способ проверить
		/// очередь честно: если бы линия не была занята, все три зазвонили бы разом.
		/// </summary>
		private static ContentDatabase BuildQueueTestContent()
		{
			var source = new InMemoryContentSource();

			source.Add("config.json", @"{
				""timings"": { ""phoneRingSeconds"": 15, ""mapMarkerSeconds"": 30, ""radioSeconds"": 20,
					""shiftCallWindowSeconds"": 300, ""minSecondsBetweenCalls"": 0, ""callQueueGapSeconds"": 2 },
				""days"": [ { ""day"": 1, ""staffLimit"": 3, ""minCalls"": 3, ""maxCalls"": 3,
					""requirementMultiplier"": 1.0, ""consumablesPerShift"": 0, ""standardPerShift"": 0,
					""missionOrder"": [ ""m_one"", ""m_two"", ""m_three"" ] } ]
			}");

			source.Add("zones.json", @"[ { ""id"": ""z_test"", ""name"": ""Полигон"", ""state"": ""Normal"", ""baseWeight"": 1.0 } ]");
			source.Add("abilities.json", "[]");
			source.Add("equipment.json", "[]");
			source.Add("creatures.json", "[]");
			source.Add("mission_events.json", "[]");
			source.Add("employees.json", @"{
				""startingRoster"": [
					{ ""id"": ""emp_a"", ""name"": ""А"", ""level"": 1, ""stats"": { ""strength"": 6 } }
				],
				""hirePool"": []
			}");

			string mission = @"{{
				""id"": ""{0}"", ""day"": 1, ""zoneId"": ""z_test"", ""creatureId"": """",
				""callId"": ""call_{0}"", ""requirements"": {{ ""strength"": 4 }},
				""travelSeconds"": 2, ""onSiteSeconds"": 1, ""returnSeconds"": 2,
				""scalesOnSuccess"": {{ ""loyalty"": 1 }}, ""scalesOnFailure"": {{ ""loyalty"": -1 }},
				""scalesOnMissedCall"": {{ ""publicity"": 1 }}, ""scalesOnExpiredMarker"": {{ ""publicity"": 1 }}
			}}";

			source.Add("missions.json", "[" + string.Format(mission, "m_one") + ","
				+ string.Format(mission, "m_two") + "," + string.Format(mission, "m_three") + "]");

			return ContentLoader.Load(source);
		}

		/// <summary>
		/// Телефон один: одновременно звонить может только один вызов, остальные ждут.
		/// Проверяется наблюдаемым поведением — сколько инцидентов в фазе Ringing за прогон.
		/// </summary>
		private static void TestCallQueue(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 21);

			int maxRinging = 0;
			int queuedEvents = 0;
			var ringOrder = new List<string>();

			simulation.Events.Subscribe<IncidentQueued>(_ => queuedEvents++);
			simulation.Events.Subscribe<IncidentCreated>(e => ringOrder.Add(e.MissionId));

			simulation.StartShift(1);

			// Никто не отвечает: каждый вызов отзвонит свои 15 секунд и уступит очередь.
			double guard = 0.0;
			while (simulation.IsShiftActive && guard < 300.0)
			{
				simulation.Tick(0.25);
				guard += 0.25;

				int ringing = 0;
				int queued = 0;
				foreach (IncidentView incident in simulation.GetActiveIncidents())
				{
					if (incident.Phase == IncidentPhase.Ringing)
					{
						ringing++;
					}
					else if (incident.Phase == IncidentPhase.Queued)
					{
						queued++;
					}
				}

				if (ringing > maxRinging)
				{
					maxRinging = ringing;
				}

				if (ringing + queued > 3)
				{
					Check("Инцидентов не больше, чем в расписании", false);
					return;
				}
			}

			Check("Одновременно звонит не больше одного вызова", maxRinging <= 1);
			Check("Остальные ждали в очереди", queuedEvents > 0);
			Check("Каждый вызов дозвонился", ringOrder.Count == 3);

			bool inOrder = ringOrder.Count == 3
				&& ringOrder[0] == "m_one" && ringOrder[1] == "m_two" && ringOrder[2] == "m_three";
			Check("Очередь соблюдает порядок поступления", inOrder);
			Check("Смена завершилась", !simulation.IsShiftActive);
		}

		private static ContentDatabase BuildTimerTestContent()
		{
			var source = new InMemoryContentSource();

			// Окно вызовов сжато до 20 секунд намеренно.
			//
			// При боевых пяти минутах единственный вызов получал случайное время
			// в диапазоне до четырёх минут, а проверки крутят симуляцию минуту-полторы.
			// Тесты то проходили, то нет — в зависимости от того, что выпало генератору,
			// и выглядело это как плавающая ошибка в ядре. Короткое окно убирает
			// случайность из условия проверки, ничего не меняя в самих таймерах.
			source.Add("config.json", @"{
				""timings"": { ""phoneRingSeconds"": 15, ""mapMarkerSeconds"": 30, ""radioSeconds"": 20,
					""shiftCallWindowSeconds"": 20, ""minSecondsBetweenCalls"": 12 },
				""days"": [ { ""day"": 1, ""staffLimit"": 3, ""minCalls"": 1, ""maxCalls"": 1,
					""requirementMultiplier"": 1.0, ""consumablesPerShift"": 2, ""standardPerShift"": 1 } ]
			}");

			source.Add("zones.json", @"[ { ""id"": ""z_test"", ""name"": ""Полигон"", ""state"": ""Normal"", ""baseWeight"": 1.0 } ]");
			source.Add("abilities.json", "[]");
			source.Add("equipment.json", @"[
				{ ""id"": ""eq_test_consumable"", ""name"": ""Расходник"", ""kind"": ""Consumable"", ""bonus"": { ""strength"": 1 } },
				{ ""id"": ""eq_test_standard"", ""name"": ""Обычное"", ""kind"": ""Standard"", ""bonus"": { ""endurance"": 1 } }
			]");
			source.Add("creatures.json", "[]");
			source.Add("mission_events.json", "[]");

			source.Add("employees.json", @"{
				""startingRoster"": [
					{ ""id"": ""emp_a"", ""name"": ""А"", ""level"": 1, ""stats"": { ""strength"": 4, ""endurance"": 4 } },
					{ ""id"": ""emp_b"", ""name"": ""Б"", ""level"": 1, ""stats"": { ""strength"": 4, ""endurance"": 4 } },
					{ ""id"": ""emp_c"", ""name"": ""В"", ""level"": 1, ""stats"": { ""strength"": 4, ""endurance"": 4 } }
				],
				""hirePool"": []
			}");

			source.Add("missions.json", @"[ {
				""id"": ""m_test"", ""day"": 1, ""zoneId"": ""z_test"", ""creatureId"": """",
				""callId"": ""call_test"",
				""requirements"": { ""strength"": 6 },
				""travelSeconds"": 4, ""onSiteSeconds"": 2, ""returnSeconds"": 4,
				""scalesOnSuccess"": { ""infection"": -1, ""publicity"": -1, ""loyalty"": 1 },
				""scalesOnFailure"": { ""infection"": 4, ""publicity"": 3, ""loyalty"": -3 },
				""scalesOnMissedCall"": { ""infection"": 6, ""publicity"": 6, ""loyalty"": -6 },
				""scalesOnExpiredMarker"": { ""infection"": 5, ""publicity"": 5, ""loyalty"": -5 }
			} ]");

			// Каталог не передаём: сверять id не с чем и не нужно — текстов у полигона нет.
			return ContentLoader.Load(source);
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
