using System;
using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>Ключ отчёта: какой текст показать на компьютере при таком исходе.</summary>
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
	/// Геймплейная сторона вызова: тайминги, требования, риски, последствия.
	/// Прозы здесь нет — только ссылки на записи текстового движка по id.
	///
	/// Разделение простое: всё, что автор пишет словами (реплики звонящего, вводная
	/// на выезде, отчёт), живёт в content/raw; всё, что дизайнер крутит числами,
	/// живёт здесь. Пересечение — ровно одно, `requirement_modifier` у вариантов,
	/// и оно объяснено в MissionEventOption.
	/// </summary>
	public sealed class MissionDefinition
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>День демо (1..4), для которого миссия доступна.</summary>
		public int Day { get; set; } = 1;

		/// <summary>
		/// Сюжетный вызов или филлер. По умолчанию филлер: забытое поле не должно
		/// молча включать смерти — цена ошибки здесь несимметрична.
		/// </summary>
		public MissionTier Tier { get; set; } = MissionTier.Filler;

		/// <summary>
		/// Потолок последствий. Если в контенте не задан, берётся от уровня миссии:
		/// сюжетная — гибель, филлер — только травмы.
		/// </summary>
		public ConsequenceCap? ConsequenceCapOverride { get; set; }

		public ConsequenceCap EffectiveCap
		{
			get { return ConsequenceCapOverride ?? ConsequenceCaps.DefaultFor(Tier); }
		}

		public string ZoneId { get; set; } = string.Empty;

		/// <summary>
		/// Существо, с которым столкнётся группа. Может быть пустым: не за каждой
		/// аномалией стоит существо со статьёй в энциклопедии (грибок, поле, зона).
		/// </summary>
		public string CreatureId { get; set; } = string.Empty;

		/// <summary>Запись типа `call` — то, что игрок услышит в трубке.</summary>
		public string CallId { get; set; } = string.Empty;

		/// <summary>
		/// Запись типа `mission_event`. Пусто — вмешательство этой миссией не предусмотрено
		/// (ДД, раздел 8: решает дизайн задания, а не случайность).
		/// </summary>
		public string MissionEventId { get; set; } = string.Empty;

		public bool HasMissionEvent
		{
			get { return !string.IsNullOrEmpty(MissionEventId); }
		}

		/// <summary>
		/// Пороги по характеристикам. Ноль означает «эта характеристика на вызове не нужна»
		/// и в расчёт не идёт вовсе.
		///
		/// Сравнивается с **лучшим в группе** по каждой характеристике отдельно, а не
		/// с суммой: порог «Восприятие 6» закрывает конкретный человек, а не толпа.
		/// </summary>
		public StatBlock Requirements { get; set; } = StatBlock.Zero;

		/// <summary>
		/// Главная характеристика вызова — весит вдвое при расчёте процента.
		/// Null — все требования равнозначны. Читается как «тут главное — хладнокровие»
		/// и даёт автору рычаг, не заставляя перекручивать сами пороги.
		/// </summary>
		public StatKind? PrimaryStat { get; set; }

		/// <summary>Время движения группы к месту (пунктирная линия на карте).</summary>
		public double TravelSeconds { get; set; } = 12.0;

		/// <summary>Время работы на объекте до резолва.</summary>
		public double OnSiteSeconds { get; set; } = 6.0;

		public double ReturnSeconds { get; set; } = 10.0;

		public ScaleDelta ScalesOnSuccess { get; set; } = ScaleDelta.Zero;

		public ScaleDelta ScalesOnFailure { get; set; } = ScaleDelta.Zero;

		/// <summary>Пропущенный звонок — шкалы сразу меняются в худшую сторону (ДД, раздел 4).</summary>
		public ScaleDelta ScalesOnMissedCall { get; set; } = ScaleDelta.Zero;

		public ScaleDelta ScalesOnExpiredMarker { get; set; } = ScaleDelta.Zero;

		public int ExperienceOnSuccess { get; set; } = 100;

		public int ExperienceOnFailure { get; set; } = 25;

		public double InjuryChance { get; set; } = 0.25;

		public double DeathChance { get; set; } = 0.08;

		/// <summary>
		/// Отчёты: ключ — id варианта решения, пустая строка — исход без вмешательства.
		/// Связка живёт здесь, а не во фронтматтере отчёта: сам отчёт по схеме
		/// текстового движка не знает ни о миссии, ни о варианте.
		/// </summary>
		public Dictionary<string, MissionReportPair> Reports { get; } =
			new Dictionary<string, MissionReportPair>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Свойства существа, которые проявляются именно на этой миссии.
		/// Будут замечены (и раскроют абзац энциклопедии), если группа выжила.
		/// </summary>
		public List<string> ManifestedPropertyIds { get; } = new List<string>();

		/// <summary>
		/// Id отчёта под конкретный исход. Пусто, если автор ещё не написал нужную
		/// комбинацию — интерфейс в этом случае просто не покажет текст.
		/// </summary>
		public string ResolveReportId(string? optionId, bool isSuccess)
		{
			MissionReportPair? pair;

			if (!string.IsNullOrEmpty(optionId) && Reports.TryGetValue(optionId!, out pair))
			{
				return pair.Get(isSuccess);
			}

			return Reports.TryGetValue(string.Empty, out pair) ? pair.Get(isSuccess) : string.Empty;
		}
	}
}
