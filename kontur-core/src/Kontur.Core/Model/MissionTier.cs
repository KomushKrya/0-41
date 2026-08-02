namespace Kontur.Core.Model
{
	/// <summary>
	/// Место вызова в структуре смены.
	///
	/// Это не «сложность» и не «важность для баланса», а обещание игроку: сюжетный вызов
	/// может стоить сотрудника, филлерный — нет. Игрок должен уметь читать ставки заранее,
	/// иначе каждый рядовой выезд превращается в лотерею, а осторожность перестаёт окупаться.
	/// </summary>
	public enum MissionTier
	{
		/// <summary>
		/// Фон смены. Держит ритм и тратит ресурс группы, но никого не убивает.
		/// Вмешательства по радио не имеет — треск радио должен означать «дело серьёзное».
		/// </summary>
		Filler = 0,

		/// <summary>Сюжетный вызов: полные ставки, вмешательство по радио, гибель возможна.</summary>
		Story = 1
	}

	/// <summary>
	/// Потолок последствий: что максимум может случиться с оперативником на этом вызове.
	///
	/// Именно потолок, а не вероятность. Шансы гибели и травмы остаются в миссии числами,
	/// а потолок их обрезает — так «в детском саду не умирают» становится проверяемым
	/// правилом, а не договорённостью не писать туда deathChance больше нуля.
	/// </summary>
	public enum ConsequenceCap
	{
		/// <summary>Ни травм, ни гибели. Выезд не может стоить ничего, кроме шкал.</summary>
		None = 0,

		/// <summary>Травмы возможны, гибель исключена.</summary>
		Injury = 1,

		/// <summary>Полные ставки.</summary>
		Death = 2
	}

	public static class ConsequenceCaps
	{
		/// <summary>Потолок по умолчанию для уровня миссии, если в контенте не задан явно.</summary>
		public static ConsequenceCap DefaultFor(MissionTier tier)
		{
			return tier == MissionTier.Story ? ConsequenceCap.Death : ConsequenceCap.Injury;
		}

		/// <summary>
		/// Более строгий из двух. Вариант решения умеет только ужесточать: «спрятаться
		/// в кладовке» безопасно везде, но никакой вариант не сделает детский сад смертельным.
		/// </summary>
		public static ConsequenceCap Tighten(ConsequenceCap left, ConsequenceCap right)
		{
			return left < right ? left : right;
		}

		public static bool AllowsDeath(ConsequenceCap cap)
		{
			return cap == ConsequenceCap.Death;
		}

		public static bool AllowsInjury(ConsequenceCap cap)
		{
			return cap != ConsequenceCap.None;
		}
	}
}
