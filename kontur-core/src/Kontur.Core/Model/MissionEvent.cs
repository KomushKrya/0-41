using System;
using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Геймплейная сторона варианта решения на выезде.
	///
	/// Числа поделены между двумя источниками намеренно. Из текста
	/// (`content/raw/missions/radio`) приходит только список проверяемых характеристик:
	/// автор решает, чем берётся этот вариант, но не насколько он труден. Все числа —
	/// тип диалога, надбавка, риски, шкалы, карантин — живут в data/mission_events.json,
	/// а порог по каждой названной характеристике подставляется из требований миссии.
	///
	/// Совпадение наборов ключей между текстом и данными проверяет загрузчик.
	/// </summary>
	public sealed class MissionEventOption
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>Из текста: надбавка к сложности, +N к каждой требуемой характеристике.</summary>
		public int RequirementModifier { get; set; }

		/// <summary>
		/// Из текста: хороший, нейтральный или плохой. В интерфейс не передаётся —
		/// подсказок о «правильности» быть не должно (ДД, раздел 8). Нужен для умолчаний,
		/// проверок на сборке и автопилота в тестах.
		/// </summary>
		public MissionEventQuality Quality { get; set; } = MissionEventQuality.Neutral;

		/// <summary>
		/// Характеристики, по которым проверяется этот вариант. Из текста, без чисел.
		/// Пусто — проверять нечего: исход варианта предрешён.
		/// </summary>
		public IReadOnlyList<StatKind> CheckedStats { get; set; } = new List<StatKind>();

		/// <summary>
		/// Подставляет требования миссии в характеристики этого варианта.
		///
		/// Вариант не запирается составом группы: выбрать можно любой, а недобор бьёт
		/// по шансу, а не по доступности. Поэтому от миссии берутся пороги только тех
		/// характеристик, которые назвал текст, — остальные к этому решению не относятся.
		/// Пустой список означает «проверки нет», и тогда требований не остаётся вовсе.
		/// </summary>
		public StatBlock ResolveRequirements(StatBlock missionRequirements)
		{
			if (CheckedStats.Count == 0)
			{
				return StatBlock.Zero;
			}

			StatBlock resolved = StatBlock.Zero;
			for (int i = 0; i < CheckedStats.Count; i++)
			{
				StatKind kind = CheckedStats[i];
				resolved = resolved.With(kind, missionRequirements[kind]);
			}

			return resolved;
		}

		public double DeathChanceMultiplier { get; set; } = 1.0;

		public double InjuryChanceMultiplier { get; set; } = 1.0;

		/// <summary>Вариант «оцепить и закрыть район» — переводит зону в карантин (ДД, раздел 9).</summary>
		/// <summary>Дополнительное изменение шкал именно за этот выбор.</summary>
		public ScaleDelta ExtraScales { get; set; } = ScaleDelta.Zero;

		/// <summary>Свойство существа, которое гарантированно будет замечено при этом выборе.</summary>
		public string? RevealsPropertyId { get; set; }

		/// <summary>
		/// Ужесточение потолка последствий для этого варианта. Ослабить потолок миссии
		/// нельзя: тихо переждать в кладовке безопасно везде, но выбор не может сделать
		/// смертельным вызов, который дизайнер объявил безопасным.
		/// </summary>
		public ConsequenceCap? ConsequenceCapOverride { get; set; }
	}

	/// <summary>
	/// Вмешательство игрока по радио (ДД, раздел 8). Id совпадает с записью типа
	/// mission_event в текстовом движке: вводная и формулировки вариантов лежат там.
	/// </summary>
	public sealed class MissionEventDefinition
	{
		public string Id { get; set; } = string.Empty;

		public List<MissionEventOption> Options { get; } = new List<MissionEventOption>();

		public MissionEventOption? FindOption(string optionId)
		{
			for (int i = 0; i < Options.Count; i++)
			{
				if (string.Equals(Options[i].Id, optionId, StringComparison.OrdinalIgnoreCase))
				{
					return Options[i];
				}
			}

			return null;
		}
	}
}
