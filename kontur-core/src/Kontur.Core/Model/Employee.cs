using System.Collections.Generic;

namespace Kontur.Core.Model
{
	public enum EmployeeStatus
	{
		/// <summary>В управлении, доступен для отправки.</summary>
		Available = 0,

		/// <summary>В пути или на объекте.</summary>
		OnMission = 1,

		/// <summary>Погиб — теряется безвозвратно (ДД, раздел 5).</summary>
		Dead = 2
	}

	public sealed class Employee
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		/// <summary>Звание = уровень 1–10 (ДД, раздел 5).</summary>
		public int Level { get; set; } = 1;

		public string RankTitle { get; set; } = string.Empty;

		public string PortraitId { get; set; } = string.Empty;

		/// <summary>Возраст и кусочки досье — данные для UI, не для боевого расчёта.</summary>
		public int Age { get; set; }
		public string ArchetypeId { get; set; } = string.Empty;
		public List<string> BioIds { get; } = new List<string>();

		public StatBlock BaseStats { get; set; } = StatBlock.Zero;

		public List<string> AbilityIds { get; } = new List<string>();

		public int Experience { get; set; }

		/// <summary>Нераспределённые очки навыков (по 3 за уровень).</summary>
		public int UnspentSkillPoints { get; set; }

		public EmployeeStatus Status { get; set; } = EmployeeStatus.Available;

		/// <summary>Травма — дебафф до конца текущей смены (ДД, раздел 5).</summary>
		public bool IsInjured { get; set; }

		/// <summary>Инцидент, на котором сотрудник занят сейчас (null — свободен).</summary>
		public string? CurrentIncidentId { get; set; }

		public bool IsAlive
		{
			get { return Status != EmployeeStatus.Dead; }
		}

		public bool IsAvailableForDispatch
		{
			get { return Status == EmployeeStatus.Available; }
		}

		/// <summary>Характеристики с учётом травмы, но без учёта снаряжения и способностей.</summary>
		public StatBlock GetEffectiveStats(int injuryPenaltyPerStat)
		{
			if (!IsInjured || injuryPenaltyPerStat <= 0)
			{
				return BaseStats;
			}

			return BaseStats.Add(StatBlock.Uniform(-injuryPenaltyPerStat)).ClampMin(0);
		}

		public Employee Clone()
		{
			var copy = new Employee
			{
				Id = Id,
				Name = Name,
				Level = Level,
				RankTitle = RankTitle,
				PortraitId = PortraitId,
				Age = Age,
				ArchetypeId = ArchetypeId,
				BaseStats = BaseStats,
				Experience = Experience,
				UnspentSkillPoints = UnspentSkillPoints,
				Status = Status,
				IsInjured = IsInjured,
				CurrentIncidentId = CurrentIncidentId
			};

			copy.AbilityIds.AddRange(AbilityIds);
			copy.BioIds.AddRange(BioIds);
			return copy;
		}
	}
}
