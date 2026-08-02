using System;
using System.Collections.Generic;
using Kontur.Core.Content;
using Kontur.Core.Model;
using Kontur.Core.Simulation;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Сборка кандидатов на найм из архетипов, бюджета характеристик и пула перков.
	///
	/// Зачем фабрика вместо готового списка в контенте: список конечен, а смен много.
	/// Прописанные вручную кандидаты остаются — под них удобно писать сюжетных персонажей,
	/// — но заполнять ими каждую смену не нужно.
	///
	/// Детерминированность держится на общем IRandomSource: один сид плюс одна
	/// последовательность действий игрока дают тот же набор кандидатов. Поэтому здесь
	/// нельзя обращаться ни к системным часам, ни к Random без сида.
	/// </summary>
	public sealed class EmployeeFactory
	{
		/// <summary>
		/// Сколько раз пробуем перекинуть имя, если такое уже есть в штате.
		/// Ограничение нужно, чтобы маленький словарь имён не подвесил генерацию.
		/// </summary>
		private const int NameAttempts = 24;

		private readonly ContentDatabase _content;
		private readonly IRandomSource _random;

		public EmployeeFactory(ContentDatabase content, IRandomSource random)
		{
			_content = content ?? throw new ArgumentNullException(nameof(content));
			_random = random ?? throw new ArgumentNullException(nameof(random));
		}

		/// <summary>
		/// Кандидаты на указанный день. <paramref name="takenNames"/> — имена, которых
		/// быть не должно: обычно текущий штат, чтобы в конторе не завелось двух Гориных.
		/// </summary>
		public IReadOnlyList<HireCandidate> Generate(
			int day,
			int count,
			IReadOnlyCollection<string> takenNames,
			IReadOnlyCollection<string> takenIds,
			IReadOnlyCollection<string>? takenPortraits = null)
		{
			var result = new List<HireCandidate>();
			EmployeeGeneratorSettings settings = _content.Generator;

			if (!settings.IsEnabled || count <= 0)
			{
				return result;
			}

			var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (takenNames != null)
			{
				foreach (string name in takenNames)
				{
					usedNames.Add(name);
				}
			}

			var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (takenIds != null)
			{
				foreach (string id in takenIds)
				{
					usedIds.Add(id);
				}
			}

			// Портреты не повторяются: два одинаковых лица на одном экране игрок
			// читает как ошибку игры, а не как совпадение.
			var usedPortraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (takenPortraits != null)
			{
				foreach (string portrait in takenPortraits)
				{
					usedPortraits.Add(portrait);
				}
			}

			for (int i = 0; i < count; i++)
			{
				Employee employee = BuildOne(settings, day, usedNames, usedIds, usedPortraits);
				usedNames.Add(employee.Name);
				usedIds.Add(employee.Id);
				if (employee.PortraitId.Length > 0)
				{
					usedPortraits.Add(employee.PortraitId);
				}

				result.Add(new HireCandidate(employee, day));
			}

			return result;
		}

		private Employee BuildOne(
			EmployeeGeneratorSettings settings,
			int day,
			HashSet<string> usedNames,
			HashSet<string> usedIds,
			HashSet<string> usedPortraits)
		{
			EmployeeArchetype archetype = PickArchetype(settings);
			int level = PickLevel(settings, day);

			var employee = new Employee
			{
				Id = MakeId(day, usedIds),
				Name = MakeName(settings, usedNames),
				Level = level,
				RankTitle = archetype.RankTitle,
				ArchetypeId = archetype.Id,
				PortraitId = PickPortrait(settings, archetype, usedPortraits),
				Age = PickAge(settings),
				BaseStats = RollStats(settings, archetype, level)
			};

			employee.AbilityIds.AddRange(PickAbilities(settings, archetype, level));
			employee.BioIds.AddRange(PickBio(settings));
			return employee;
		}

		/// <summary>Возраст — только для досье, на расчёты не влияет.</summary>
		private int PickAge(EmployeeGeneratorSettings settings)
		{
			int min = settings.MinAge < 1 ? 1 : settings.MinAge;
			int max = settings.MaxAge < min ? min : settings.MaxAge;
			return min == max ? min : _random.NextInt(min, max + 1);
		}

		/// <summary>
		/// По одной фразе на слот, в порядке слотов. Пустой слот пропускается молча:
		/// без текстового движка (консольный прогон) досье просто не собирается,
		/// и это не повод ронять генерацию.
		/// </summary>
		private List<string> PickBio(EmployeeGeneratorSettings settings)
		{
			var result = new List<string>();

			for (int i = 0; i < settings.BioSlots.Count; i++)
			{
				List<string>? lines;
				if (!settings.BioLinesBySlot.TryGetValue(settings.BioSlots[i], out lines)
					|| lines == null
					|| lines.Count == 0)
				{
					continue;
				}

				result.Add(_random.Pick(lines));
			}

			return result;
		}

		private EmployeeArchetype PickArchetype(EmployeeGeneratorSettings settings)
		{
			var weights = new List<double>(settings.Archetypes.Count);
			for (int i = 0; i < settings.Archetypes.Count; i++)
			{
				weights.Add(settings.Archetypes[i].Weight);
			}

			return settings.Archetypes[_random.PickWeightedIndex(weights)];
		}

		private int PickLevel(EmployeeGeneratorSettings settings, int day)
		{
			LevelRange range = settings.GetLevelRange(day);
			int min = Math.Max(1, range.MinLevel);
			int max = Math.Max(min, range.MaxLevel);
			return min == max ? min : _random.NextInt(min, max + 1);
		}

		/// <summary>
		/// Идентификатор кандидата. Привязан ко дню и порядковому номеру, потому что
		/// по нему отслеживается «этого уже нанимали» — id обязан быть уникальным
		/// за всю партию, а не только внутри одной пачки.
		/// </summary>
		private string MakeId(int day, HashSet<string> usedIds)
		{
			for (int index = 1; ; index++)
			{
				string candidate = "emp_gen_" + day.ToString() + "_" + index.ToString();
				if (!usedIds.Contains(candidate))
				{
					return candidate;
				}
			}
		}

		private string MakeName(EmployeeGeneratorSettings settings, HashSet<string> usedNames)
		{
			string fallback = string.Empty;

			for (int attempt = 0; attempt < NameAttempts; attempt++)
			{
				string surname = _random.Pick(settings.Surnames);
				string initials = settings.Initials.Count > 0 ? _random.Pick(settings.Initials) : string.Empty;
				string name = initials.Length == 0 ? surname : surname + " " + initials;

				if (attempt == 0)
				{
					fallback = name;
				}

				if (!usedNames.Contains(name))
				{
					return name;
				}
			}

			// Словарь имён исчерпан. Повтор фамилии лучше, чем зависший найм;
			// заметно это станет сразу, и автор просто дополнит списки в employees.json.
			return fallback;
		}

		/// <summary>
		/// Свободный портрет. Сначала из набора архетипа, потом из общего пула.
		///
		/// Если свободных не осталось, возвращается занятый: генерация не должна падать
		/// из-за нехватки картинок. Что их хватает, проверяет загрузчик при старте —
		/// там об этом можно сказать понятно, а не показывать близнецов посреди смены.
		/// </summary>
		private string PickPortrait(
			EmployeeGeneratorSettings settings,
			EmployeeArchetype archetype,
			HashSet<string> usedPortraits)
		{
			string picked = PickFree(archetype.PortraitIds, usedPortraits);
			if (picked.Length > 0)
			{
				return picked;
			}

			picked = PickFree(settings.PortraitIds, usedPortraits);
			if (picked.Length > 0)
			{
				return picked;
			}

			if (archetype.PortraitIds.Count > 0)
			{
				return _random.Pick(archetype.PortraitIds);
			}

			return settings.PortraitIds.Count > 0 ? _random.Pick(settings.PortraitIds) : string.Empty;
		}

		private string PickFree(IReadOnlyList<string> pool, HashSet<string> used)
		{
			var free = new List<string>();
			for (int i = 0; i < pool.Count; i++)
			{
				if (!used.Contains(pool[i]))
				{
					free.Add(pool[i]);
				}
			}

			return free.Count > 0 ? _random.Pick(free) : string.Empty;
		}

		/// <summary>
		/// Распределение бюджета. Все пять характеристик начинают с MinStat, дальше очки
		/// раздаются по одному: каждое достаётся случайной характеристике, но основные
		/// архетипа тянут его в PrimaryWeight раз чаще, второстепенные — в SecondaryWeight.
		///
		/// Вес на характеристику, а не доля на пул — важная деталь. При долях архетип
		/// с двумя основными и одной второстепенной получал бы самой высокой именно
		/// второстепенную: её доля не делится ни с кем. Силуэт выходил бы обратный
		/// задуманному, причём незаметно.
		///
		/// Раздача по одному очку решает и вторую задачу — потолок. Упёршаяся
		/// характеристика просто выбывает из розыгрыша, и очко достаётся другой,
		/// а не пропадает. Поэтому сумма характеристик кандидата всегда ровно
		/// предсказуема: MinStat×5 + бюджет.
		/// </summary>
		private StatBlock RollStats(EmployeeGeneratorSettings settings, EmployeeArchetype archetype, int level)
		{
			StatBlock stats = StatBlock.Uniform(Math.Max(0, settings.MinStat));

			int budget = settings.StatPointsBase + (settings.StatPointsPerLevel * (level - 1));
			if (budget <= 0)
			{
				return stats;
			}

			double[] weights = BuildWeights(settings, archetype);
			var pool = new List<StatKind>(StatKinds.Count);
			var poolWeights = new List<double>(StatKinds.Count);

			for (int point = 0; point < budget; point++)
			{
				pool.Clear();
				poolWeights.Clear();

				for (int i = 0; i < StatKinds.All.Length; i++)
				{
					StatKind kind = StatKinds.All[i];
					if (stats[kind] < settings.MaxStat)
					{
						pool.Add(kind);
						poolWeights.Add(weights[(int)kind]);
					}
				}

				if (pool.Count == 0)
				{
					// Бюджет не помещается в характеристики. ContentLoader такое ловит
					// при загрузке, так что сюда попасть можно только с невалидным контентом.
					break;
				}

				stats = stats.Add(pool[_random.PickWeightedIndex(poolWeights)], 1);
			}

			return stats;
		}

		private static double[] BuildWeights(EmployeeGeneratorSettings settings, EmployeeArchetype archetype)
		{
			var weights = new double[StatKinds.Count];
			for (int i = 0; i < weights.Length; i++)
			{
				weights[i] = 1.0;
			}

			// Второстепенные раньше основных: если характеристика указана и там, и там,
			// побеждает основная, а не порядок перебора.
			for (int i = 0; i < archetype.SecondaryStats.Count; i++)
			{
				weights[(int)archetype.SecondaryStats[i]] = Math.Max(0.0, settings.SecondaryWeight);
			}

			for (int i = 0; i < archetype.PrimaryStats.Count; i++)
			{
				weights[(int)archetype.PrimaryStats[i]] = Math.Max(0.0, settings.PrimaryWeight);
			}

			return weights;
		}

		/// <summary>
		/// Перки берутся только из пула архетипа: у каждого перка есть текст и игровой эффект,
		/// придумать их на лету нельзя. Разнообразие даёт сочетание, а не новые способности.
		/// </summary>
		private IReadOnlyList<string> PickAbilities(
			EmployeeGeneratorSettings settings,
			EmployeeArchetype archetype,
			int level)
		{
			var result = new List<string>();
			if (archetype.AbilityIds.Count == 0)
			{
				return result;
			}

			int wanted = Math.Max(0, settings.AbilitiesBase);
			if (settings.SecondAbilityFromLevel > 0 && level >= settings.SecondAbilityFromLevel)
			{
				wanted++;
			}

			if (wanted > archetype.AbilityIds.Count)
			{
				wanted = archetype.AbilityIds.Count;
			}

			var available = new List<string>(archetype.AbilityIds);
			for (int i = 0; i < wanted; i++)
			{
				int index = _random.NextInt(0, available.Count);
				result.Add(available[index]);
				available.RemoveAt(index);
			}

			return result;
		}
	}
}
