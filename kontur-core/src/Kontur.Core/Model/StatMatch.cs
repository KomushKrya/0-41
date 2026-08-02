namespace Kontur.Core.Model
{
	/// <summary>
	/// Насколько группа закрывает порог по одной характеристике.
	/// Три ступени, потому что игрок читает их цветом, а не числом.
	/// </summary>
	public enum StatMatchRating
	{
		/// <summary>Недобор: лучший в группе слабее порога.</summary>
		Below = 0,

		/// <summary>Ровно дотянул или чуть выше — успех вероятен, но с осложнениями.</summary>
		Meets = 1,

		/// <summary>Уверенное превышение. Если так по всем требованиям — успех без броска.</summary>
		Exceeds = 2
	}

	/// <summary>
	/// Одна строка экрана отправки: что требуется, кто в группе это закрывает и насколько.
	///
	/// Сравнивается **сумма отряда**: порог «Восприятие 8» закрывают двое по четыре или
	/// у кого восприятие не ниже шести. Трое невнимательных не заменяют одного глазастого —
	/// именно поэтому состав собирают под требования, а не по принципу «отправить всех».
	/// </summary>
	public sealed class StatMatch
	{
		public StatMatch(StatKind stat, int required, int best, bool isPrimary, StatMatchRating rating, double score)
		{
			Stat = stat;
			Required = required;
			Best = best;
			IsPrimary = isPrimary;
			Rating = rating;
			Score = score;
		}

		public StatKind Stat { get; }

		public int Required { get; }

		/// <summary>Лучшее значение в группе с учётом перков и снаряжения.</summary>
		public int Best { get; }

		/// <summary>Главная характеристика вызова — весит вдвое.</summary>
		public bool IsPrimary { get; }

		public StatMatchRating Rating { get; }

		/// <summary>Вклад в итоговый процент, 0..1.</summary>
		public double Score { get; }

		public int Margin
		{
			get { return Best - Required; }
		}

		/// <summary>Сколько не хватает. Ноль, если порог закрыт.</summary>
		public int Shortfall
		{
			get { return Margin < 0 ? -Margin : 0; }
		}
	}
}
