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
		/// <summary>Id перков. Названия достаёт слой Godot из текстового движка по этим id.</summary>
		IReadOnlyList<string> AbilityIds,
		string PortraitId,
		int Age,
		/// <summary>Кусочки досье: id фраз, разворачивает интерфейс через текстовый движок.</summary>
		IReadOnlyList<string> BioIds);

	/// <summary>
	/// Вызов глазами интерфейса. Прозы нет: название и реплики звонящего разворачиваются
	/// по CallId, вводная и варианты — по MissionEventId через текстовый движок.
	/// </summary>
	public sealed record IncidentView(
		string Id,
		string MissionId,
		string CallId,
		string ZoneId,
		/// <summary>Дом на карте. Пусто — домов нет, метка ставится по координатам зоны.</summary>
		string BuildingId,
		string MissionEventId,
		MissionTier Tier,
		ConsequenceCap ConsequenceCap,
		IncidentPhase Phase,
		double RemainingSeconds,
		StatBlock Requirements,
		/// <summary>Сколько человек берёт этот вызов. Обычно один.</summary>
		int SquadLimit,
		IReadOnlyList<string> SquadEmployeeIds,
		IReadOnlyList<string> EquipmentIds);

	public sealed record ZoneView(string Id, string Name, double MapX, double MapY);

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
		IReadOnlyList<StatMatch> Matches,
		double MatchScore,
		double SuccessChance,
		bool IsPerfectMatch);

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

	/// <summary>
	/// Кандидат в меню найма. Перки — то, ради чего в этом меню вообще есть выбор,
	/// поэтому они здесь: без них два кандидата с похожими характеристиками неразличимы.
	/// Названия перков интерфейс достаёт из текстового движка по этим id.
	/// </summary>
	public sealed record HireCandidateView(
		string Id,
		string Name,
		string RankTitle,
		int Level,
		StatBlock Stats,
		IReadOnlyList<string> AbilityIds,
		string PortraitId,
		int Age,
		IReadOnlyList<string> BioIds);

	public sealed record ShiftStatusView(
		int Day,
		bool IsShiftActive,
		double ShiftTime,
		bool IsCallWindowClosed,
		int OpenIncidents,
		int PendingCalls,
		int QueuedCalls,
		int StaffLimit,
		bool IsTimeFrozen,
		ScaleValues Scales,
		bool IsGameOver,
		GameOverReason? GameOverReason);
}
