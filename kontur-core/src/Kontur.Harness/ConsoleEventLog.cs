using System;
using System.Collections.Generic;
using System.Globalization;
using Kontur.Core.Events;
using Kontur.Core.Model;

namespace Kontur.Harness
{
	/// <summary>
	/// Печатает поток событий ядра. Это и есть основной инструмент отладки:
	/// если в логе последовательность фаз неправильная — виновато ядро, а не движок.
	/// </summary>
	public sealed class ConsoleEventLog
	{
		private readonly Func<double> _clock;
		private readonly bool _verbose;

		public ConsoleEventLog(Func<double> clock, bool verbose)
		{
			_clock = clock;
			_verbose = verbose;
		}

		public void Attach(IEventBus bus)
		{
			bus.SubscribeAll(OnEvent);
		}

		private void OnEvent(IGameEvent gameEvent)
		{
			string? line = Format(gameEvent);
			if (line == null)
			{
				return;
			}

			Console.WriteLine("[{0,7}] {1}", FormatTime(_clock()), line);
		}

		private static string FormatOffers(IReadOnlyList<RadioOptionOffer> offers)
		{
			var parts = new List<string>();
			for (int i = 0; i < offers.Count; i++)
			{
				parts.Add(offers[i].IsUnlocked
					? offers[i].Id
					: $"{offers[i].Id} (закрыт: не хватает {offers[i].Shortfall})");
			}

			return string.Join(", ", parts);
		}

		/// <summary>Дом в скобках — только если он есть. Пустая карта не должна сорить в логе.</summary>
		private static string FormatBuilding(string buildingId)
		{
			return string.IsNullOrEmpty(buildingId) ? string.Empty : ", дом " + buildingId;
		}

		/// <summary>Ноль — обучающая смена без таймеров игрока, обратного отсчёта нет.</summary>
		private static string FormatTimer(double seconds)
		{
			return seconds > 0.0 ? $"{seconds:0} с" : "без таймера";
		}

		private static string FormatTime(double seconds)
		{
			int minutes = (int)(seconds / 60.0);
			double rest = seconds - (minutes * 60.0);
			return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00.0}", minutes, rest);
		}

		private string? Format(IGameEvent e)
		{
			switch (e)
			{
				case ShiftStarted s:
					return $"=== СМЕНА {s.Day} НАЧАТА. Лимит штата: {s.StaffLimit}. Записка: {s.ShiftNoteId}";

				case CallWindowClosed s:
					return $"--- Окно приёма вызовов закрыто. Открытых вызовов: {s.OpenIncidents}";

				case ShiftEnded s:
					return $"=== СМЕНА {s.Day} ЗАВЕРШЕНА. Вызовов {s.Summary.TotalIncidents}: успех {s.Summary.Successes}, провал {s.Summary.Failures} "
						+ $"(пропущено звонков {s.Summary.MissedCalls}, просрочено меток {s.Summary.ExpiredMarkers}). "
						+ $"Травм {s.Summary.Injuries}, погибло {s.Summary.Deaths}. Ролик: {s.OutroCutsceneId}";

				case IncidentCreated s:
					return $"ТЕЛЕФОН звонит — {s.IncidentId} ({s.ZoneId}{FormatBuilding(s.BuildingId)}), запись {s.CallId}. {FormatTimer(s.RingSeconds)}";

				case IncidentQueued s:
					return $"Линия занята — {s.IncidentId} ждёт очереди (в очереди {s.Position})";

				case CallAnswered s:
					return $"Трубка снята — {s.IncidentId}: {s.CallId}";

				case CallMissed s:
					return $"!! ЗВОНОК ПРОПУЩЕН — {s.IncidentId}";

				case MapMarkerSpawned s:
					return $"КАРТА: метка {s.IncidentId} в {s.ZoneId}{FormatBuilding(s.BuildingId)}, {s.LifetimeSeconds:0} с на отправку";

				case MapMarkerExpired s:
					return $"!! МЕТКА ПРОСРОЧЕНА — {s.IncidentId}";

				case DispatchScreenRequested s:
					return _verbose ? $"КОМПЬЮТЕР: экран отправки — {s.IncidentId}" : null;

				case SquadDispatched s:
					return $"ОТПРАВКА {s.IncidentId}: [{string.Join(", ", s.EmployeeIds)}] снаряжение [{string.Join(", ", s.EquipmentIds)}], в пути {s.TravelSeconds:0} с";

				case SquadArrived s:
					return $"Группа на месте — {s.IncidentId}";

				case RadioTriggered s:
					return $"РАДИО {s.IncidentId}: {s.MissionEventId} | варианты: {FormatOffers(s.Options)}, {FormatTimer(s.ResponseSeconds)}";

				case TimeFreezeChanged s:
					return s.IsFrozen
						? $"|| ВРЕМЯ ОСТАНОВЛЕНО ({s.Reason})"
						: "|> время идёт дальше";

				case RadioAnswered s:
					return $"Радио взято — {s.IncidentId}";

				case DispatchScreenClosed s:
					return _verbose ? $"Экран отправки закрыт — {s.IncidentId}" : null;

				case RadioMissed s:
					return $"!! РАДИО БЕЗ ОТВЕТА — {s.IncidentId} (бросок с повышенным риском)";

				case RadioOptionChosen s:
					return $"Выбор по радио {s.IncidentId}: {s.OptionId}";

				case MissionResolved s:
					return FormatOutcome(s.Outcome);

				case MissionOutcomeReady s:
					return "ЭКРАН ИТОГА " + s.IncidentId + ": "
						+ (s.IsSuccess ? "ВЫПОЛНЕНО" : "ПРОВАЛЕНО")
						+ (string.IsNullOrEmpty(s.SummaryTextId) ? " | текста нет" : " | " + s.SummaryTextId)
						+ (s.SquadWiped
							? " | возвращаться некому"
							: $" | возвращаются {s.ReturningEmployeeIds.Count} чел., {s.ReturnSeconds:0.#} с")
						+ (s.InjuredEmployeeIds.Count > 0 ? $" | ранено {s.InjuredEmployeeIds.Count}" : string.Empty)
						+ (s.KilledEmployeeIds.Count > 0 ? $" | погибло {s.KilledEmployeeIds.Count}" : string.Empty);

				case HiringOpened s:
					return $"НАЙМ ОТКРЫТ на день {s.NextDay}: штат {s.LivingStaff}/{s.StaffLimit}, "
						+ $"свободно мест {s.FreeSlots}, кандидатов {s.CandidateIds.Count}";

				case ScalesChanged s:
					return $"ШКАЛЫ {s.Delta} -> {s.Values} ({s.Reason})";

				case EmployeeInjured s:
					return $"ТРАВМА: {s.EmployeeName} ({s.IncidentId})";

				case EmployeeKilled s:
					return $"ГИБЕЛЬ: {s.EmployeeName} ({s.IncidentId})";

				case EmployeeLeveledUp s:
					return $"ПОВЫШЕНИЕ: {s.EmployeeId} -> уровень {s.NewLevel}, очков навыков {s.UnspentSkillPoints}";

				case EmployeeExperienceGained s:
					return _verbose ? $"Опыт: {s.EmployeeId} +{s.Amount} (всего {s.Total})" : null;

				case EmployeeStatsChanged s:
					return _verbose ? $"Характеристики {s.EmployeeId}: {s.Stats}" : null;

				case EmployeeHired s:
					return $"НАЙМ: {s.EmployeeName} (день {s.Day})";

				case SquadReturned s:
					return _verbose ? $"Группа вернулась — {s.IncidentId}" : null;

				case MissionReportReady s:
					return $"ОТЧЁТ {s.Report.IncidentId}: {(s.Report.IsSuccess ? "УСПЕХ" : "ПРОВАЛ")}"
						+ (string.IsNullOrEmpty(s.Report.ReportId) ? " | текста нет" : $" | {s.Report.ReportId}")
						+ (string.IsNullOrEmpty(s.Report.CreatureId) ? string.Empty : $" | существо: {s.Report.CreatureId}");

				case CreatureIdentified s:
					return $"ЭНЦИКЛОПЕДИЯ: опознано существо {s.CreatureId}";

				case CreatureRevealed s:
					return $"ЭНЦИКЛОПЕДИЯ: {s.CreatureId} — открыто свойство {s.PropertyId}";

				case ZoneStateChanged s:
					return $"КАРТА: зона {s.ZoneId} {s.OldState} -> {s.NewState} ({s.Reason})";

				case EquipmentConsumed s:
					return _verbose ? $"Расход: {s.EquipmentName}, осталось {s.RemainingQuantity}" : null;

				case EquipmentAcquired s:
					return $"Найдено снаряжение: {s.EquipmentName}{(s.IsShiftOnly ? " (только на эту смену)" : string.Empty)}";

				case EquipmentLost s:
					return $"Утрачено снаряжение: {s.EquipmentName} ({s.Reason})";

				case IncidentClosed s:
					return _verbose ? $"Вызов закрыт — {s.IncidentId}" : null;

				case GameOverTriggered s:
					return $"### GAME OVER: {FormatGameOver(s.Reason)} | {s.Values} | день {s.Day}";

				default:
					return _verbose ? e.GetType().Name : null;
			}
		}

		private static string FormatOutcome(MissionOutcome outcome)
		{
			string result = outcome.IsSuccess ? "УСПЕХ" : "ПРОВАЛ";
			string reason;
			switch (outcome.Reason)
			{
				case MissionResolutionReason.StatsCovered:
					reason = "профиль закрыт с запасом";
					break;
				case MissionResolutionReason.DiceSuccess:
				case MissionResolutionReason.DiceFailure:
					reason = string.Format(
						CultureInfo.InvariantCulture,
						"бросок {0:0.000} против шанса {1:0.000}, совпадение профилей {2:0.00}",
						outcome.Roll,
						outcome.SuccessChance,
						outcome.Coverage);
					break;
				case MissionResolutionReason.CallMissed:
					reason = "автопровал: звонок пропущен";
					break;
				case MissionResolutionReason.MarkerExpired:
					reason = "автопровал: группа не отправлена";
					break;
				default:
					reason = outcome.Reason.ToString();
					break;
			}

			string stats = outcome.EmployeeIds.Count == 0
				? string.Empty
				: $" | требовалось [{outcome.EffectiveRequirements}] / группа [{outcome.SquadStats}]";

			string cap = outcome.AppliedCap == ConsequenceCap.Death
				? string.Empty
				: $" | потолок: {(outcome.AppliedCap == ConsequenceCap.None ? "без потерь" : "только травмы")}";

			return $"ИТОГ {outcome.IncidentId}: {result} ({reason}){stats}{cap}";
		}

		private static string FormatGameOver(GameOverReason reason)
		{
			switch (reason)
			{
				case GameOverReason.InfectionMaxed:
					return "заражение достигло максимума";
				case GameOverReason.PublicityMaxed:
					return "гласность достигла максимума";
				case GameOverReason.LoyaltyDepleted:
					return "лояльность упала до нуля";
				default:
					return reason.ToString();
			}
		}
	}
}
