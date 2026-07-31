using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Качество варианта по лору (ДД, раздел 8). В интерфейс НЕ передаётся —
	/// игрок должен сопоставить вариант с абзацами энциклопедии сам.
	/// Поле нужно только для баланса и отладочного лога.
	/// </summary>
	public enum RadioOptionQuality
	{
		Best = 0,
		Good = 1,
		Bad = 2
	}

	public sealed class RadioOption
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>Текст рекомендации, который видит игрок.</summary>
		public string Text { get; set; } = string.Empty;

		/// <summary>
		/// Множитель требуемых показателей миссии.
		/// Лучший по лору вариант снижает требования сильнее всего (например 0.4),
		/// заведомо провальный — повышает (например 1.6).
		/// </summary>
		public double RequirementMultiplier { get; set; } = 1.0;

		public double DeathChanceMultiplier { get; set; } = 1.0;

		public double InjuryChanceMultiplier { get; set; } = 1.0;

		/// <summary>Вариант «оцепить и закрыть район» — переводит зону в карантин (ДД, раздел 9).</summary>
		/// <summary>Дополнительное изменение шкал именно за этот выбор.</summary>
		public ScaleDelta ExtraScales { get; set; } = ScaleDelta.Zero;

		/// <summary>Свойство существа, которое гарантированно будет замечено при этом выборе.</summary>
		public string? RevealsPropertyId { get; set; }

		public RadioOptionQuality Quality { get; set; } = RadioOptionQuality.Good;
	}

	public sealed class RadioEncounter
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>Что сотрудники видят на месте — вводная по радио.</summary>
		public string SituationText { get; set; } = string.Empty;

		/// <summary>Ровно три варианта по ДД, но ядро не навязывает это число жёстко.</summary>
		public List<RadioOption> Options { get; } = new List<RadioOption>();

		public RadioOption? FindOption(string optionId)
		{
			for (int i = 0; i < Options.Count; i++)
			{
				if (string.Equals(Options[i].Id, optionId, System.StringComparison.OrdinalIgnoreCase))
				{
					return Options[i];
				}
			}

			return null;
		}
	}
}
