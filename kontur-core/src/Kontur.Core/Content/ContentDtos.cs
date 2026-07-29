using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Content
{
	/// <summary>
	/// DTO-слой — ровно то, что лежит в JSON. Отделён от доменной модели намеренно:
	/// схему контента можно менять, не трогая рантайм, и наоборот.
	/// Схему задаёт ядро; текстовый движок (Obsidian .md -> JSON) обязан её соблюдать.
	/// См. docs/CONTENT_SCHEMA.md.
	/// </summary>
	public sealed class StatBlockDto
	{
		public int Strength { get; set; }

		public int Perception { get; set; }

		public int Endurance { get; set; }

		public int Agility { get; set; }

		public int Composure { get; set; }

		public StatBlock ToModel()
		{
			return new StatBlock(Strength, Perception, Endurance, Agility, Composure);
		}
	}

	public sealed class ScaleDeltaDto
	{
		public double Infection { get; set; }

		public double Publicity { get; set; }

		public double Loyalty { get; set; }

		public ScaleDelta ToModel()
		{
			return new ScaleDelta(Infection, Publicity, Loyalty);
		}
	}

	public sealed class ZoneDto
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public string State { get; set; } = "Normal";

		public double BaseWeight { get; set; } = 1.0;

		public double MapX { get; set; }

		public double MapY { get; set; }
	}

	public sealed class AbilityDto
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public string Description { get; set; } = string.Empty;

		public string Condition { get; set; } = "Always";

		public string ConditionValue { get; set; } = string.Empty;

		public StatBlockDto? Bonus { get; set; }

		public int AllStatsBonus { get; set; }
	}

	public sealed class EquipmentDto
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public string Description { get; set; } = string.Empty;

		public string Kind { get; set; } = "Consumable";

		public StatBlockDto? Bonus { get; set; }

		public int AllStatsBonus { get; set; }

		public double SuccessChanceBonus { get; set; }

		public double DeathChanceMultiplier { get; set; } = 1.0;
	}

	public sealed class CreaturePropertyDto
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public int ParagraphIndex { get; set; } = 1;
	}

	public sealed class CreatureDto
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public List<string>? Tags { get; set; }

		public List<string>? Paragraphs { get; set; }

		public List<CreaturePropertyDto>? Properties { get; set; }

		public string IllustrationId { get; set; } = string.Empty;
	}

	public sealed class EmployeeDto
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public int Level { get; set; } = 1;

		public string RankTitle { get; set; } = string.Empty;

		public string PortraitId { get; set; } = string.Empty;

		public StatBlockDto? Stats { get; set; }

		public List<string>? Abilities { get; set; }

		/// <summary>Для кандидатов на найм: с какого дня доступен (ДД, раздел 5).</summary>
		public int AvailableFromDay { get; set; } = 1;
	}

	public sealed class RosterDto
	{
		public List<EmployeeDto>? StartingRoster { get; set; }

		public List<EmployeeDto>? HirePool { get; set; }
	}

	public sealed class RadioOptionDto
	{
		public string Id { get; set; } = string.Empty;

		public string Text { get; set; } = string.Empty;

		public double RequirementMultiplier { get; set; } = 1.0;

		public double DeathChanceMultiplier { get; set; } = 1.0;

		public double InjuryChanceMultiplier { get; set; } = 1.0;

		public bool AppliesQuarantine { get; set; }

		public ScaleDeltaDto? ExtraScales { get; set; }

		public string? RevealsPropertyId { get; set; }

		public string Quality { get; set; } = "Good";
	}

	public sealed class RadioEncounterDto
	{
		public string Id { get; set; } = string.Empty;

		public string SituationText { get; set; } = string.Empty;

		public List<RadioOptionDto>? Options { get; set; }
	}

	public sealed class MissionDto
	{
		public string Id { get; set; } = string.Empty;

		public int Day { get; set; } = 1;

		public string ZoneId { get; set; } = string.Empty;

		public string CreatureId { get; set; } = string.Empty;

		public string Title { get; set; } = string.Empty;

		public string CallerName { get; set; } = string.Empty;

		public string BriefingText { get; set; } = string.Empty;

		public StatBlockDto? Requirements { get; set; }

		public double TravelSeconds { get; set; } = 12.0;

		public double OnSiteSeconds { get; set; } = 6.0;

		public double ReturnSeconds { get; set; } = 10.0;

		public string? RadioEncounterId { get; set; }

		public ScaleDeltaDto? ScalesOnSuccess { get; set; }

		public ScaleDeltaDto? ScalesOnFailure { get; set; }

		public ScaleDeltaDto? ScalesOnMissedCall { get; set; }

		public ScaleDeltaDto? ScalesOnExpiredMarker { get; set; }

		public int ExperienceOnSuccess { get; set; } = 100;

		public int ExperienceOnFailure { get; set; } = 25;

		public double InjuryChance { get; set; } = 0.25;

		public double DeathChance { get; set; } = 0.08;

		public string ReportSuccessText { get; set; } = string.Empty;

		public string ReportFailureText { get; set; } = string.Empty;

		public List<string>? ManifestedPropertyIds { get; set; }
	}

	public sealed class ShiftNoteDto
	{
		public int Day { get; set; } = 1;

		public string Title { get; set; } = string.Empty;

		/// <summary>Флейвор-текст от дневного сменщика (ДД, раздел 2).</summary>
		public string Text { get; set; } = string.Empty;

		/// <summary>Идентификатор пререндеренного ролика после смены.</summary>
		public string OutroCutsceneId { get; set; } = string.Empty;
	}
}
