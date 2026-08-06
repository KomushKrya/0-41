using System.Collections.Generic;

namespace Kontur.Core.Model
{
	public sealed class EmployeeArchetype
	{
		public string Id { get; set; } = string.Empty;
		public double Weight { get; set; } = 1.0;
		public string RankTitle { get; set; } = string.Empty;
		public List<StatKind> PrimaryStats { get; } = new List<StatKind>();
		public List<StatKind> SecondaryStats { get; } = new List<StatKind>();
		public List<string> AbilityIds { get; } = new List<string>();
		public List<string> PortraitIds { get; } = new List<string>();
	}

	public sealed class LevelRange
	{
		public int FromDay { get; set; } = 1;
		public int MinLevel { get; set; } = 1;
		public int MaxLevel { get; set; } = 1;
	}

	public sealed class EmployeeGeneratorSettings
	{
		public int CandidatesPerShift { get; set; }
		public int CandidatesChoiceMargin { get; set; } = 2;

		/// <summary>
		/// Сколько кандидатов показывать за раз на экране найма.
		///
		/// Влияет не только на вёрстку: игрок берёт из тройки одного, остальные
		/// уходят навсегда. Значит на каждое свободное место нужна своя тройка,
		/// и пул кандидатов считается отсюда.
		/// </summary>
		public int CandidatesPerDraw { get; set; } = 3;
		public int LevelLagBehindDay { get; set; } = 1;
		public int LevelSpread { get; set; } = 1;
		public int MinAge { get; set; } = 24;
		public int MaxAge { get; set; } = 52;
		public List<string> BioSlots { get; } = new List<string>();
		public Dictionary<string, List<string>> BioLinesBySlot { get; } = new Dictionary<string, List<string>>();
		public List<string> Surnames { get; } = new List<string>();
		public List<string> Initials { get; } = new List<string>();
		public List<string> PortraitIds { get; } = new List<string>();
		public List<EmployeeArchetype> Archetypes { get; } = new List<EmployeeArchetype>();
		public List<LevelRange> LevelsByDay { get; } = new List<LevelRange>();
		public int MinStat { get; set; } = 2;
		public int MaxStat { get; set; } = 7;
		public int StatPointsBase { get; set; } = 6;
		public int StatPointsPerLevel { get; set; } = 3;
		public double PrimaryWeight { get; set; } = 3.0;
		public double SecondaryWeight { get; set; } = 2.0;
		public int AbilitiesBase { get; set; } = 1;
		public int SecondAbilityFromLevel { get; set; } = 3;
		public int StartingChoicePoolSize { get; set; }

		/// <summary>
		/// Сколько оперативников выдать игроку на старте, если стартовый состав
		/// не расписан руками в контенте.
		///
		/// Меньше потолка первой смены намеренно: у игрока сразу есть свободное
		/// место, и первый же найм он делает по своей воле, а не потому, что
		/// кого-то потерял.
		/// </summary>
		public int StartingRosterSize { get; set; } = 2;
		public bool IsEnabled => CandidatesPerShift > 0 && Archetypes.Count > 0 && Surnames.Count > 0;
		public LevelRange GetLevelRange(int day)
		{
			LevelRange? found = null;
			for (int i = 0; i < LevelsByDay.Count; i++) if (LevelsByDay[i].FromDay <= day) found = LevelsByDay[i];
			if (found != null) return found;
			int max = System.Math.Max(1, day - LevelLagBehindDay);
			return new LevelRange { FromDay = day, MinLevel = System.Math.Max(1, max - LevelSpread), MaxLevel = max };
		}
	}
}
