using System;
using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>Один вариант решения на выезде. Проза хранится в текстовом движке.</summary>
	public sealed class MissionEventOption
	{
		public string Id { get; set; } = string.Empty;
		public int RequirementModifier { get; set; }
		public IReadOnlyList<StatKind> CheckedStats { get; set; } = new List<StatKind>();
		public double DeathChanceMultiplier { get; set; } = 1.0;
		public double InjuryChanceMultiplier { get; set; } = 1.0;
		public ScaleDelta ExtraScales { get; set; } = ScaleDelta.Zero;
		public string? RevealsPropertyId { get; set; }
		public ConsequenceCap? ConsequenceCapOverride { get; set; }
		public string? RequiresEquipmentId { get; set; }

		/// <summary>Подставляет требования миссии только в названные текстом характеристики.</summary>
		public StatBlock ResolveRequirements(StatBlock missionRequirements)
		{
			StatBlock resolved = StatBlock.Zero;
			for (int i = 0; i < CheckedStats.Count; i++)
			{
				StatKind kind = CheckedStats[i];
				resolved = resolved.With(kind, missionRequirements[kind]);
			}
			return resolved;
		}
	}

	/// <summary>Событие по радио. Его id совпадает с записью текстового движка.</summary>
	public sealed class MissionEventDefinition
	{
		public string Id { get; set; } = string.Empty;
		public List<MissionEventOption> Options { get; } = new List<MissionEventOption>();

		public MissionEventOption? FindOption(string optionId)
		{
			for (int i = 0; i < Options.Count; i++)
			{
				if (string.Equals(Options[i].Id, optionId, StringComparison.OrdinalIgnoreCase))
				{
					return Options[i];
				}
			}
			return null;
		}
	}
}
