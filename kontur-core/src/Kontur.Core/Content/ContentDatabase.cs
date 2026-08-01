using System;
using System.Collections.Generic;
using Kontur.Core.Config;
using Kontur.Core.Model;

namespace Kontur.Core.Content
{
	/// <summary>Загруженный и провалидированный контент. Рантайм считает его неизменяемым.</summary>
	public sealed class ContentDatabase
	{
		public SimulationConfig Config { get; set; } = SimulationConfig.CreateDefault();

		public Dictionary<string, Zone> Zones { get; } = new Dictionary<string, Zone>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Дома на карте. Пусто — карта ещё без домов, метка ставится по координатам зоны.</summary>
		public Dictionary<string, BuildingDefinition> Buildings { get; } = new Dictionary<string, BuildingDefinition>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, Ability> Abilities { get; } = new Dictionary<string, Ability>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, EquipmentDefinition> Equipment { get; } = new Dictionary<string, EquipmentDefinition>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, CreatureDefinition> Creatures { get; } = new Dictionary<string, CreatureDefinition>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, MissionDefinition> Missions { get; } = new Dictionary<string, MissionDefinition>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, MissionEventDefinition> MissionEvents { get; } = new Dictionary<string, MissionEventDefinition>(StringComparer.OrdinalIgnoreCase);


		public List<Employee> StartingRoster { get; } = new List<Employee>();

		/// <summary>Прописанные вручную кандидаты — сюжетные лица, появляющиеся в свой день.</summary>
		public List<HireCandidate> HirePool { get; } = new List<HireCandidate>();

		/// <summary>Настройки фабрики, которой добираются остальные кандидаты каждую смену.</summary>
		public EmployeeGeneratorSettings Generator { get; } = new EmployeeGeneratorSettings();

		public IReadOnlyList<MissionDefinition> GetMissionsForDay(int day)
		{
			var result = new List<MissionDefinition>();
			foreach (KeyValuePair<string, MissionDefinition> pair in Missions)
			{
				if (pair.Value.Day == day)
				{
					result.Add(pair.Value);
				}
			}

			result.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
			return result;
		}

		public Zone? FindZone(string zoneId)
		{
			Zone? zone;
			return Zones.TryGetValue(zoneId, out zone) ? zone : null;
		}

		public BuildingDefinition? FindBuilding(string buildingId)
		{
			BuildingDefinition? building;
			return Buildings.TryGetValue(buildingId, out building) ? building : null;
		}

		/// <summary>Главное управление: откуда выезжает и куда возвращается группа.</summary>
		public BuildingDefinition? FindHeadquarters()
		{
			foreach (KeyValuePair<string, BuildingDefinition> pair in Buildings)
			{
				if (pair.Value.IsHeadquarters)
				{
					return pair.Value;
				}
			}

			return null;
		}

		public CreatureDefinition? FindCreature(string creatureId)
		{
			CreatureDefinition? creature;
			return Creatures.TryGetValue(creatureId, out creature) ? creature : null;
		}

		public MissionDefinition? FindMission(string missionId)
		{
			MissionDefinition? mission;
			return Missions.TryGetValue(missionId, out mission) ? mission : null;
		}

		public EquipmentDefinition? FindEquipment(string equipmentId)
		{
			EquipmentDefinition? equipment;
			return Equipment.TryGetValue(equipmentId, out equipment) ? equipment : null;
		}

		public Ability? FindAbility(string abilityId)
		{
			Ability? ability;
			return Abilities.TryGetValue(abilityId, out ability) ? ability : null;
		}

		public MissionEventDefinition? FindMissionEvent(string missionEventId)
		{
			MissionEventDefinition? missionEvent;
			return MissionEvents.TryGetValue(missionEventId, out missionEvent) ? missionEvent : null;
		}
	}

	public sealed class HireCandidate
	{
		public HireCandidate(Employee template, int availableFromDay)
		{
			Template = template;
			AvailableFromDay = availableFromDay;
		}

		public Employee Template { get; }

		public int AvailableFromDay { get; }
	}
}
