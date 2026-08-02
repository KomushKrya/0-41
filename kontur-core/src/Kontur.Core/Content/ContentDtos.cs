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

		public int Intellect { get; set; }

		public int Combat { get; set; }

		public int Agility { get; set; }

		public int Charisma { get; set; }

		public StatBlock ToModel()
		{
			return new StatBlock(Strength, Intellect, Combat, Agility, Charisma);
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

	public sealed class BuildingDto
	{
		public string Id { get; set; } = string.Empty;

		public bool IsDispatchTarget { get; set; }

		public bool IsHeadquarters { get; set; }

		public string ZoneId { get; set; } = string.Empty;
	}

	public sealed class ZoneDto
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public double BaseWeight { get; set; } = 1.0;

		public double MapX { get; set; }

		public double MapY { get; set; }
	}

	public sealed class AbilityDto
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>Название и описание — в текстовом движке, здесь только механика.</summary>
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

	public sealed class CreatureDto
	{
		public string Id { get; set; } = string.Empty;

		public List<string>? Tags { get; set; }

		/// <summary>Только id свойств — абзацы под них лежат в текстовом движке.</summary>
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

		/// <summary>Для кандидатов на найм: с какого дня доступен (ДД, раздел 5).</summary>
		public int AvailableFromDay { get; set; } = 1;
	}

	public sealed class RosterDto
	{
		public List<EmployeeDto>? StartingRoster { get; set; }

		public List<EmployeeDto>? HirePool { get; set; }

		/// <summary>Фабрика кандидатов. Отсутствует — предлагаются только прописанные вручную.</summary>
		public GeneratorDto? Generator { get; set; }
	}

	public sealed class GeneratorDto
	{
		public int CandidatesPerShift { get; set; }

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
		/// <summary>Пусто — берётся умолчание по типу диалога из config.missionEvents.</summary>
		public double? DeathChanceMultiplier { get; set; }

		public double? InjuryChanceMultiplier { get; set; }

		public ScaleDeltaDto? ExtraScales { get; set; }

		public string? RevealsPropertyId { get; set; }

		/// <summary>None | Injury | Death. Только ужесточает потолок миссии.</summary>
		public string? ConsequenceCap { get; set; }
	}

	/// <summary>
	/// Баланс вмешательства. Формулировки, `quality`, `requirement_modifier` и `requires` живут
	/// в content/raw/mission_events под тем же id — здесь только то, что крутит дизайнер.
	/// Ключи словаря options должны совпадать с id вариантов в тексте; расхождение
	/// ловит загрузчик.
	/// </summary>
	public sealed class MissionEventDto
	{
		public string Id { get; set; } = string.Empty;

		public Dictionary<string, MissionEventOptionDto>? Options { get; set; }
	}

	public sealed class ReportPairDto
	{
		public string Success { get; set; } = string.Empty;

		public string Failure { get; set; } = string.Empty;
	}

	public sealed class MissionDto
	{
		public string Id { get; set; } = string.Empty;

		public int Day { get; set; } = 1;

		/// <summary>Story | Filler. По умолчанию Filler.</summary>
		public string Tier { get; set; } = "Filler";

		/// <summary>None | Injury | Death. Пусто — берётся от уровня миссии.</summary>
		public string? ConsequenceCap { get; set; }

		public string ZoneId { get; set; } = string.Empty;

		public string CreatureId { get; set; } = string.Empty;

		public StatBlockDto? Requirements { get; set; }

		/// <summary>Главная характеристика вызова — весит вдвое. Пусто — все равнозначны.</summary>
		public string? PrimaryStat { get; set; }

		/// <summary>Сколько человек можно отправить. По умолчанию один, как в Dispatch.</summary>
		public int SquadLimit { get; set; } = 1;

		public double TravelSeconds { get; set; } = 12.0;

		public double OnSiteSeconds { get; set; } = 6.0;

		public double ReturnSeconds { get; set; } = 10.0;

		public string CallId { get; set; } = string.Empty;

		public string MissionEventId { get; set; } = string.Empty;

		/// <summary>Ключ — id варианта решения; пустая строка — исход без вмешательства.</summary>
		public Dictionary<string, ReportPairDto>? Reports { get; set; }

		public ScaleDeltaDto? ScalesOnSuccess { get; set; }

		public ScaleDeltaDto? ScalesOnFailure { get; set; }

		public ScaleDeltaDto? ScalesOnMissedCall { get; set; }

		public ScaleDeltaDto? ScalesOnExpiredMarker { get; set; }

		public int ExperienceOnSuccess { get; set; } = 100;

		public int ExperienceOnFailure { get; set; } = 25;

		public double InjuryChance { get; set; } = 0.25;

		public double DeathChance { get; set; } = 0.08;

		public List<string>? ManifestedPropertyIds { get; set; }
	}

}
