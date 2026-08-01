using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Заготовка оперативника: во что вкладывать очки и какие перки ему идут.
	///
	/// Без архетипов случайные кандидаты получаются одинаково серыми — у всех
	/// характеристики в середине, и выбирать не из чего. Архетип задаёт узнаваемый силуэт
	/// («здоровяк», «наблюдатель»), а значит, и смысл в самом выборе при найме.
	/// </summary>
	public sealed class EmployeeArchetype
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>Как часто попадается относительно других архетипов.</summary>
		public double Weight { get; set; } = 1.0;

		public string RankTitle { get; set; } = string.Empty;

		/// <summary>Куда уходит основная доля очков.</summary>
		public List<StatKind> PrimaryStats { get; } = new List<StatKind>();

		/// <summary>Куда уходит остаток сверх минимума.</summary>
		public List<StatKind> SecondaryStats { get; } = new List<StatKind>();

		/// <summary>Перки, из которых собирается набор. Пустой — кандидат будет без перков.</summary>
		public List<string> AbilityIds { get; } = new List<string>();

		/// <summary>Портреты, подходящие этому архетипу. Пустой — берётся общий список.</summary>
		public List<string> PortraitIds { get; } = new List<string>();
	}

	/// <summary>Диапазон уровней кандидатов, начиная с указанного дня.</summary>
	public sealed class LevelRange
	{
		public int FromDay { get; set; } = 1;

		public int MinLevel { get; set; } = 1;

		public int MaxLevel { get; set; } = 1;
	}

	/// <summary>
	/// Настройки фабрики кандидатов (раздел «generator» в data/employees.json).
	///
	/// Смысл разделения на бюджет и архетип: бюджет отвечает за то, насколько кандидат
	/// силён вообще (растёт по дням вместе со сложностью), архетип — за то, куда эта сила
	/// вложена. Правится это по отдельности, и балансировать так заметно проще.
	/// </summary>
	public sealed class EmployeeGeneratorSettings
	{
		/// <summary>Сколько кандидатов показывать в меню найма. 0 — фабрика выключена.</summary>
		public int CandidatesPerShift { get; set; }

		public List<string> Surnames { get; } = new List<string>();

		public List<string> Initials { get; } = new List<string>();

		public List<string> PortraitIds { get; } = new List<string>();

		public List<EmployeeArchetype> Archetypes { get; } = new List<EmployeeArchetype>();

		public List<LevelRange> LevelsByDay { get; } = new List<LevelRange>();

		/// <summary>Стартовое значение каждой из пяти характеристик до распределения бюджета.</summary>
		public int MinStat { get; set; } = 2;

		/// <summary>Потолок характеристики при генерации. Дальше можно вырасти только уровнями.</summary>
		public int MaxStat { get; set; } = 7;

		/// <summary>Сколько очков распределяется сверх минимума у кандидата первого уровня.</summary>
		public int StatPointsBase { get; set; } = 6;

		/// <summary>Прибавка к бюджету за каждый уровень сверх первого.</summary>
		public int StatPointsPerLevel { get; set; } = 3;

		/// <summary>
		/// Во сколько раз чаще очко достаётся основной характеристике архетипа.
		/// Вес на характеристику, а не доля на пул: иначе единственная второстепенная
		/// оказывалась бы выше двух основных, между которыми доля делится.
		/// </summary>
		public double PrimaryWeight { get; set; } = 3.0;

		/// <summary>Во сколько раз чаще очко достаётся второстепенной. Прочие весят 1.</summary>
		public double SecondaryWeight { get; set; } = 2.0;

		/// <summary>Сколько перков у кандидата первого уровня.</summary>
		public int AbilitiesBase { get; set; } = 1;

		/// <summary>С какого уровня выдаётся второй перк. 0 — никогда.</summary>
		public int SecondAbilityFromLevel { get; set; } = 3;

		public bool IsEnabled
		{
			get { return CandidatesPerShift > 0 && Archetypes.Count > 0 && Surnames.Count > 0; }
		}

		/// <summary>Диапазон уровней для дня: последняя подходящая запись побеждает.</summary>
		public LevelRange GetLevelRange(int day)
		{
			LevelRange result = new LevelRange();
			for (int i = 0; i < LevelsByDay.Count; i++)
			{
				if (LevelsByDay[i].FromDay <= day)
				{
					result = LevelsByDay[i];
				}
			}

			return result;
		}
	}
}
