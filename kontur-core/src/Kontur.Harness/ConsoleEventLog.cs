using System;
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
					return $"=== СМЕНА {s.Day} НАЧАТА. Лимит штата: {s.StaffLimit}. «{s.ShiftNoteTitle}»";

				case CallWindowClosed s:
					return $"--- Окно приёма вызовов закрыто. Открытых вызовов: {s.OpenIncidents}";

				case ShiftEnded s:
					return $"=== СМЕНА {s.Day} ЗАВЕРШЕНА. Вызовов {s.Summary.TotalIncidents}: успех {s.Summary.Successes}, провал {s.Summary.Failures} "
						+ $"(пропущено звонков {s.Summary.MissedCalls}, просрочено меток {s.Summary.ExpiredMarkers}). "
						+ $"Травм {s.Summary.Injuries}, погибло {s.Summary.Deaths}. Ролик: {s.OutroCutsceneId}";

				case IncidentCreated s:
					return $"ТЕЛЕФОН звонит — {s.IncidentId} ({s.BuildingId}), звонит {s.CallerName}. {s.RingSeconds:0} с на ответ";

				case CallAnswered s:
					return $"Трубка снята — {s.IncidentId}: {s.Title}";

				case CallMissed s:
					return $"!! ЗВОНОК ПРОПУЩЕН — {s.IncidentId}";

				case MapMarkerSpawned s:
					return $"КАРТА: метка {s.IncidentId} в {s.BuildingId}, {s.LifetimeSeconds:0} с на отправку";

				case MapMarkerExpired s:
					return $"!! МЕТКА ПРОСРОЧЕНА — {s.IncidentId}";

				case DispatchScreenRequested s:
					return _verbose ? $"КОМПЬЮТЕР: экран отправки — {s.IncidentId}" : null;

				case SquadDispatched s:
					return $"ОТПРАВКА {s.IncidentId}: [{string.Join(", ", s.EmployeeIds)}] снаряжение [{string.Join(", ", s.EquipmentIds)}], в пути {s.TravelSeconds:0} с";

				case SquadArrived s:
					return $"Группа на месте — {s.IncidentId}";

				case RadioTriggered s:
					return $"РАДИО {s.IncidentId}: {s.SituationText} | вариантов: {s.Options.Count}, {s.ResponseSeconds:0} с";

				case RadioMissed s:
					return $"!! РАДИО БЕЗ ОТВЕТА — {s.IncidentId} (бросок с повышенным риском)";

				case RadioOptionChosen s:
					return $"Выбор по радио {s.IncidentId}: {s.OptionText}";

				case MissionResolved s:
					return FormatOutcome(s.Outcome);

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
						+ (string.IsNullOrEmpty(s.Report.CreatureId) ? " | существо не опознано" : $" | существо: {s.Report.CreatureId}");

				case CreatureIdentified s:
					return $"ЭНЦИКЛОПЕДИЯ: опознано существо {s.CreatureId}";

				case CreatureRevealed s:
					return $"ЭНЦИКЛОПЕДИЯ: {s.CreatureId} — открыто свойство {s.PropertyId}";

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
					reason = "требования покрыты";
					break;
				case MissionResolutionReason.DiceSuccess:
				case MissionResolutionReason.DiceFailure:
					reason = string.Format(
						CultureInfo.InvariantCulture,
						"бросок {0:0.000} против шанса {1:0.000}, покрытие {2:0.00}",
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

			return $"ИТОГ {outcome.IncidentId}: {result} ({reason}){stats}";
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
