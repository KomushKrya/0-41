namespace Kontur.Core.Model
{
	/// <summary>Штриховка зоны на карте — механический слой (ДД, раздел 9).</summary>
	public enum ZoneState
	{
		Normal = 0,

		/// <summary>Заражена: вызовы чаще и сложнее.</summary>
		Infected = 1,

		/// <summary>Карантин: вызовы реже.</summary>
		Quarantine = 2,

		/// <summary>Очищена/контролируется: вызовов почти нет.</summary>
		Cleared = 3
	}

	public sealed class Zone
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public ZoneState State { get; set; } = ZoneState.Normal;

		/// <summary>Базовый вес зоны в планировщике вызовов.</summary>
		public double BaseWeight { get; set; } = 1.0;

		/// <summary>Подряд успешных вызовов — накопление ведёт к переходу в Cleared.</summary>
		public int SuccessStreak { get; set; }

		/// <summary>Подряд провалов — накопление ведёт к переходу в Infected.</summary>
		public int FailStreak { get; set; }

		/// <summary>Условная позиция на карте для отрисовки; ядру не нужна, но удобно прокидывать в UI.</summary>
		public double MapX { get; set; }

		public double MapY { get; set; }
	}
}
