using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Content
{
	/// <summary>
	/// DTO-РЎРѓР В»Р С•Р в„– РІР‚вЂќ РЎР‚Р С•Р Р†Р Р…Р С• РЎвЂљР С•, РЎвЂЎРЎвЂљР С• Р В»Р ВµР В¶Р С‘РЎвЂљ Р Р† JSON. Р С›РЎвЂљР Т‘Р ВµР В»РЎвЂР Р… Р С•РЎвЂљ Р Т‘Р С•Р СР ВµР Р…Р Р…Р С•Р в„– Р СР С•Р Т‘Р ВµР В»Р С‘ Р Р…Р В°Р СР ВµРЎР‚Р ВµР Р…Р Р…Р С•:
	/// РЎРѓРЎвЂ¦Р ВµР СРЎС“ Р С”Р С•Р Р…РЎвЂљР ВµР Р…РЎвЂљР В° Р СР С•Р В¶Р Р…Р С• Р СР ВµР Р…РЎРЏРЎвЂљРЎРЉ, Р Р…Р Вµ РЎвЂљРЎР‚Р С•Р С–Р В°РЎРЏ РЎР‚Р В°Р Р…РЎвЂљР В°Р в„–Р С, Р С‘ Р Р…Р В°Р С•Р В±Р С•РЎР‚Р С•РЎвЂљ.
	/// Р РЋРЎвЂ¦Р ВµР СРЎС“ Р В·Р В°Р Т‘Р В°РЎвЂРЎвЂљ РЎРЏР Т‘РЎР‚Р С•; РЎвЂљР ВµР С”РЎРѓРЎвЂљР С•Р Р†РЎвЂ№Р в„– Р Т‘Р Р†Р С‘Р В¶Р С•Р С” (Obsidian .md -> JSON) Р С•Р В±РЎРЏР В·Р В°Р Р… Р ВµРЎвЂ РЎРѓР С•Р В±Р В»РЎР‹Р Т‘Р В°РЎвЂљРЎРЉ.
	/// Р РЋР С. docs/CONTENT_SCHEMA.md.
	/// </summary>
	public sealed class StatBlockDto
	{
		public int Strength { get; set; }

		public int Intellect { get; set; }

		public int Combat { get; set; }

		public int Agility { get; set; }

		public int Charisma { get; set; }

		public StatBlock ToModel()
		{
			return new StatBlock(Strength, Intellect, Combat, Agility, Charisma);
		}
	}

	public sealed class ScaleDeltaDto	{
		public double Infection { get; set; }

		public double Publicity { get; set; }

		public double Loyalty { get; set; }

		public ScaleDelta ToModel()
		{
			return new ScaleDelta(Infection, Publicity, Loyalty);
		}
	}

	public sealed class BuildingDto
	{
		public string Id { get; set; } = string.Empty;

		public bool IsDispatchTarget { get; set; }

		public bool IsHeadquarters { get; set; }
	}

	public sealed class AbilityDto
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>Р СњР В°Р В·Р Р†Р В°Р Р…Р С‘Р Вµ Р С‘ Р С•Р С—Р С‘РЎРѓР В°Р Р…Р С‘Р Вµ РІР‚вЂќ Р Р† РЎвЂљР ВµР С”РЎРѓРЎвЂљР С•Р Р†Р С•Р С Р Т‘Р Р†Р С‘Р В¶Р С”Р Вµ, Р В·Р Т‘Р ВµРЎРѓРЎРЉ РЎвЂљР С•Р В»РЎРЉР С”Р С• Р СР ВµРЎвЂ¦Р В°Р Р…Р С‘Р С”Р В°.</summary>
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

		/// <summary>Название и описание — в текстовом движке, здесь только механика.</summary>
		public string Condition { get; set; } = "Always";

		public string ConditionValue { get; set; } = string.Empty;

		public StatBlockDto? Bonus { get; set; }

		public int AllStatsBonus { get; set; }
	}

	public sealed class CreatureDto
	{
		public string Id { get; set; } = string.Empty;

		public List<string>? Tags { get; set; }

		/// <summary>Р СћР С•Р В»РЎРЉР С”Р С• id РЎРѓР Р†Р С•Р в„–РЎРѓРЎвЂљР Р† РІР‚вЂќ Р В°Р В±Р В·Р В°РЎвЂ РЎвЂ№ Р С—Р С•Р Т‘ Р Р…Р С‘РЎвЂ¦ Р В»Р ВµР В¶Р В°РЎвЂљ Р Р† РЎвЂљР ВµР С”РЎРѓРЎвЂљР С•Р Р†Р С•Р С Р Т‘Р Р†Р С‘Р В¶Р С”Р Вµ.</summary>
		public List<string>? Properties { get; set; }

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

		/// <summary>Возраст. Ноль означает «не указан» — тогда досье не покажет строку.</summary>
		public int Age { get; set; }

		/// <summary>
		/// Id строк досье, по одной на слот генератора.
		///
		/// Прописанным сотрудникам их задают руками: фабрика тасует био только у
		/// тех, кого сама и создала, а Горин с Васнецовой существуют до неё.
		/// </summary>
		public List<string>? Bio { get; set; }

		/// <summary>Р вЂќР В»РЎРЏ Р С”Р В°Р Р…Р Т‘Р С‘Р Т‘Р В°РЎвЂљР С•Р Р† Р Р…Р В° Р Р…Р В°Р в„–Р С: РЎРѓ Р С”Р В°Р С”Р С•Р С–Р С• Р Т‘Р Р…РЎРЏ Р Т‘Р С•РЎРѓРЎвЂљРЎС“Р С—Р ВµР Р… (Р вЂќР вЂќ, РЎР‚Р В°Р В·Р Т‘Р ВµР В» 5).</summary>
		public int AvailableFromDay { get; set; } = 1;
	}

	public sealed class RosterDto
	{
		public List<EmployeeDto>? StartingRoster { get; set; }

		public List<EmployeeDto>? HirePool { get; set; }

		public GeneratorDto? Generator { get; set; }
	}

	public sealed class GeneratorDto
	{
		public int CandidatesPerShift { get; set; }
		public int CandidatesChoiceMargin { get; set; } = 2;
		public int LevelLagBehindDay { get; set; } = 1;
		public int LevelSpread { get; set; } = 1;
		public int MinAge { get; set; } = 24;
		public int MaxAge { get; set; } = 52;
		public List<string>? BioSlots { get; set; }
		public List<string>? Surnames { get; set; }
		public List<string>? Initials { get; set; }
		public List<string>? Portraits { get; set; }
		public List<ArchetypeDto>? Archetypes { get; set; }
		public List<LevelRangeDto>? LevelsByDay { get; set; }
		public int MinStat { get; set; } = 2;
		public int MaxStat { get; set; } = 7;
		public int StatPointsBase { get; set; } = 6;
		public int StatPointsPerLevel { get; set; } = 3;
		public double PrimaryWeight { get; set; } = 3.0;
		public double SecondaryWeight { get; set; } = 2.0;
		public int StartingChoicePoolSize { get; set; }
		public int AbilitiesBase { get; set; } = 1;
		public int SecondAbilityFromLevel { get; set; } = 3;
	}

	public sealed class ArchetypeDto
	{
		public string Id { get; set; } = string.Empty;
		public double Weight { get; set; } = 1.0;
		public string RankTitle { get; set; } = string.Empty;
		public List<string>? Primary { get; set; }
		public List<string>? Secondary { get; set; }
		public List<string>? Abilities { get; set; }
		public List<string>? Portraits { get; set; }
	}

	public sealed class LevelRangeDto
	{
		public int FromDay { get; set; } = 1;
		public int MinLevel { get; set; } = 1;
		public int MaxLevel { get; set; } = 1;
	}

	public sealed class MissionEventOptionDto
	{
		public double? DeathChanceMultiplier { get; set; }
		public double? InjuryChanceMultiplier { get; set; }
		public ScaleDeltaDto? ExtraScales { get; set; }
		public string? RevealsPropertyId { get; set; }
		public string? RequiresEquipmentId { get; set; }
		public string? ConsequenceCap { get; set; }
	}

	/// <summary>Р вЂР В°Р В»Р В°Р Р…РЎРѓ MissionEvent; РЎвЂљР ВµР С”РЎРѓРЎвЂљ Р Р†Р В°РЎР‚Р С‘Р В°Р Р…РЎвЂљР С•Р Р† Р С‘ Р С—Р С•РЎР‚РЎРЏР Т‘Р С•Р С” Р В±Р ВµРЎР‚РЎС“РЎвЂљРЎРѓРЎРЏ Р С—Р С• РЎвЂљР С•Р СРЎС“ Р В¶Р Вµ Id Р С‘Р В· Content.</summary>
	public sealed class MissionEventDto
	{
		public string Id { get; set; } = string.Empty;
		public Dictionary<string, MissionEventOptionDto>? Options { get; set; }
	}

	public sealed class MissionDto
	{
		public string Id { get; set; } = string.Empty;

		public int Day { get; set; } = 1;

		public string Tier { get; set; } = "Filler";
		public string? ConsequenceCap { get; set; }

		public string CreatureId { get; set; } = string.Empty;

		public string CallId { get; set; } = string.Empty;

		public StatBlockDto? Requirements { get; set; }
		public string? PrimaryStat { get; set; }
		public int SquadLimit { get; set; } = 1;

		public double TravelSeconds { get; set; } = 12.0;

		public double OnSiteSeconds { get; set; } = 6.0;

		public double ReturnSeconds { get; set; } = 10.0;

		public string? MissionEventId { get; set; }

		public ScaleDeltaDto? ScalesOnSuccess { get; set; }

		public ScaleDeltaDto? ScalesOnFailure { get; set; }

		public ScaleDeltaDto? ScalesOnMissedCall { get; set; }

		public ScaleDeltaDto? ScalesOnExpiredMarker { get; set; }

		public int ExperienceOnSuccess { get; set; } = 100;

		public int ExperienceOnFailure { get; set; } = 25;

		public double InjuryChance { get; set; } = 0.25;

		public double DeathChance { get; set; } = 0.08;


		public Dictionary<string, ReportPairDto>? Reports { get; set; }

		public List<string>? ManifestedPropertyIds { get; set; }
	}

	public sealed class ReportPairDto
	{
		public string Success { get; set; } = string.Empty;
		public string Failure { get; set; } = string.Empty;
	}

	public sealed class ShiftNoteDto
	{
		public int Day { get; set; } = 1;

		public string Title { get; set; } = string.Empty;

		/// <summary>Р В¤Р В»Р ВµР в„–Р Р†Р С•РЎР‚-РЎвЂљР ВµР С”РЎРѓРЎвЂљ Р С•РЎвЂљ Р Т‘Р Р…Р ВµР Р†Р Р…Р С•Р С–Р С• РЎРѓР СР ВµР Р…РЎвЂ°Р С‘Р С”Р В° (Р вЂќР вЂќ, РЎР‚Р В°Р В·Р Т‘Р ВµР В» 2).</summary>
		public string Text { get; set; } = string.Empty;

		/// <summary>Р ВР Т‘Р ВµР Р…РЎвЂљР С‘РЎвЂћР С‘Р С”Р В°РЎвЂљР С•РЎР‚ Р С—РЎР‚Р ВµРЎР‚Р ВµР Р…Р Т‘Р ВµРЎР‚Р ВµР Р…Р Р…Р С•Р С–Р С• РЎР‚Р С•Р В»Р С‘Р С”Р В° Р С—Р С•РЎРѓР В»Р Вµ РЎРѓР СР ВµР Р…РЎвЂ№.</summary>
		public string OutroCutsceneId { get; set; } = string.Empty;
	}
}
