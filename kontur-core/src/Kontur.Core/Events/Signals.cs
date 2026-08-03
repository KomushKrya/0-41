using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Events
{
	// Список сигналов из раздела 13 ДД. Каждый сигнал — иммутабельная запись:
	// подписчик не может испортить состояние ядра, поменяв поле события.

	public sealed record ShiftStarted(int Day, int StaffLimit, string ShiftNoteTitle, string ShiftNoteText) : IGameEvent;

	/// <summary>Окно приёма новых вызовов закрылось (5 минут), но смена ещё идёт.</summary>
	public sealed record CallWindowClosed(int Day, int OpenIncidents) : IGameEvent;

	public sealed record ShiftEnded(int Day, string OutroCutsceneId, ShiftSummary Summary) : IGameEvent;

	public sealed record IncidentCreated(string IncidentId, string MissionId, string BuildingId, string CallId, double RingSeconds) : IGameEvent;
		/// <summary>Id звонка в текстовом движке. CallerName остаётся совместимым fallback-ом.</summary>

	/// <summary>Звонок ожидает свободной линии; здание уже выбрано и закреплено за инцидентом.</summary>
	public sealed record IncidentQueued(string IncidentId, string MissionId, string BuildingId, int Position) : IGameEvent;

	public sealed record CallAnswered(string IncidentId, string MissionId, string CallId) : IGameEvent;

	public sealed record BriefingConfirmed(string IncidentId, string MissionId, string BuildingId, double MarkerSeconds) : IGameEvent;

	public sealed record CallMissed(string IncidentId, string MissionId) : IGameEvent;

	public sealed record MapMarkerSpawned(string IncidentId, string BuildingId, double LifetimeSeconds) : IGameEvent;

	public sealed record MapMarkerExpired(string IncidentId, string BuildingId) : IGameEvent;

	/// <summary>Игрок нажал на метку — компьютер должен открыть экран отправки.</summary>
	public sealed record DispatchScreenRequested(string IncidentId, string MissionId) : IGameEvent;
	public sealed record DispatchScreenClosed(string IncidentId) : IGameEvent;

	public sealed record SquadDispatched(
		string IncidentId,
		IReadOnlyList<string> EmployeeIds,
		IReadOnlyList<string> EquipmentIds,
		double TravelSeconds) : IGameEvent;

	public sealed record SquadArrived(string IncidentId, string BuildingId) : IGameEvent;

	public sealed record MissionExecutionStarted(string IncidentId, string BuildingId, double DurationSeconds) : IGameEvent;

	public sealed record RadioTriggered(
		string IncidentId,
		string MissionEventId,
		IReadOnlyList<RadioOptionOffer> Options,
		double ResponseSeconds) : IGameEvent;

	public sealed record RadioOptionOffer(string Id, StatBlock Requirements, bool IsAvailable, string RequiredEquipmentId);

	public sealed record RadioMissed(string IncidentId) : IGameEvent;
	public sealed record RadioAnswered(string IncidentId, string MissionEventId) : IGameEvent;

	public sealed record RadioOptionChosen(string IncidentId, string MissionEventId, string OptionId) : IGameEvent;

	public sealed record MissionResolved(MissionOutcome Outcome) : IGameEvent;

	/// <summary>
	/// Результат миссии, подготовленный для цельного модального экрана. Ядро
	/// передаёт только игровые id: текст и изображение выбирает UI.
	/// </summary>
	public sealed record MissionOutcomeReady(
		string IncidentId,
		string MissionId,
		bool IsSuccess,
		MissionResolutionReason Reason,
		string SummaryContentId,
		string CreatureId,
		IReadOnlyList<string> ReturningEmployeeIds,
		IReadOnlyList<string> InjuredEmployeeIds,
		IReadOnlyList<string> KilledEmployeeIds,
		double ReturnSeconds,
		bool SquadWiped) : IGameEvent;

	public sealed record SquadReturning(string IncidentId, string BuildingId, double ReturnSeconds) : IGameEvent;

	public sealed record ScalesChanged(ScaleValues Values, ScaleDelta Delta, string Reason) : IGameEvent;

	public sealed record EmployeeInjured(string EmployeeId, string EmployeeName, string IncidentId) : IGameEvent;

	public sealed record EmployeeKilled(string EmployeeId, string EmployeeName, string IncidentId) : IGameEvent;

	public sealed record EmployeeExperienceGained(string EmployeeId, int Amount, int Total) : IGameEvent;

	public sealed record EmployeeLeveledUp(string EmployeeId, int NewLevel, int UnspentSkillPoints) : IGameEvent;

	public sealed record EmployeeStatsChanged(string EmployeeId, StatBlock Stats, int UnspentSkillPoints) : IGameEvent;

	public sealed record EmployeeHired(string EmployeeId, string EmployeeName, int Day) : IGameEvent;

	public sealed record HiringOpened(
		int NextDay,
		int StaffLimit,
		int LivingStaff,
		int FreeSlots,
		IReadOnlyList<string> CandidateIds) : IGameEvent;

	public sealed record SquadReturned(string IncidentId, IReadOnlyList<string> EmployeeIds) : IGameEvent;

	/// <summary>Отчёт появился на компьютере (ДД, раздел 3, п. 12).</summary>
	public sealed record MissionReportReady(MissionReport Report) : IGameEvent;

	/// <summary>
	/// В энциклопедии открыто новое свойство существа (ДД, раздел 10).
	/// Текста здесь нет: абзац под свойство разворачивает интерфейс через текстовый движок.
	/// </summary>
	public sealed record CreatureRevealed(string CreatureId, string PropertyId) : IGameEvent;

	/// <summary>Сюжетный флаг поменялся. Виджеты с условным текстом перечитывают себя по нему.</summary>
	public sealed record FlagChanged(string Flag, bool Value) : IGameEvent;

	/// <summary>Существо опознано и добавлено в энциклопедию впервые.</summary>
	public sealed record CreatureIdentified(string CreatureId) : IGameEvent;

	public sealed record EquipmentConsumed(string EquipmentId, string EquipmentName, int RemainingQuantity) : IGameEvent;

	public sealed record EquipmentAcquired(string EquipmentId, string EquipmentName, bool IsShiftOnly) : IGameEvent;

	public sealed record EquipmentLost(string EquipmentId, string EquipmentName, string Reason) : IGameEvent;

	public sealed record IncidentClosed(string IncidentId, bool WasSuccess) : IGameEvent;

	public sealed record GameOverTriggered(GameOverReason Reason, ScaleValues Values, int Day) : IGameEvent;

	/// <summary>
	/// Вариант ответа по радио в том виде, в каком его видит игрок.
	/// Намеренно НЕ содержит Quality и множителей: подсказок в интерфейсе быть не должно (ДД, раздел 8).
	/// </summary>
	/// <summary>Состояние симуляции приостановлено одним или несколькими владельцами модальных экранов.</summary>
	public sealed record TimeFreezeChanged(bool IsFrozen, IReadOnlyCollection<string> Owners) : IGameEvent;

	public sealed record ShiftSummary(
		int TotalIncidents,
		int Successes,
		int Failures,
		int MissedCalls,
		int ExpiredMarkers,
		int Injuries,
		int Deaths);
}
