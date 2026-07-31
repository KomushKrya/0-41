using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>Когда срабатывает спецспособность сотрудника (ДД, раздел 5).</summary>
	public enum AbilityConditionKind
	{
		/// <summary>Всегда.</summary>
		Always = 0,

		/// <summary>Против существа с указанным тегом («мимик», «перекожник»).</summary>
		AgainstCreatureTag = 1,

		/// <summary>Если группе выдано указанное снаряжение («Новые фильтры»).</summary>
		WithEquipment = 2
	}

	/// <summary>
	/// Только механика способности. Название и описание живут в текстовом движке
	/// (content/raw/UI/perks) и достаются по этому же Id — ядро текста не носит.
	/// </summary>
	public sealed class Ability
	{
		public string Id { get; set; } = string.Empty;

		public AbilityConditionKind Condition { get; set; } = AbilityConditionKind.Always;

		/// <summary>Тег существа или Id снаряжения — в зависимости от Condition.</summary>
		public string ConditionValue { get; set; } = string.Empty;

		/// <summary>Бонус по конкретным характеристикам («+2 к силе»).</summary>
		public StatBlock Bonus { get; set; } = StatBlock.Zero;

		/// <summary>Бонус ко всем характеристикам сразу («+1 ко всем против мимиков»).</summary>
		public int AllStatsBonus { get; set; }

		public bool IsActive(IReadOnlyCollection<string> creatureTags, IReadOnlyCollection<string> equipmentIds)
		{
			switch (Condition)
			{
				case AbilityConditionKind.Always:
					return true;
				case AbilityConditionKind.AgainstCreatureTag:
					return Contains(creatureTags, ConditionValue);
				case AbilityConditionKind.WithEquipment:
					return Contains(equipmentIds, ConditionValue);
				default:
					return false;
			}
		}

		public StatBlock GetEffectiveBonus()
		{
			return AllStatsBonus == 0 ? Bonus : Bonus.Add(StatBlock.Uniform(AllStatsBonus));
		}

		private static bool Contains(IReadOnlyCollection<string> values, string target)
		{
			if (values == null || string.IsNullOrEmpty(target))
			{
				return false;
			}

			foreach (string value in values)
			{
				if (string.Equals(value, target, System.StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}
	}
}
