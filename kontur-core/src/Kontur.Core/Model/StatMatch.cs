namespace Kontur.Core.Model
{
	public enum StatMatchRating { Below = 0, Meets = 1, Exceeds = 2 }

	/// <summary>Одна строка оценки отправленной группы по необходимой характеристике.</summary>
	public sealed class StatMatch
	{
		public StatMatch(StatKind stat, int required, int available, bool isPrimary, StatMatchRating rating, double score)
		{
			Stat = stat; Required = required; Available = available; IsPrimary = isPrimary; Rating = rating; Score = score;
		}
		public StatKind Stat { get; }
		public int Required { get; }
		public int Available { get; }
		public bool IsPrimary { get; }
		public StatMatchRating Rating { get; }
		public double Score { get; }
		public int Margin => Available - Required;
		public int Shortfall => Margin < 0 ? -Margin : 0;
	}
}
