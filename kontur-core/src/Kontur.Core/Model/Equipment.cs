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

		/// <summary>Бонус действует на всю отправленную группу целиком (ДД, раздел 6).</summary>
		public StatBlock Bonus { get; set; } = StatBlock.Zero;

		public int AllStatsBonus { get; set; }

		/// <summary>Прямая прибавка к шансу успеха при броске кубика, 0..1.</summary>
		public double SuccessChanceBonus { get; set; }

		/// <summary>Множитель шанса гибели сотрудника (например, 0.5 для брони).</summary>
		public double DeathChanceMultiplier { get; set; } = 1.0;

		public StatBlock GetEffectiveBonus()
		{
			return AllStatsBonus == 0 ? Bonus : Bonus.Add(StatBlock.Uniform(AllStatsBonus));
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
