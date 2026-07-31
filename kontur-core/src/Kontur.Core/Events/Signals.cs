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

	public sealed record IncidentCreated(string IncidentId, string MissionId, string BuildingId, string CallerName, double RingSeconds) : IGameEvent;

	public sealed record CallAnswered(string IncidentId, string MissionId, string Title, string BriefingText) : IGameEvent;

	public sealed record CallMissed(string IncidentId, string MissionId) : IGameEvent;

	public sealed record MapMarkerSpawned(string IncidentId, string BuildingId, double LifetimeSeconds) : IGameEvent;

	public sealed record MapMarkerExpired(string IncidentId, string BuildingId) : IGameEvent;

	/// <summary>Игрок нажал на метку — компьютер должен открыть экран отправки.</summary>
	public sealed record DispatchScreenRequested(string IncidentId, string MissionId) : IGameEvent;

	public sealed record SquadDispatched(
		string IncidentId,
		IReadOnlyList<string> EmployeeIds,
		IReadOnlyList<string> EquipmentIds,
		double TravelSeconds) : IGameEvent;

	public sealed record SquadArrived(string IncidentId, string BuildingId) : IGameEvent;

	public sealed record RadioTriggered(
		string IncidentId,
		string SituationText,
		IReadOnlyList<RadioOptionView> Options,
		double ResponseSeconds) : IGameEvent;

	public sealed record RadioMissed(string IncidentId) : IGameEvent;

	public sealed record RadioOptionChosen(string IncidentId, string OptionId, string OptionText) : IGameEvent;

	public sealed record MissionResolved(MissionOutcome Outcome) : IGameEvent;

	public sealed record ScalesChanged(ScaleValues Values, ScaleDelta Delta, string Reason) : IGameEvent;

	public sealed record EmployeeInjured(string EmployeeId, string EmployeeName, string IncidentId) : IGameEvent;

	public sealed record EmployeeKilled(string EmployeeId, string EmployeeName, string IncidentId) : IGameEvent;

	public sealed record EmployeeExperienceGained(string EmployeeId, int Amount, int Total) : IGameEvent;

	public sealed record EmployeeLeveledUp(string EmployeeId, int NewLevel, int UnspentSkillPoints) : IGameEvent;

	public sealed record EmployeeStatsChanged(string EmployeeId, StatBlock Stats, int UnspentSkillPoints) : IGameEvent;

	public sealed record EmployeeHired(string EmployeeId, string EmployeeName, int Day) : IGameEvent;

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

	public sealed record EquipmentConsumed(string EquipmentId, int RemainingQuantity) : IGameEvent;

	public sealed record EquipmentAcquired(string EquipmentId, bool IsShiftOnly) : IGameEvent;

	public sealed record EquipmentLost(string EquipmentId, string Reason) : IGameEvent;

	public sealed record IncidentClosed(string IncidentId, bool WasSuccess) : IGameEvent;

	public sealed record GameOverTriggered(GameOverReason Reason, ScaleValues Values, int Day) : IGameEvent;

	/// <summary>
	/// Вариант ответа по радио в том виде, в каком его видит игрок.
	/// Намеренно НЕ содержит Quality и множителей: подсказок в интерфейсе быть не должно (ДД, раздел 8).
	/// </summary>
	public sealed record RadioOptionView(string Id, string Text);

	public sealed record ShiftSummary(
		int TotalIncidents,
		int Successes,
		int Failures,
		int MissedCalls,
		int ExpiredMarkers,
		int Injuries,
		int Deaths);
}
