using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Api
{
	/// <summary>
	/// Снимки состояния для интерфейса. Возвращаются по значению, чтобы слой Godot
	/// физически не мог мутировать состояние симуляции в обход команд.
	/// </summary>
	public sealed record EmployeeView(
		string Id,
		string Name,
		string RankTitle,
		int Level,
		StatBlock Stats,
		int Experience,
		int ExperienceToNextLevel,
		int UnspentSkillPoints,
		EmployeeStatus Status,
		bool IsInjured,
		string? CurrentIncidentId,
		IReadOnlyList<string> AbilityNames,
		string PortraitId);

	public sealed record IncidentView(
		string Id,
		string MissionId,
		string Title,
		string BuildingId,
		string CallerName,
		IncidentPhase Phase,
		double RemainingSeconds,
		StatBlock Requirements,
		IReadOnlyList<string> SquadEmployeeIds,
		IReadOnlyList<string> EquipmentIds);

	/// <summary>
	/// Предпросмотр отправки: что получится, если послать именно этот состав с этим снаряжением.
	/// Считается тем же кодом, что и настоящий резолв, поэтому экран отправки не может
	/// разойтись с реальным расчётом.
	///
	/// Показывать игроку весь набор необязательно: требования и сумму группы — да,
	/// точный процент — на усмотрение дизайна (ДД, раздел 8: подсказок в интерфейсе быть не должно).
	/// </summary>
	public sealed record DispatchEstimateView(
		StatBlock Requirements,
		StatBlock SquadStats,
		double Coverage,
		double SuccessChance,
		bool IsAutoSuccess);

	public sealed record EquipmentSlotView(
		string Id,
		string Name,
		string Description,
		EquipmentKind Kind,
		int Quantity,
		bool IsShiftOnly);

	/// <summary>
	/// Что известно об существе. Имя и абзацы берутся интерфейсом из текстового движка
	/// по CreatureId и RevealedPropertyIds — ядро прозу не хранит.
	/// </summary>
	public sealed record EncyclopediaEntryView(
		string CreatureId,
		string IllustrationId,
		IReadOnlyList<string> RevealedPropertyIds,
		int TotalProperties);

	public sealed record HireCandidateView(string Id, string Name, string RankTitle, int Level, StatBlock Stats);

	public sealed record ShiftStatusView(
		int Day,
		bool IsShiftActive,
		double ShiftTime,
		bool IsCallWindowClosed,
		int OpenIncidents,
		int PendingCalls,
		int StaffLimit,
		ScaleValues Scales,
		bool IsGameOver,
		GameOverReason? GameOverReason);
}
