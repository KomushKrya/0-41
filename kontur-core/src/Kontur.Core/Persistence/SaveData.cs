using System.Collections.Generic;

namespace Kontur.Core.Persistence
{
	/// <summary>
	/// Снимок партии. Плоские структуры, а не ссылки на игровые объекты: файл сохранения
	/// должен переживать правку кода, а не только текущий запуск.
	///
	/// Здесь нет ничего из контента — ни требований миссии, ни текстов, ни характеристик
	/// снаряжения. Только идентификаторы и то, что игрок наиграл. Контент читается заново
	/// при загрузке, поэтому исправленная опечатка в тексте или подкрученный баланс
	/// доезжают до старых сохранений сами.
	/// </summary>
	public sealed class SaveData
	{
		/// <summary>
		/// Версия формата. Растёт, когда меняется раскладка полей несовместимо.
		/// Загрузка чужой версии отклоняется с внятным сообщением, а не падает на разборе.
		/// </summary>
		public int Version { get; set; } = CurrentVersion;

		public const int CurrentVersion = 1;

		/// <summary>Метка времени в формате ISO — для списка сохранений в интерфейсе.</summary>
		public string SavedAtUtc { get; set; } = string.Empty;

		/// <summary>Свободная подпись: «День 3, 04:12» или что угодно от интерфейса.</summary>
		public string Label { get; set; } = string.Empty;

		public int Seed { get; set; }

		/// <summary>Состояние генератора случайных чисел на момент сохранения.</summary>
		public ulong RandomState { get; set; }

		public int Day { get; set; }

		public double Infection { get; set; }

		public double Publicity { get; set; }

		public double Loyalty { get; set; }

		public bool IsGameOver { get; set; }

		/// <summary>Имя причины проигрыша либо пусто.</summary>
		public string GameOverReason { get; set; } = string.Empty;

		public List<SavedEmployee> Roster { get; set; } = new List<SavedEmployee>();

		public List<SavedZone> Zones { get; set; } = new List<SavedZone>();

		public List<SavedStack> Inventory { get; set; } = new List<SavedStack>();

		public List<string> Flags { get; set; } = new List<string>();

		public List<SavedCreatureKnowledge> Encyclopedia { get; set; } = new List<SavedCreatureKnowledge>();

		public List<string> HiredCandidateIds { get; set; } = new List<string>();

		public List<string> UsedMissionIds { get; set; } = new List<string>();

		public List<SavedReport> Reports { get; set; } = new List<SavedReport>();

		/// <summary>Кандидаты, показанные в меню найма. Сгенерированных не пересоздать — только сохранить.</summary>
		public List<SavedEmployee> HireOffers { get; set; } = new List<SavedEmployee>();

		public int HireOffersDay { get; set; } = -1;

		/// <summary>Стартовый состав уже собран — экран выбора больше не показывать.</summary>
		public bool StartingRosterConfirmed { get; set; }

		/// <summary>Состояние смены. null — сохранение сделано между сменами.</summary>
		public SavedShift? Shift { get; set; }
	}

	public sealed class SavedEmployee
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public string RankTitle { get; set; } = string.Empty;

		public string PortraitId { get; set; } = string.Empty;

		public string ArchetypeId { get; set; } = string.Empty;

		public int Level { get; set; } = 1;

		public int Strength { get; set; }

		public int Perception { get; set; }

		public int Endurance { get; set; }

		public int Agility { get; set; }

		public int Composure { get; set; }

		public int Experience { get; set; }

		public int UnspentSkillPoints { get; set; }

		/// <summary>Имя значения EmployeeStatus.</summary>
		public string Status { get; set; } = "Available";

		public bool IsInjured { get; set; }

		public string CurrentIncidentId { get; set; } = string.Empty;

		public List<string> AbilityIds { get; set; } = new List<string>();

		/// <summary>Для кандидатов: с какого дня доступен.</summary>
		public int AvailableFromDay { get; set; } = 1;
	}

	public sealed class SavedZone
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>Имя значения ZoneState.</summary>
		public string State { get; set; } = "Normal";

		public int SuccessStreak { get; set; }

		public int FailStreak { get; set; }
	}

	public sealed class SavedStack
	{
		public string Id { get; set; } = string.Empty;

		public int Quantity { get; set; }

		public bool IsShiftOnly { get; set; }
	}

	public sealed class SavedCreatureKnowledge
	{
		public string CreatureId { get; set; } = string.Empty;

		public List<string> RevealedPropertyIds { get; set; } = new List<string>();
	}

	public sealed class SavedReport
	{
		public string IncidentId { get; set; } = string.Empty;

		public string MissionId { get; set; } = string.Empty;

		public string ReportId { get; set; } = string.Empty;

		public string CreatureId { get; set; } = string.Empty;

		public string ChosenOptionId { get; set; } = string.Empty;

		public bool IsSuccess { get; set; }

		public List<string> RevealedPropertyIds { get; set; } = new List<string>();
	}

	/// <summary>
	/// Смена в разгаре: сколько прошло, что с расписанием и в какой фазе каждый вызов.
	/// Ради этого блока сохранение и затевалось — без него пришлось бы доигрывать день до конца.
	/// </summary>
	public sealed class SavedShift
	{
		public bool IsActive { get; set; }

		public double ShiftTime { get; set; }

		public double LineFreeAt { get; set; }

		public bool CallWindowClosed { get; set; }

		public int SpawnedCount { get; set; }

		public int TotalIncidents { get; set; }

		public int Successes { get; set; }

		public int Failures { get; set; }

		public int MissedCalls { get; set; }

		public int ExpiredMarkers { get; set; }

		public int Injuries { get; set; }

		public int Deaths { get; set; }

		/// <summary>Вызовы, которые ещё не наступили.</summary>
		public List<SavedIncident> Pending { get; set; } = new List<SavedIncident>();

		/// <summary>Вызовы в работе и уже закрытые за эту смену.</summary>
		public List<SavedIncident> Incidents { get; set; } = new List<SavedIncident>();
	}

	public sealed class SavedIncident
	{
		public string Id { get; set; } = string.Empty;

		public string MissionId { get; set; } = string.Empty;

		/// <summary>Дом, куда едет группа. Выбирается на смену, поэтому его нужно сохранять.</summary>
		public string BuildingId { get; set; } = string.Empty;

		/// <summary>Имя значения IncidentPhase.</summary>
		public string Phase { get; set; } = "Scheduled";

		public double ScheduledAtSeconds { get; set; }

		public bool HasTimer { get; set; }

		public double TimerDuration { get; set; }

		public double TimerRemaining { get; set; }

		public bool TimerRunning { get; set; }

		public List<string> SquadEmployeeIds { get; set; } = new List<string>();

		public List<string> EquipmentIds { get; set; } = new List<string>();

		public string MissionEventId { get; set; } = string.Empty;

		public string ChosenOptionId { get; set; } = string.Empty;

		public bool RadioWasTriggered { get; set; }

		public bool RadioWasMissed { get; set; }

		/// <summary>
		/// Отчёт, уже собранный по этому вызову. Хранится здесь, а не только в общем списке:
		/// пока группа едет обратно, отчёт существует, но на компьютер ещё не лёг.
		/// </summary>
		public SavedReport? Report { get; set; }

		/// <summary>Итог был разрешён успешно — нужно, чтобы правильно закрыть вызов после возвращения.</summary>
		public bool OutcomeWasSuccess { get; set; }

		public bool HasOutcome { get; set; }
	}
}
