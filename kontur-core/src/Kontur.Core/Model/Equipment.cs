using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>Три вида снаряжения (ДД, раздел 6).</summary>
	public enum EquipmentKind
	{
		/// <summary>Расходник: тратится при использовании на вызове.</summary>
		Consumable = 0,

		/// <summary>Обычное: не тратится после успешно завершённого вызова.</summary>
		Standard = 1,

		/// <summary>Сюжетное: один раз за игру, теряется при гибели всей группы.</summary>
		Story = 2
	}

	public sealed class EquipmentDefinition
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public string Description { get; set; } = string.Empty;

		public EquipmentKind Kind { get; set; } = EquipmentKind.Consumable;

		/// <summary>Когда действует бонус: всегда или только против существа с указанным тегом.</summary>
		public AbilityConditionKind Condition { get; set; } = AbilityConditionKind.Always;

		/// <summary>Тег существа — используется только при Condition == AgainstCreatureTag.</summary>
		public string ConditionValue { get; set; } = string.Empty;

		/// <summary>Бонус действует на всю отправленную группу целиком (ДД, раздел 6).</summary>
		public StatBlock Bonus { get; set; } = StatBlock.Zero;

		public int AllStatsBonus { get; set; }

		public StatBlock GetEffectiveBonus()
		{
			return AllStatsBonus == 0 ? Bonus : Bonus.Add(StatBlock.Uniform(AllStatsBonus));
		}

		public bool IsActive(IReadOnlyCollection<string> creatureTags)
		{
			switch (Condition)
			{
				case AbilityConditionKind.Always:
					return true;
				case AbilityConditionKind.AgainstCreatureTag:
					if (creatureTags == null || string.IsNullOrEmpty(ConditionValue))
					{
						return false;
					}

					foreach (string tag in creatureTags)
					{
						if (string.Equals(tag, ConditionValue, System.StringComparison.OrdinalIgnoreCase))
						{
							return true;
						}
					}

					return false;
				default:
					return false;
			}
		}
	}

	/// <summary>Единица снаряжения на складе смены.</summary>
	public sealed class EquipmentStack
	{
		public EquipmentStack(string definitionId, int quantity, bool isShiftOnly)
		{
			DefinitionId = definitionId;
			Quantity = quantity;
			IsShiftOnly = isShiftOnly;
		}

		public string DefinitionId { get; }

		public int Quantity { get; set; }

		/// <summary>
		/// Найденный по итогам удачной миссии расходник действует только в текущую смену
		/// и не переносится на следующую (ДД, раздел 6).
		/// </summary>
		public bool IsShiftOnly { get; }
	}
}
