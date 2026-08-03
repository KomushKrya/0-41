using System;
using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>Смысловой уровень вызова из нового контентного контракта.</summary>
	public enum MissionTier
	{
		Filler = 0,
		Story = 1
	}

	/// <summary>Максимальная тяжесть последствий, разрешённая сценарием.</summary>
	public enum ConsequenceCap
	{
		None = 0,
		Injury = 1,
		Death = 2
	}

	public static class ConsequenceCaps
	{
		public static ConsequenceCap DefaultFor(MissionTier tier)
		{
			return tier == MissionTier.Story ? ConsequenceCap.Death : ConsequenceCap.Injury;
		}

		public static ConsequenceCap Tighten(ConsequenceCap first, ConsequenceCap second)
		{
			return first < second ? first : second;
		}

		public static bool AllowsInjury(ConsequenceCap cap)
		{
			return cap >= ConsequenceCap.Injury;
		}

		public static bool AllowsDeath(ConsequenceCap cap)
		{
			return cap >= ConsequenceCap.Death;
		}
	}

	/// <summary>Пара текстов отчёта из нового data/missions.json.</summary>
	public sealed class MissionReportPair
	{
		public string SuccessId { get; set; } = string.Empty;
		public string FailureId { get; set; } = string.Empty;

		public string Get(bool isSuccess)
		{
			return isSuccess ? SuccessId : FailureId;
		}
	}

	/// <summary>
	/// Авторский дизайн одного вызова. Приходит из JSON (текстовый пайплайн, ДД раздел 14).
	/// Ядро не генерирует миссии процедурно — только выбирает из пула на день.
	/// </summary>
	public sealed class MissionDefinition
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>День демо (1..4), для которого миссия доступна.</summary>
		public int Day { get; set; } = 1;

		/// <summary>Story/Filler из нового ядра. Районы к этому полю не относятся.</summary>
		public MissionTier Tier { get; set; } = MissionTier.Filler;

		public ConsequenceCap? ConsequenceCapOverride { get; set; }

		public ConsequenceCap EffectiveCap
		{
			get { return ConsequenceCapOverride ?? ConsequenceCaps.DefaultFor(Tier); }
		}

		public string CreatureId { get; set; } = string.Empty;


		/// <summary>Кто звонит — для экрана телефона.</summary>

		/// <summary>Id записи звонка в текстовом движке. Пустое значение оставляет совместимый старый текст.</summary>
		public string CallId { get; set; } = string.Empty;

		/// <summary>Нативное имя ссылки на запись звонка в новом ядре.</summary>

		/// <summary>Краткое описание задания на экране после ответа на звонок.</summary>

		/// <summary>Id отдельного брифинга в текстовом движке.</summary>

		/// <summary>Требуемые показатели, сравниваются с суммой характеристик группы (ДД, раздел 7).</summary>
		public StatBlock Requirements { get; set; } = StatBlock.Zero;

		/// <summary>Время движения группы к месту (пунктирная линия на карте).</summary>
		public double TravelSeconds { get; set; } = 12.0;

		/// <summary>Время работы на объекте до резолва (после прибытия/после ответа по радио).</summary>
		public double OnSiteSeconds { get; set; } = 6.0;

		/// <summary>Время возвращения в главное управление.</summary>
		public double ReturnSeconds { get; set; } = 10.0;

		/// <summary>
		/// Радио-энкаунтер. Если null — вмешательство игрока этой миссией не предусмотрено
		/// (ДД, раздел 8: определяется дизайном задания, а не случайным шансом).
		/// </summary>
		/// <summary>Id MissionEvent в data и текстовом движке.</summary>
		public string? MissionEventId { get; set; }

		public bool HasMissionEvent
		{
			get { return !string.IsNullOrWhiteSpace(MissionEventId); }
		}

		/// <summary>Главная характеристика нового расчёта. Старый расчёт остаётся совместимым.</summary>
		public StatKind? PrimaryStat { get; set; }

		/// <summary>Максимум сотрудников в группе именно этого вызова.</summary>
		public int SquadLimit { get; set; } = 1;

		public ScaleDelta ScalesOnSuccess { get; set; } = ScaleDelta.Zero;

		public ScaleDelta ScalesOnFailure { get; set; } = ScaleDelta.Zero;

		/// <summary>Пропущенный звонок — шкалы сразу меняются в худшую сторону (ДД, раздел 4).</summary>
		public ScaleDelta ScalesOnMissedCall { get; set; } = ScaleDelta.Zero;

		/// <summary>Метка истекла — автоматический провал (ДД, раздел 4).</summary>
		public ScaleDelta ScalesOnExpiredMarker { get; set; } = ScaleDelta.Zero;

		public int ExperienceOnSuccess { get; set; } = 100;

		public int ExperienceOnFailure { get; set; } = 25;

		/// <summary>Базовые риски на сотрудника при провале; при успехе умножаются на понижающий коэффициент из конфига.</summary>
		public double InjuryChance { get; set; } = 0.25;

		public double DeathChance { get; set; } = 0.08;


		/// <summary>Id отчёта при успешном завершении.</summary>


		/// <summary>Id отчёта при неуспехе.</summary>

		/// <summary>
		/// Отчёты нового формата: ключ — выбранный вариант, пустой ключ — исход без радио.
		/// Старые поля Report* остаются fallback-ом для ранее созданных миссий.
		/// </summary>
		public Dictionary<string, MissionReportPair> Reports { get; } =
			new Dictionary<string, MissionReportPair>(StringComparer.OrdinalIgnoreCase);

		public string ResolveReportId(string? optionId, bool isSuccess)
		{
			MissionReportPair? pair;
			if (!string.IsNullOrEmpty(optionId) && Reports.TryGetValue(optionId, out pair))
			{
				return pair.Get(isSuccess);
			}

			if (Reports.TryGetValue(string.Empty, out pair))
			{
				return pair.Get(isSuccess);
			}

			return string.Empty;
		}

		/// <summary>
		/// Свойства существа, которые проявляются именно на этой миссии.
		/// Будут замечены (и раскроют абзац энциклопедии), если группа выжила.
		/// </summary>
		public List<string> ManifestedPropertyIds { get; } = new List<string>();
	}
}
