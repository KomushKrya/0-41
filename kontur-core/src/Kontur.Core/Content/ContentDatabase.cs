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

		public Dictionary<string, Ability> Abilities { get; } = new Dictionary<string, Ability>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, EquipmentDefinition> Equipment { get; } = new Dictionary<string, EquipmentDefinition>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, CreatureDefinition> Creatures { get; } = new Dictionary<string, CreatureDefinition>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, MissionDefinition> Missions { get; } = new Dictionary<string, MissionDefinition>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, RadioEncounter> RadioEncounters { get; } = new Dictionary<string, RadioEncounter>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<int, ShiftNoteDto> ShiftNotes { get; } = new Dictionary<int, ShiftNoteDto>();

		public List<Employee> StartingRoster { get; } = new List<Employee>();

		public List<HireCandidate> HirePool { get; } = new List<HireCandidate>();

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

		public RadioEncounter? FindRadioEncounter(string encounterId)
		{
			RadioEncounter? encounter;
			return RadioEncounters.TryGetValue(encounterId, out encounter) ? encounter : null;
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
