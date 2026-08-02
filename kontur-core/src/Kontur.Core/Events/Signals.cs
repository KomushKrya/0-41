using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Events
{
	// Список сигналов из раздела 13 ДД. Каждый сигнал — иммутабельная запись:
	// подписчик не может испортить состояние ядра, поменяв поле события.

	public sealed record ShiftStarted(int Day, int StaffLimit, string ShiftNoteId) : IGameEvent;

	/// <summary>Окно приёма новых вызовов закрылось (5 минут), но смена ещё идёт.</summary>
	public sealed record CallWindowClosed(int Day, int OpenIncidents) : IGameEvent;

	public sealed record ShiftEnded(int Day, string OutroCutsceneId, ShiftSummary Summary) : IGameEvent;

	public sealed record IncidentCreated(
		string IncidentId,
		string MissionId,
		string ZoneId,
		/// <summary>Дом на карте. Пусто — домов в контенте нет, ставьте метку по координатам зоны.</summary>
		string BuildingId,
		string CallId,
		double RingSeconds) : IGameEvent;

	/// <summary>Трубка снята. Текст задания интерфейс берёт по CallId из текстового движка.</summary>
	/// <summary>
	/// Вызов поступил, но линия занята — ждёт очереди. Телефон по нему ещё не звонит:
	/// IncidentCreated придёт, когда очередь дойдёт. Интерфейсу это нужно, чтобы показать
	/// индикатор «на линии ждут» и не удивлять игрока внезапной пачкой звонков.
	/// </summary>
	public sealed record IncidentQueued(string IncidentId, string CallId, int Position) : IGameEvent;

	public sealed record CallAnswered(string IncidentId, string MissionId, string CallId) : IGameEvent;

	public sealed record CallMissed(string IncidentId, string MissionId) : IGameEvent;

	public sealed record MapMarkerSpawned(string IncidentId, string ZoneId, string BuildingId, double LifetimeSeconds) : IGameEvent;

	public sealed record MapMarkerExpired(string IncidentId, string ZoneId, string BuildingId) : IGameEvent;

	/// <summary>Игрок нажал на метку — компьютер должен открыть экран отправки.</summary>
	public sealed record DispatchScreenRequested(string IncidentId, string MissionId) : IGameEvent;

	public sealed record SquadDispatched(
		string IncidentId,
		IReadOnlyList<string> EmployeeIds,
		IReadOnlyList<string> EquipmentIds,
		double TravelSeconds) : IGameEvent;

	public sealed record SquadArrived(string IncidentId, string ZoneId, string BuildingId) : IGameEvent;

	/// <summary>Экран отправки закрыт без отправки. Метка продолжает висеть, время идёт дальше.</summary>
	public sealed record DispatchScreenClosed(string IncidentId) : IGameEvent;

	/// <summary>
	/// Глобальное время остановлено или пущено дальше.
	///
	/// Пока оно стоит, не идёт ничего: ни таймеры вызовов, ни дорога группы, ни приём
	/// новых звонков. Мир ждёт решения игрока. Интерфейсу это нужно, чтобы гасить
	/// индикаторы обратного отсчёта и не анимировать движение по карте.
	/// </summary>
	public sealed record TimeFreezeChanged(bool IsFrozen, string Reason) : IGameEvent;

	/// <summary>
	/// Радио затрещало. Ни вводной, ни формулировок вариантов здесь нет: интерфейс
	/// разворачивает MissionEventId через текстовый движок и берёт варианты оттуда же,
	/// в том же порядке. Ядро присылает только ключи — по ним UI и отвечает.
	/// </summary>
	public sealed record RadioTriggered(
		string IncidentId,
		string MissionEventId,
		IReadOnlyList<RadioOptionOffer> Options,
		double ResponseSeconds) : IGameEvent;

	/// <summary>
	/// Вариант в том виде, в каком его видит игрок: ключ и то, за что тут спросят.
	///
	/// Нажать можно любой вариант: состав группы решает не доступность, а шанс.
	/// Требования — пороги миссии, подставленные в характеристики этого варианта;
	/// пустой блок означает, что проверки нет вовсе.
	///
	/// Тип диалога (хороший/нейтральный/плохой) сюда не попадает намеренно — подсказок
	/// о правильности в интерфейсе быть не должно (ДД, раздел 8).
	/// </summary>
	public sealed record RadioOptionOffer(
		string Id,
		StatBlock Requirements);

	/// <summary>Игрок взял радио: экран вариантов открыт, мир остановлен.</summary>
	public sealed record RadioAnswered(string IncidentId, string MissionEventId) : IGameEvent;

	public sealed record RadioMissed(string IncidentId) : IGameEvent;

	public sealed record RadioOptionChosen(string IncidentId, string MissionEventId, string OptionId) : IGameEvent;

	public sealed record MissionResolved(MissionOutcome Outcome) : IGameEvent;

	/// <summary>
	/// Дело на объекте кончилось — пора показать экран итога: выполнено или провалено,
	/// кого зацепило, группа выезжает обратно.
	///
	/// Приходит в тот же момент, что и MissionResolved, но отдельным сигналом и с уже
	/// разложенными полями: MissionResolved — это внутренний расчёт со всеми промежуточными
	/// числами, а здесь ровно то, что нужно нарисовать на экране.
	///
	/// Только для выездов, где группа действительно доехала. На пропущенный звонок и
	/// протухшую метку сигнал не приходит: докладывать некому и возвращаться некому,
	/// там достаточно CallMissed и MapMarkerExpired.
	/// </summary>
	public sealed record MissionOutcomeReady(
		string IncidentId,
		string MissionId,
		string ZoneId,
		bool IsSuccess,
		MissionResolutionReason Reason,
		/// <summary>Запись типа `report` — текст исхода. Пусто, если под эту комбинацию текста нет.</summary>
		string SummaryTextId,
		string CreatureId,
		IReadOnlyList<string> ReturningEmployeeIds,
		IReadOnlyList<string> InjuredEmployeeIds,
		IReadOnlyList<string> KilledEmployeeIds,
		/// <summary>Сколько группа будет ехать обратно. 0 — возвращаться некому.</summary>
		double ReturnSeconds,
		bool SquadWiped) : IGameEvent;

	public sealed record ScalesChanged(ScaleValues Values, ScaleDelta Delta, string Reason) : IGameEvent;

	public sealed record EmployeeInjured(string EmployeeId, string EmployeeName, string IncidentId) : IGameEvent;

	public sealed record EmployeeKilled(string EmployeeId, string EmployeeName, string IncidentId) : IGameEvent;

	public sealed record EmployeeExperienceGained(string EmployeeId, int Amount, int Total) : IGameEvent;

	public sealed record EmployeeLeveledUp(string EmployeeId, int NewLevel, int UnspentSkillPoints) : IGameEvent;

	public sealed record EmployeeStatsChanged(string EmployeeId, StatBlock Stats, int UnspentSkillPoints) : IGameEvent;

	public sealed record EmployeeHired(string EmployeeId, string EmployeeName, int Day) : IGameEvent;

	/// <summary>
	/// Смена закрыта, можно набирать людей на следующую. Приходит сразу за ShiftEnded.
	///
	/// Не приходит, когда брать некого: партия проиграна или штат уже полный. Меню найма
	/// в этих случаях открывать не нужно — пустой список кандидатов игрок читает как поломку.
	///
	/// Список кандидатов зафиксирован на этот день: GetHireCandidates(NextDay) вернёт
	/// ровно этих людей в этом же порядке, сколько бы раз его ни спросили.
	/// </summary>
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

	public sealed record ZoneStateChanged(string ZoneId, ZoneState OldState, ZoneState NewState, string Reason) : IGameEvent;

	public sealed record EquipmentConsumed(string EquipmentId, string EquipmentName, int RemainingQuantity) : IGameEvent;

	public sealed record EquipmentAcquired(string EquipmentId, string EquipmentName, bool IsShiftOnly) : IGameEvent;

	public sealed record EquipmentLost(string EquipmentId, string EquipmentName, string Reason) : IGameEvent;

	public sealed record IncidentClosed(string IncidentId, bool WasSuccess) : IGameEvent;

	public sealed record GameOverTriggered(GameOverReason Reason, ScaleValues Values, int Day) : IGameEvent;

	/// <summary>
	/// Партия загружена из сохранения. Единственное событие, после которого интерфейс
	/// обязан перерисовать себя целиком по снимкам Get*: потока событий, который привёл
	/// партию в это состояние, не было — было чтение файла.
	///
	/// Мир в этот момент остановлен. Закончив перерисовку, вызовите ResumeAfterLoad().
	/// </summary>
	public sealed record GameLoaded(int Day, string SavedAtUtc, string Label) : IGameEvent;

	public sealed record ShiftSummary(
		int TotalIncidents,
		int Successes,
		int Failures,
		int MissedCalls,
		int ExpiredMarkers,
		int Injuries,
		int Deaths);
}
