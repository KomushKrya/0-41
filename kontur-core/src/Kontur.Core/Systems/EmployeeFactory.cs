using System;
using System.Collections.Generic;
using Kontur.Core.Content;
using Kontur.Core.Model;
using Kontur.Core.Simulation;

namespace Kontur.Core.Systems
{
	/// <summary>Детерминированная фабрика кандидатов из архетипов и бюджета характеристик.</summary>
	public sealed class EmployeeFactory
	{
		private readonly ContentDatabase _content;
		private readonly IRandomSource _random;
		public EmployeeFactory(ContentDatabase content, IRandomSource random) { _content = content; _random = random; }

		public IReadOnlyList<HireCandidate> Generate(int day, int count, IReadOnlyCollection<string> takenNames, IReadOnlyCollection<string> takenIds, IReadOnlyCollection<string>? takenPortraits = null)
		{
			var result = new List<HireCandidate>();
			EmployeeGeneratorSettings settings = _content.Generator;
			if (!settings.IsEnabled || count <= 0) return result;
			var names = new HashSet<string>(takenNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			var ids = new HashSet<string>(takenIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			var portraits = new HashSet<string>(takenPortraits ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < count; i++)
			{
				EmployeeArchetype archetype = PickArchetype(settings);
				int level = PickLevel(settings, day);
				var employee = new Employee {
					Id = MakeId(day, ids), Name = MakeName(settings, names), Level = level,
					RankTitle = archetype.RankTitle, ArchetypeId = archetype.Id,
					PortraitId = PickPortrait(settings, archetype, portraits), Age = PickAge(settings),
					BaseStats = RollStats(settings, archetype, level)
				};
				employee.AbilityIds.AddRange(PickAbilities(settings, archetype, level));
				employee.BioIds.AddRange(PickBio(settings));
				names.Add(employee.Name); ids.Add(employee.Id); if (employee.PortraitId.Length > 0) portraits.Add(employee.PortraitId);
				result.Add(new HireCandidate(employee, day));
			}
			return result;
		}

		private EmployeeArchetype PickArchetype(EmployeeGeneratorSettings s) { var weights = new List<double>(); foreach (EmployeeArchetype a in s.Archetypes) weights.Add(a.Weight); return s.Archetypes[_random.PickWeightedIndex(weights)]; }
		private int PickLevel(EmployeeGeneratorSettings s, int day) { LevelRange range = s.GetLevelRange(day); int min = Math.Max(1, range.MinLevel); int max = Math.Max(min, range.MaxLevel); return min == max ? min : _random.NextInt(min, max + 1); }
		private int PickAge(EmployeeGeneratorSettings s) { int min = Math.Max(1, s.MinAge); int max = Math.Max(min, s.MaxAge); return min == max ? min : _random.NextInt(min, max + 1); }
		private static string MakeId(int day, HashSet<string> used) { for (int i = 1;; i++) { string id = $"emp_gen_{day}_{i}"; if (!used.Contains(id)) return id; } }
		private string MakeName(EmployeeGeneratorSettings s, HashSet<string> used)
		{
			string fallback = s.Surnames[0];
			for (int attempt = 0; attempt < 24; attempt++) { string name = _random.Pick(s.Surnames) + (s.Initials.Count > 0 ? " " + _random.Pick(s.Initials) : string.Empty); if (attempt == 0) fallback = name; if (!used.Contains(name)) return name; }
			return fallback;
		}
		private string PickPortrait(EmployeeGeneratorSettings s, EmployeeArchetype a, HashSet<string> used)
		{
			string free = PickFree(a.PortraitIds, used); if (free.Length > 0) return free;
			free = PickFree(s.PortraitIds, used); if (free.Length > 0) return free;
			if (a.PortraitIds.Count > 0) return _random.Pick(a.PortraitIds);
			return s.PortraitIds.Count > 0 ? _random.Pick(s.PortraitIds) : string.Empty;
		}
		private string PickFree(IReadOnlyList<string> source, HashSet<string> used) { var free = new List<string>(); foreach (string item in source) if (!used.Contains(item)) free.Add(item); return free.Count > 0 ? _random.Pick(free) : string.Empty; }
		private List<string> PickBio(EmployeeGeneratorSettings s) { var result = new List<string>(); foreach (string slot in s.BioSlots) if (s.BioLinesBySlot.TryGetValue(slot, out List<string>? lines) && lines.Count > 0) result.Add(_random.Pick(lines)); return result; }

		private StatBlock RollStats(EmployeeGeneratorSettings s, EmployeeArchetype a, int level)
		{
			StatBlock stats = StatBlock.Uniform(Math.Max(0, s.MinStat));
			int budget = s.StatPointsBase + s.StatPointsPerLevel * (level - 1);
			double[] weights = BuildWeights(s, a);
			for (int point = 0; point < budget; point++)
			{
				var pool = new List<StatKind>(); var poolWeights = new List<double>();
				foreach (StatKind kind in StatKinds.All) if (stats[kind] < s.MaxStat) { pool.Add(kind); poolWeights.Add(weights[(int)kind]); }
				if (pool.Count == 0) break;
				stats = stats.Add(pool[_random.PickWeightedIndex(poolWeights)], 1);
			}
			return stats;
		}
		private static double[] BuildWeights(EmployeeGeneratorSettings s, EmployeeArchetype a)
		{
			var weights = new double[StatKinds.Count]; for (int i = 0; i < weights.Length; i++) weights[i] = 1.0;
			foreach (StatKind kind in a.SecondaryStats) weights[(int)kind] = Math.Max(0, s.SecondaryWeight);
			foreach (StatKind kind in a.PrimaryStats) weights[(int)kind] = Math.Max(0, s.PrimaryWeight);
			return weights;
		}
		private IReadOnlyList<string> PickAbilities(EmployeeGeneratorSettings s, EmployeeArchetype a, int level)
		{
			var result = new List<string>(); var available = new List<string>(a.AbilityIds);
			int wanted = Math.Max(0, s.AbilitiesBase) + (s.SecondAbilityFromLevel > 0 && level >= s.SecondAbilityFromLevel ? 1 : 0);
			for (int i = 0; i < wanted && available.Count > 0; i++) { int index = _random.NextInt(0, available.Count); result.Add(available[index]); available.RemoveAt(index); }
			return result;
		}
	}
}
