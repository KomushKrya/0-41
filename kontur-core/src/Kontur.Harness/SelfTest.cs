using System;
using System.Collections.Generic;
using Kontur.Core.Api;
using Kontur.Core.Config;
using Kontur.Core.Content;
using Kontur.Core.Events;
using Kontur.Core.Model;
using Kontur.Core.Persistence;
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
		private const string ExpectedRevealSignature = "0/3 1/3 0/3 0/3";
		private const int ExpectedRevealEvents = 1;

		public static bool Run(ContentDatabase content)
		{
			_passed = 0;
			_failed = 0;

			Console.WriteLine("SELFTEST ядра К.О.Н.Т.У.Р.");
			Console.WriteLine(new string('-', 78));

			TestCountdown();
			TestEventBus();
			TestCoverageMath();
			TestStatMatches();
			TestSuccessChanceCurve();
			TestTimings(content);
			TestTimeFreeze(content);
			TestObjectInteractionCommands(content);
			TestCallQueue(content);
			TestSaveRoundTrip(content);
			TestSaveLoadRejections(content);
			TestMissedCallIsAutoFailure(content);
			TestExpiredMarkerIsAutoFailure(content);
			TestEquipmentSlotLimits(content);
			TestStaffLimit(content);
			TestShiftEndsAfterLastIncident(content);
			TestHiringCapacity(content);
			TestEmployeeFactory(content);
			TestOutcomeAndHiring(content);
			TestRadioContracts(content);
			TestConsequenceCaps(content);
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

		private static void TestStatMatches()
		{
			var resolver = new MissionResolver(new ContentDatabase(), new SimulationConfig(), new XorShiftRandom(41));
			IReadOnlyList<StatMatch> perfect = resolver.EvaluateMatches(
				new StatBlock(6, 4, 0, 0, 0), new StatBlock(8, 6, 0, 0, 0), StatKind.Strength);
			Check("StatMatch marks values above the exceed margin", perfect[0].Rating == StatMatchRating.Exceeds && perfect[1].Rating == StatMatchRating.Exceeds);
			Check("StatMatch scores an all-exceeds profile as perfect", Math.Abs(resolver.ComputeMatchScore(perfect) - 1.0) < 1e-9 && MissionResolver.IsPerfectMatch(perfect));

			IReadOnlyList<StatMatch> meets = resolver.EvaluateMatches(
				new StatBlock(6, 4, 0, 0, 0), new StatBlock(6, 5, 0, 0, 0), null);
			Check("StatMatch classifies threshold values as meets", meets[0].Rating == StatMatchRating.Meets && meets[1].Rating == StatMatchRating.Meets);
			Check("StatMatch applies the configured meets score", Math.Abs(resolver.ComputeMatchScore(meets) - 0.8) < 1e-9);

			IReadOnlyList<StatMatch> primary = resolver.EvaluateMatches(
				new StatBlock(6, 4, 0, 0, 0), new StatBlock(8, 3, 0, 0, 0), StatKind.Strength);
			IReadOnlyList<StatMatch> secondary = resolver.EvaluateMatches(
				new StatBlock(6, 4, 0, 0, 0), new StatBlock(8, 3, 0, 0, 0), null);
			Check("Primary stat receives extra weight in the profile score", resolver.ComputeMatchScore(primary) > resolver.ComputeMatchScore(secondary));

			IReadOnlyList<StatMatch> matches = resolver.EvaluateMatches(
				new StatBlock(5, 4, 0, 0, 0),
				new StatBlock(7, 2, 0, 0, 0),
				StatKind.Strength);
			Check("StatMatch returns one row per required stat", matches.Count == 2);
			Check("StatMatch classifies exceeded and below values", matches[0].Rating == StatMatchRating.Exceeds && matches[1].Rating == StatMatchRating.Below);
			Check("StatMatch score does not treat excess as a substitute", resolver.ComputeMatchScore(matches) < 1.0);
			Check("StatMatch perfect match requires every row", !MissionResolver.IsPerfectMatch(matches));
		}

		private static void TestSuccessChanceCurve()
		{
			ContentDatabase content = BuildMinimalContent();
			var resolver = new MissionResolver(content, content.Config, new XorShiftRandom(1));

			double half = resolver.ComputeSuccessChance(0.5, false);
			Check("StatMatch score is used as the direct success chance", Math.Abs(half - 0.5) < 1e-9);

			double capped = resolver.ComputeSuccessChance(0.99, false);
			Check("Шанс ограничен потолком 0.95", capped <= 0.95 + 1e-9);

			double missed = resolver.ComputeSuccessChance(0.5, true);
			Check("Просроченное радио режет шанс вдвое", Math.Abs(missed - System.Math.Max(content.Config.Resolution.MinDiceChance, 0.5 * content.Config.Resolution.RadioMissedChanceMultiplier)) < 1e-9);
		}

		private static void TestTimings(ContentDatabase content)
		{
			Check("Phone ring duration is exactly 15 seconds", Math.Abs(content.Config.Timings.PhoneRingSeconds - 15.0) < 1e-9);
			Check("Map marker lifetime is exactly 30 seconds", Math.Abs(content.Config.Timings.MapMarkerSeconds - 30.0) < 1e-9);
			Check("Radio response duration is exactly 20 seconds", Math.Abs(content.Config.Timings.RadioSeconds - 20.0) < 1e-9);
			Check("Call window lasts five minutes", Math.Abs(content.Config.Timings.ShiftCallWindowSeconds - 300.0) < 1e-9);
			Check($"Staff limit progression continues beyond authored days ({content.Config.GetStaffLimit(5)}/{content.Config.GetStaffLimit(9)})", content.Config.GetStaffLimit(5) == 7 && content.Config.GetStaffLimit(9) == 11);
			Check("Звонок 15 с", Math.Abs(content.Config.Timings.PhoneRingSeconds - 15.0) < 1e-9);
			Check("У метки задан положительный таймер", content.Config.Timings.MapMarkerSeconds > 0.0);
			Check("Радио 20 с", Math.Abs(content.Config.Timings.RadioSeconds - 20.0) < 1e-9);
			Check("Окно вызовов 5 минут", Math.Abs(content.Config.Timings.ShiftCallWindowSeconds - 300.0) < 1e-9);
			Check("Лимиты штата 3/4/5/6",
				content.Config.GetDay(1).StaffLimit == 3
				&& content.Config.GetDay(2).StaffLimit == 4
				&& content.Config.GetDay(3).StaffLimit == 5
				&& content.Config.GetDay(4).StaffLimit == 6);
		}

		private static void TestTimeFreeze(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 5);
			simulation.StartShift(2);
			simulation.Tick(0.25);
			double before = simulation.GetStatus().ShiftTime;

			simulation.FreezeTime("selftest.modal");
			simulation.Tick(10.0);
			Check("Пауза ядра останавливает время смены", Math.Abs(simulation.GetStatus().ShiftTime - before) < 1e-9);

			simulation.UnfreezeTime("selftest.modal");
			simulation.Tick(1.0);
			Check("Снятие своей паузы возобновляет время", simulation.GetStatus().ShiftTime > before);
		}

		/// <summary>Контракт предметов в комнате: телефон → карта/ПК → рация.</summary>
		private static void TestObjectInteractionCommands(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 77);
			string? incidentId = null;
			simulation.Events.Subscribe<IncidentCreated>(e => incidentId ??= e.IncidentId);
			simulation.StartShift(1);
			RunSeconds(simulation, 1.0, 0.25);

			bool briefing = incidentId != null
				&& simulation.AnswerCall(incidentId).IsSuccess
				&& simulation.ConfirmBriefing(incidentId).IsSuccess;
			Check("Телефон переводит вызов в брифинг и ставит метку", briefing);

			if (incidentId == null)
			{
				Check("Карта удерживает время только пока открыт dispatch", false);
				Check("Рация удерживает время только пока открыт диалог", false);
				return;
			}

			CommandResult openDispatch = simulation.OpenDispatchScreen(incidentId);
			bool dispatchFreeze = openDispatch.IsSuccess && simulation.IsTimeFrozen;
			simulation.CloseDispatchScreen(incidentId);
			Check("Карта удерживает время только пока открыт dispatch", dispatchFreeze && !simulation.IsTimeFrozen);

			IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
			CommandResult dispatch = simulation.DispatchSquad(incidentId, new[] { roster[0].Id }, Array.Empty<string>());
			RunSeconds(simulation, 15.0, 0.25);

			CommandResult answerRadio = simulation.AnswerRadio(incidentId);
			bool radioFreeze = dispatch.IsSuccess && answerRadio.IsSuccess && simulation.IsTimeFrozen;
			simulation.CloseRadio(incidentId);
			Check("Рация удерживает время только пока открыт диалог", radioFreeze && !simulation.IsTimeFrozen);
		}

		private static void TestCallQueue(ContentDatabase content)
		{
			TimingConfig timings = content.Config.Timings;
			DayConfig day = content.Config.GetDay(3);
			double window = timings.ShiftCallWindowSeconds;
			double gap = timings.MinSecondsBetweenCalls;
			int minCalls = day.MinCalls;
			int maxCalls = day.MaxCalls;
			try
			{
				timings.ShiftCallWindowSeconds = 4.0;
				timings.MinSecondsBetweenCalls = 0.0;
				day.MinCalls = 2;
				day.MaxCalls = 2;

				var simulation = new KonturSimulation(content, 6);
				bool wasQueued = false;
				simulation.Events.Subscribe<IncidentQueued>(_ => wasQueued = true);
				simulation.StartShift(3);
				RunSeconds(simulation, 5.0, 0.25);
				Check("Звонок, пришедший на занятую линию, попадает в очередь", wasQueued);
			}
			finally
			{
				timings.ShiftCallWindowSeconds = window;
				timings.MinSecondsBetweenCalls = gap;
				day.MinCalls = minCalls;
				day.MaxCalls = maxCalls;
			}
		}

		private static void TestSaveRoundTrip(ContentDatabase content)
		{
			var source = new KonturSimulation(content, 21);
			source.SetFlag("save_round_trip");
			string json = source.Save();

			var restored = new KonturSimulation(content, 99);
			CommandResult result = restored.Load(json);
			Check("Сохранение между сменами восстанавливает партию", result.IsSuccess && restored.IsFlagSet("save_round_trip"));

			source.StartShift(2);
			RunSeconds(source, 2.0, 0.25);
			int incidentCount = source.GetActiveIncidents().Count;
			string activeShiftJson = source.Save("selftest active shift");
			var restoredShift = new KonturSimulation(content, 42);
			CommandResult activeResult = restoredShift.Load(activeShiftJson);
			bool wasFrozenAfterLoad = restoredShift.IsTimeFrozen;
			restoredShift.ResumeAfterLoad();
			Check(
				"Сохранение активной смены восстанавливает вызовы и ждёт UI после загрузки",
				activeResult.IsSuccess
				&& restoredShift.IsShiftActive
				&& restoredShift.GetActiveIncidents().Count == incidentCount
				&& wasFrozenAfterLoad
				&& !restoredShift.IsTimeFrozen);
		}

		private static void TestMissedCallIsAutoFailure(ContentDatabase content)
		{
			// Во второй смене один вызов. Сжимаем окно расписания, чтобы проверять
			// пропуск звонка, а не ждать случайный слот до конца смены.
			double window = content.Config.Timings.ShiftCallWindowSeconds;
			content.Config.Timings.ShiftCallWindowSeconds = 3.0;
			var simulation = new KonturSimulation(content, 7);

			MissionOutcome? outcome = null;
			bool missed = false;
			simulation.Events.Subscribe<CallMissed>(_ => missed = true);
			simulation.Events.Subscribe<MissionResolved>(e => outcome ??= e.Outcome);

			ScaleValues before = simulation.GetStatus().Scales;

			simulation.StartShift(2);
			RunSeconds(simulation, 30.0, 0.25);
			content.Config.Timings.ShiftCallWindowSeconds = window;

			Check("Неотвеченный звонок помечается пропущенным", missed);
			Check("Пропуск звонка = автопровал", outcome != null && outcome.Reason == MissionResolutionReason.CallMissed);

			ScaleValues after = simulation.GetStatus().Scales;
			Check("Шкалы сразу меняются в худшую сторону",
				after.Infection > before.Infection && after.Publicity > before.Publicity && after.Loyalty < before.Loyalty);
		}

		private static void TestExpiredMarkerIsAutoFailure(ContentDatabase content)
		{
			double window = content.Config.Timings.ShiftCallWindowSeconds;
			content.Config.Timings.ShiftCallWindowSeconds = 3.0;
			var simulation = new KonturSimulation(content, 11);

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
			Check(
				"Ответ на звонок открывает брифинг, а подтверждение ставит метку",
				simulation.AnswerCall(ringingId).IsSuccess && simulation.ConfirmBriefing(ringingId).IsSuccess);
			}

			RunSeconds(simulation, content.Config.Timings.MapMarkerSeconds + 5.0, 0.25);
			content.Config.Timings.ShiftCallWindowSeconds = window;

			Check("Метка истекает по таймеру конфигурации", expired);
			Check("Истечение метки = автопровал", outcome != null && outcome.Reason == MissionResolutionReason.MarkerExpired);
		}

		private static void TestEquipmentSlotLimits(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 13);

			string? incidentId = null;
			simulation.Events.Subscribe<MapMarkerSpawned>(e => incidentId ??= e.IncidentId);
			simulation.Events.Subscribe<IncidentCreated>(e =>
			{
				if (simulation.AnswerCall(e.IncidentId).IsSuccess)
				{
					simulation.ConfirmBriefing(e.IncidentId);
				}
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

			IncidentView? current = FindIncident(simulation, incidentId);
			if (current != null && roster.Count > current.SquadLimit)
			{
				var tooLarge = new List<string>();
				for (int i = 0; i <= current.SquadLimit && i < roster.Count; i++)
				{
					tooLarge.Add(roster[i].Id);
				}

				Check("Squad larger than a mission limit is rejected", !simulation.DispatchSquad(incidentId, tooLarge, new List<string>()).IsSuccess);
			}
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

			IReadOnlyList<HireCandidateView> day1 = simulation.GetHireCandidates(1);
			Check("Фабрика формирует кандидатов уже в первый день", day1.Count >= content.Generator.CandidatesPerShift);
			Check("Фабрика прикрепляет id строк досье", day1.Count == 0 || day1[0].BioIds.Count == content.Generator.BioSlots.Count);

			IReadOnlyList<HireCandidateView> day2 = simulation.GetHireCandidates(2);
			Check("Со второго дня доступны кандидаты", day2.Count > 0);

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

		private static IncidentView? FindIncident(KonturSimulation simulation, string incidentId)
		{
			IReadOnlyList<IncidentView> incidents = simulation.GetActiveIncidents();
			for (int i = 0; i < incidents.Count; i++)
			{
				if (incidents[i].Id == incidentId)
				{
					return incidents[i];
				}
			}

			return null;
		}

		private static void TestShiftEndsAfterLastIncident(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 31);
			var order = new List<string>();
			int closed = 0;
			int ended = 0;

			simulation.Events.Subscribe<IncidentCreated>(e =>
			{
				simulation.AnswerCall(e.IncidentId);
				simulation.ConfirmBriefing(e.IncidentId);
			});
			simulation.Events.Subscribe<MapMarkerSpawned>(e =>
			{
				foreach (EmployeeView employee in simulation.GetRoster())
				{
					if (employee.Status == EmployeeStatus.Available)
					{
						simulation.DispatchSquad(e.IncidentId, new[] { employee.Id }, Array.Empty<string>());
						break;
					}
				}
			});
			simulation.Events.Subscribe<RadioTriggered>(e =>
			{
				if (e.Options.Count > 0)
				{
					simulation.AnswerRadio(e.IncidentId);
					simulation.ChooseRadioOption(e.IncidentId, e.Options[0].Id);
				}
			});
			simulation.Events.Subscribe<IncidentClosed>(_ => { closed++; order.Add("closed"); });
			simulation.Events.Subscribe<ShiftEnded>(_ => { ended++; order.Add("ended"); });

			simulation.StartShift(1);
			for (int i = 0; i < 6000 && ended == 0; i++) simulation.Tick(0.25);

			Check("Shift ends exactly once after active work", ended == 1);
			Check("At least one incident closed before ending", closed > 0);
			Check("ShiftEnded is the last lifecycle signal", order.Count > 0 && order[order.Count - 1] == "ended");
			Check("No open incidents remain after shift end", simulation.GetActiveIncidents().Count == 0);
		}

		private static void TestHiringCapacity(ContentDatabase content)
		{
			var simulation = new KonturSimulation(content, 23);
			int alive = simulation.GetRoster().Count;
			int dayFourLimit = content.Config.GetStaffLimit(4);
			int free = dayFourLimit - alive;
			IReadOnlyList<HireCandidateView> candidates = simulation.GetHireCandidates(4);

			Check("Hiring has room to grow the day four roster", free > 0);
			Check("Hiring offers enough candidates to fill every vacancy", candidates.Count >= free);
			Check("Hiring offers a choice beyond mandatory vacancies", candidates.Count > free);

			int hired = 0;
			for (int i = 0; i < candidates.Count; i++)
			{
				if (simulation.HireEmployee(candidates[i].Id, 4).IsSuccess) hired++;
			}
			Check("Hiring never exceeds the staff cap", alive + hired == dayFourLimit && simulation.GetRoster().Count == dayFourLimit);

			var heavyLossSimulation = new KonturSimulation(content, 29);
			int largeGap = content.Config.GetStaffLimit(4) - heavyLossSimulation.GetRoster().Count;
			IReadOnlyList<HireCandidateView> largeBatch = heavyLossSimulation.GetHireCandidates(4);
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool unique = true;
			foreach (HireCandidateView candidate in largeBatch) unique &= names.Add(candidate.Name);
			Check("Hiring can recover from every current roster vacancy", largeGap > 0 && largeBatch.Count >= largeGap);
			Check("Large hiring batch has no duplicate names", unique);
		}

		private static void TestEmployeeFactory(ContentDatabase content)
		{
			if (!content.Generator.IsEnabled)
			{
				Check("Employee factory is enabled", false);
				return;
			}

			var simulation = new KonturSimulation(content, 501);
			IReadOnlyList<HireCandidateView> first = simulation.GetHireCandidates(1);
			IReadOnlyList<HireCandidateView> again = simulation.GetHireCandidates(1);
			var twin = new KonturSimulation(content, 501);
			IReadOnlyList<HireCandidateView> twinList = twin.GetHireCandidates(1);
			bool stable = first.Count == again.Count && first.Count == twinList.Count;
			var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool valid = first.Count > 0;

			for (int i = 0; i < first.Count; i++)
			{
				HireCandidateView candidate = first[i];
				stable &= candidate.Id == again[i].Id && candidate.Id == twinList[i].Id
					&& candidate.Name == again[i].Name && candidate.Stats.Equals(twinList[i].Stats);
				valid &= ids.Add(candidate.Id) && names.Add(candidate.Name)
					&& candidate.Age >= content.Generator.MinAge && candidate.Age <= content.Generator.MaxAge
					&& candidate.BioIds.Count == content.Generator.BioSlots.Count;
				foreach (StatKind stat in StatKinds.All)
				{
					int value = candidate.Stats[stat];
					valid &= value >= content.Generator.MinStat && value <= content.Generator.MaxStat;
				}
			}

			Check("Employee factory produces candidates", first.Count > 0);
			Check("Employee factory is stable for a saved simulation", stable);
			Check("Factory candidates have unique ids, valid ages, bios and stats", valid);
		}

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

			var bot = new AutoOperator(simulation, content, RadioStrategy.Best, 909);
			double guard = 0.0;
			while (simulation.IsShiftActive && guard < 1800.0)
			{
				simulation.Tick(0.25);
				guard += 0.25;
				bot.Update();
			}

			Check("Mission outcome screen is emitted", outcomes.Count > 0);
			if (outcomes.Count > 0)
			{
				MissionOutcomeReady outcome = outcomes[0];
				Check("Mission outcome contains a text id", !string.IsNullOrEmpty(outcome.SummaryContentId));
				Check("Mission outcome describes return or a wiped squad", outcome.SquadWiped || (outcome.ReturningEmployeeIds.Count > 0 && outcome.ReturnSeconds > 0.0));
			}
			Check("Outcome is emitted before any corresponding return", outcomes.Count >= returns.Count);
			Check("Shift opens hiring exactly once", !simulation.IsShiftActive && hirings.Count == 1);
			if (hirings.Count > 0)
			{
				HiringOpened hiring = hirings[0];
				IReadOnlyList<HireCandidateView> menu = simulation.GetHireCandidates(hiring.NextDay);
				bool sameOrder = menu.Count == hiring.CandidateIds.Count;
				for (int i = 0; sameOrder && i < menu.Count; i++) sameOrder = menu[i].Id == hiring.CandidateIds[i];
				Check("Hiring event exposes the same candidate list as the API", hiring.NextDay == 2 && hiring.FreeSlots > 0 && sameOrder);
			}
		}

		private static void TestRadioContracts(ContentDatabase content)
		{
			var option = new MissionEventOption
			{
				Id = "checked",
				CheckedStats = new List<StatKind> { StatKind.Intellect, StatKind.Charisma },
				RequirementModifier = 2
			};
			var requirements = new StatBlock(7, 8, 9, 6, 5);
			StatBlock selected = option.ResolveRequirements(requirements);
			Check("Radio option checks only its declared stats", selected[StatKind.Intellect] == 8 && selected[StatKind.Charisma] == 5 && selected.Total == 13);

			var resolver = new MissionResolver(content, content.Config, new XorShiftRandom(1));
			var mission = new MissionDefinition { Id = "test", Day = 1, Requirements = requirements };
			StatBlock adjusted = resolver.ComputeEffectiveRequirements(mission, option, 1);
			Check("Radio modifier applies only to selected requirements", adjusted[StatKind.Intellect] == 10 && adjusted[StatKind.Charisma] == 7 && adjusted[StatKind.Strength] == 0);

			bool backed = true;
			bool reportsMapped = true;
			foreach (KeyValuePair<string, MissionDefinition> pair in content.Missions)
			{
				MissionDefinition current = pair.Value;
				reportsMapped &= !string.IsNullOrEmpty(current.ResolveReportId(null, true)) && !string.IsNullOrEmpty(current.ResolveReportId(null, false));
				if (!current.HasMissionEvent || !content.MissionEvents.TryGetValue(current.MissionEventId!, out MissionEventDefinition? missionEvent)) continue;
				foreach (MissionEventOption candidate in missionEvent.Options)
				{
					reportsMapped &= !string.IsNullOrEmpty(current.ResolveReportId(candidate.Id, true)) && !string.IsNullOrEmpty(current.ResolveReportId(candidate.Id, false));
					foreach (StatKind stat in candidate.CheckedStats) backed &= current.Requirements[stat] > 0;
				}
			}
			Check("Every radio stat check is backed by a mission requirement", backed);
			Check("Every radio option and outcome maps to a report id", reportsMapped);
		}

		private static void TestConsequenceCaps(ContentDatabase content)
		{
			Check("Filler consequence cap defaults to injury", ConsequenceCaps.DefaultFor(MissionTier.Filler) == ConsequenceCap.Injury);
			Check("Story consequence cap defaults to death", ConsequenceCaps.DefaultFor(MissionTier.Story) == ConsequenceCap.Death);
			Check("Consequence cap tightening keeps the stricter cap",
				ConsequenceCaps.Tighten(ConsequenceCap.Death, ConsequenceCap.Injury) == ConsequenceCap.Injury
				&& ConsequenceCaps.Tighten(ConsequenceCap.Injury, ConsequenceCap.None) == ConsequenceCap.None);
		}

		private static void TestSaveLoadRejections(ContentDatabase content)
		{
			var source = new KonturSimulation(content, 7);
			source.StartShift(1);
			RunSeconds(source, 2.0, 0.25);
			string json = source.Save();
			var target = new KonturSimulation(content, 7);
			int rosterBefore = target.GetRoster().Count;

			Check("Empty save is rejected", !target.Load(string.Empty).IsSuccess);
			Check("Malformed JSON save is rejected", !target.Load("{not json").IsSuccess);
			string wrongVersion = json.Replace("\"Version\": " + SaveData.CurrentVersion, "\"Version\": 999");
			Check("Save version replacement matched JSON", wrongVersion != json);
			Check("Unsupported save version is rejected", !target.Load(wrongVersion).IsSuccess);
			string missingMission = json.Replace("m_black_mold", "m_missing_for_selftest");
			Check("Save with a missing mission is rejected", !target.Load(missingMission).IsSuccess);
			Check("Rejected save does not mutate the target game", target.GetRoster().Count == rosterBefore && !target.IsShiftActive);
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
			string third = RunSignature(content, 100);

			Check("Один seed — один и тот же прогон", first == second);
			Check("Разный seed — разный прогон", first != third);
		}

		/// <summary>Сценарная смена: фиксированный порядок, вызовы по одному, обычные таймеры игрока.</summary>
		private static void TestTutorialShift(ContentDatabase content)
		{
			Kontur.Core.Config.DayConfig day1 = content.Config.GetDay(1);
			Check("День 1 помечен как сценарный", day1.IsScripted);
			Check("День 1 использует таймеры игрока", !day1.DisableTimers);

			var simulation = new KonturSimulation(content, 5);

			var order = new List<string>();
			int maxSimultaneous = 0;
			double ringSeconds = -1.0;
			bool missedCall = false;

			simulation.Events.Subscribe<IncidentCreated>(e =>
			{
				order.Add(e.MissionId);
				if (ringSeconds < 0.0)
				{
					ringSeconds = e.RingSeconds;
				}
			});
			simulation.Events.Subscribe<CallMissed>(_ => missedCall = true);

			simulation.StartShift(1);

			RunSeconds(simulation, 14.0, 0.25);
			Check("Звонок не срывается до истечения таймера", !missedCall);
			Check("Телефон получает обычный обратный отсчёт", Math.Abs(ringSeconds - content.Config.Timings.PhoneRingSeconds) < 1e-9);
			RunSeconds(simulation, 2.0, 0.25);
			Check("Звонок сценарной смены срывается по таймеру", missedCall);
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
			Check("Сценарный день не накладывает вызовы", maxSimultaneous == 1);
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

			simulation.StartShift(3);

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
				Check("За смену 5–7 вызовов", summary.TotalIncidents >= 5 && summary.TotalIncidents <= 7);
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
			simulation.StartShift(3);

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
