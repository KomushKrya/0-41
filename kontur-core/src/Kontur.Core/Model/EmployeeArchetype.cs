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
		/// <summary>
		/// Нижняя граница списка найма и выключатель фабрики: ноль означает, что
		/// кандидатов вообще не генерируем и состав задан контентом.
		/// </summary>
		public int CandidatesPerShift { get; set; }

		/// <summary>
		/// Сколько кандидатов сверх свободных мест. Список найма — это выбор, а не
		/// раздача: потеряв четверых, игрок должен добрать штат полностью, но при
		/// этом от кого-то отказаться.
		/// </summary>
		public int CandidatesChoiceMargin { get; set; } = 2;

		/// <summary>
		/// На сколько уровней кандидаты отстают от номера смены. Единица: на четвёртой
		/// смене приходят третьеуровневые, пока свои дорастают до третьего-четвёртого.
		/// </summary>
		public int LevelLagBehindDay { get; set; } = 1;

		/// <summary>
		/// Разброс уровней внутри одной пачки. Единица: рядом стоят второй и третий,
		/// и видно, что кандидаты не одинаковые.
		/// </summary>
		public int LevelSpread { get; set; } = 1;

		/// <summary>Границы возраста кандидата. На механику не влияет, только на досье.</summary>
		public int MinAge { get; set; } = 24;

		public int MaxAge { get; set; } = 52;

		/// <summary>
		/// Слоты досье в том порядке, в каком фразы встают в анкету. Сами фразы живут
		/// в текстовом движке (`content/raw/<локаль>/personnel/bio/<слот>/`), здесь
		/// только перечень слотов.
		/// </summary>
		public List<string> BioSlots { get; } = new List<string>();

		/// <summary>
		/// Идентификаторы фраз по слотам. Заполняет ContentLoader из текстового движка
		/// при загрузке: у него есть каталог, а фабрика работает уже с готовым списком
		/// и на движок не смотрит.
		/// </summary>
		public Dictionary<string, List<string>> BioLinesBySlot { get; } =
			new Dictionary<string, List<string>>();

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

		/// <summary>
		/// Сколько кандидатов показать при стартовом выборе состава.
		/// 0 — выбора нет, штат берётся из startingRoster как есть.
		///
		/// Больше, чем мест в штате: смысл выбора в том, чтобы от кого-то отказаться.
		/// </summary>
		public int StartingChoicePoolSize { get; set; }

		/// <summary>Сколько перков у кандидата первого уровня.</summary>
		public int AbilitiesBase { get; set; } = 1;

		/// <summary>С какого уровня выдаётся второй перк. 0 — никогда.</summary>
		public int SecondAbilityFromLevel { get; set; } = 3;

		public bool IsEnabled
		{
			get { return CandidatesPerShift > 0 && Archetypes.Count > 0 && Surnames.Count > 0; }
		}

		/// <summary>Диапазон уровней для дня: последняя подходящая запись побеждает.</summary>
		/// <summary>
		/// Уровень кандидатов на этот день.
		///
		/// Правило: **на ступень ниже номера смены**. К четвёртой смене игрок доводит
		/// своих до третьего уровня, и кандидат второго-третьего читается как «крепкий,
		/// но не ровня ветерану». Отставание нужно, чтобы найм не обесценивал выживших:
		/// человек, которого вели через три смены, должен быть лучше того, кто пришёл
		/// с улицы.
		///
		/// Правило, а не таблица, по той же причине, что и лимит штата: таблица
		/// кончается, а смены — нет. Непустой levelsByDay правило перебивает.
		/// </summary>
		public LevelRange GetLevelRange(int day)
		{
			if (LevelsByDay.Count > 0)
			{
				LevelRange found = new LevelRange();
				for (int i = 0; i < LevelsByDay.Count; i++)
				{
					if (LevelsByDay[i].FromDay <= day)
					{
						found = LevelsByDay[i];
					}
				}

				return found;
			}

			int max = day - LevelLagBehindDay;
			if (max < 1)
			{
				max = 1;
			}

			int min = max - LevelSpread;
			if (min < 1)
			{
				min = 1;
			}

			return new LevelRange { FromDay = day, MinLevel = min, MaxLevel = max };
		}
	}
}
