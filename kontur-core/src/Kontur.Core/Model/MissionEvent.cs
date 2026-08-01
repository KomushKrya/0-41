using System;
using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Геймплейная сторона варианта решения на выезде.
	///
	/// Числа поделены между двумя источниками намеренно. Тип диалога, надбавка к сложности
	/// и порог доступности приходят из текста (`content/raw/mission_events`), потому что
	/// автор правит их вместе с формулировкой варианта — держать их отдельно значило бы
	/// разъезжаться при каждой вычитке. Всё остальное — риски, шкалы, карантин — чистый
	/// баланс и живёт в data/mission_events.json.
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
		/// Порог по сумме характеристик группы. Пусто — вариант открыт всегда.
		/// Считается по уже отправленному отряду: слабый состав закрывает умные решения.
		/// </summary>
		public StatBlock Requirements { get; set; } = StatBlock.Zero;

		/// <summary>Доступен ли вариант такой группе.</summary>
		public bool IsUnlockedBy(StatBlock squadStats)
		{
			return GetShortfall(squadStats).Total == 0;
		}

		/// <summary>
		/// Чего не хватает до открытия, по характеристикам. Нули там, где хватает.
		/// Интерфейс показывает это причиной блокировки: «нужна Ловкость 6, у группы 4».
		/// </summary>
		public StatBlock GetShortfall(StatBlock squadStats)
		{
			StatBlock shortfall = StatBlock.Zero;

			for (int i = 0; i < StatKinds.All.Length; i++)
			{
				StatKind kind = StatKinds.All[i];
				int missing = Requirements[kind] - squadStats[kind];
				if (missing > 0)
				{
					shortfall = shortfall.With(kind, missing);
				}
			}

			return shortfall;
		}

		public double DeathChanceMultiplier { get; set; } = 1.0;

		public double InjuryChanceMultiplier { get; set; } = 1.0;

		/// <summary>Вариант «оцепить и закрыть район» — переводит зону в карантин (ДД, раздел 9).</summary>
		public bool AppliesQuarantine { get; set; }

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
