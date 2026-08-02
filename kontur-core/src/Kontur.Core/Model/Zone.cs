namespace Kontur.Core.Model
{
	/// <summary>
	/// Район города. Справочные данные: ядро их только читает.
	///
	/// Состояний у района нет намеренно. Штриховка (заражено / карантин / очищено)
	/// была снята: она меняла и частоту вызовов, и пороги, из-за чего провал тянул
	/// за собой ещё более тяжёлые вызовы в том же районе — спираль, из которой
	/// игрок не выбирался. Частота задаётся статично через BaseWeight.
	/// </summary>
	public sealed class Zone
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		/// <summary>Вес района в планировщике вызовов. Задаётся контентом и не меняется.</summary>
		public double BaseWeight { get; set; } = 1.0;

		/// <summary>Условная позиция на карте для отрисовки; ядру не нужна, но удобно прокидывать в UI.</summary>
		public double MapX { get; set; }

		public double MapY { get; set; }
	}
}
