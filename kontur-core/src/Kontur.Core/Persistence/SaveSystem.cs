using System;
using System.Collections.Generic;
using System.Text.Json;
using Kontur.Core.Content;
using Kontur.Core.Model;
using Kontur.Core.Systems;

namespace Kontur.Core.Persistence
{
	/// <summary>JSON-РѕР±С‘СЂС‚РєР° Рё РїРµСЂРµРЅРѕСЃ СЃРѕСЃС‚РѕСЏРЅРёСЏ РґР»СЏ СЃРѕС…СЂР°РЅРµРЅРёР№ РјРµР¶РґСѓ СЃРјРµРЅР°РјРё.</summary>
	public static class SaveSystem
	{
		private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			WriteIndented = true
		};

		public static string ToJson(SaveData data)
		{
			return JsonSerializer.Serialize(data, Options);
		}

		public static SaveData? FromJson(string json, out string error)
		{
			try
			{
				SaveData? data = JsonSerializer.Deserialize<SaveData>(json, Options);
				if (data == null)
				{
					error = "Р¤Р°Р№Р» СЃРѕС…СЂР°РЅРµРЅРёСЏ РїСѓСЃС‚.";
					return null;
				}

				if (data.Version != SaveData.CurrentVersion)
				{
					error = $"РЎРѕС…СЂР°РЅРµРЅРёРµ РІРµСЂСЃРёРё {data.Version}, РёРіСЂР° РїРѕРЅРёРјР°РµС‚ РІРµСЂСЃРёСЋ {SaveData.CurrentVersion}.";
					return null;
				}

				error = string.Empty;
				return data;
			}
			catch (JsonException exception)
			{
				error = "Р¤Р°Р№Р» СЃРѕС…СЂР°РЅРµРЅРёСЏ РїРѕРІСЂРµР¶РґС‘РЅ: " + exception.Message;
				return null;
			}
		}

		public static SaveData Capture(GameState state, ShiftDirector director, int seed, ulong randomState, string label = "")
		{
			var data = new SaveData
			{
				Version = SaveData.CurrentVersion,
				SavedAtUtc = DateTime.UtcNow.ToString("o"),
				Label = label ?? string.Empty,
				Seed = seed,
				RandomState = randomState,
				Day = state.Day,
				Infection = state.Scales.Infection,
				Publicity = state.Scales.Publicity,
				Loyalty = state.Scales.Loyalty,
				IsGameOver = state.IsGameOver,
				GameOverReason = state.GameOverReason?.ToString() ?? string.Empty
			};

			foreach (Employee employee in state.Roster)
			{
				data.Roster.Add(CaptureEmployee(employee));
			}

			foreach (KeyValuePair<string, EquipmentStack> pair in state.Inventory.Stacks)
			{
				data.Inventory.Add(new SavedStack { Id = pair.Key, Quantity = pair.Value.Quantity, IsShiftOnly = pair.Value.IsShiftOnly });
			}

			data.Flags.AddRange(state.Flags.All);
			data.HiredCandidateIds.AddRange(state.HiredCandidateIds);
			data.UsedMissionIds.AddRange(state.UsedMissionIds);

			foreach (string creatureId in state.Encyclopedia.GetKnownCreatureIds())
			{
				var knowledge = new SavedKnowledge { CreatureId = creatureId };
				knowledge.PropertyIds.AddRange(state.Encyclopedia.GetRevealedProperties(creatureId));
				data.Encyclopedia.Add(knowledge);
			}

			foreach (MissionReport report in state.Reports)
			{
				data.Reports.Add(CaptureReport(report));
			}

			data.Shift = director.CaptureShift();

			return data;
		}

		public static bool Validate(SaveData data, ContentDatabase content, out string error)
		{
			if (data.Shift != null)
			{
				foreach (SavedIncident incident in data.Shift.Pending)
				{
					if (content.FindMission(incident.MissionId) == null)
					{
						error = $"Р’ СЃРѕС…СЂР°РЅРµРЅРёРё РµСЃС‚СЊ РѕС‚СЃСѓС‚СЃС‚РІСѓСЋС‰Р°СЏ РјРёСЃСЃРёСЏ '{incident.MissionId}'.";
						return false;
					}
				}

				foreach (SavedIncident incident in data.Shift.Incidents)
				{
					if (content.FindMission(incident.MissionId) == null)
					{
						error = $"Р’ СЃРѕС…СЂР°РЅРµРЅРёРё РµСЃС‚СЊ РѕС‚СЃСѓС‚СЃС‚РІСѓСЋС‰Р°СЏ РјРёСЃСЃРёСЏ '{incident.MissionId}'.";
						return false;
					}
				}
			}

			error = string.Empty;
			return true;
		}

		public static void Apply(SaveData data, GameState state)
		{
			state.Day = data.Day;
			state.Scales = new ScaleValues(data.Infection, data.Publicity, data.Loyalty);
			state.IsGameOver = data.IsGameOver;
			state.GameOverReason = Enum.TryParse<GameOverReason>(data.GameOverReason, true, out GameOverReason reason)
				? reason : (GameOverReason?)null;

			state.Roster.Clear();
			foreach (SavedEmployee saved in data.Roster)
			{
				state.Roster.Add(RestoreEmployee(saved));
			}

			state.Inventory.Clear();
			foreach (SavedStack stack in data.Inventory)
			{
				state.Inventory.Add(stack.Id, stack.Quantity, stack.IsShiftOnly);
			}

			state.Flags.Clear();
			foreach (string flag in data.Flags) state.Flags.Set(flag);
			state.HiredCandidateIds.Clear();
			foreach (string id in data.HiredCandidateIds) state.HiredCandidateIds.Add(id);
			state.UsedMissionIds.Clear();
			foreach (string id in data.UsedMissionIds) state.UsedMissionIds.Add(id);

			state.Encyclopedia.Clear();
			foreach (SavedKnowledge knowledge in data.Encyclopedia)
			{
				state.Encyclopedia.Identify(knowledge.CreatureId);
				foreach (string propertyId in knowledge.PropertyIds) state.Encyclopedia.RevealProperty(knowledge.CreatureId, propertyId);
			}

			state.Reports.Clear();
			foreach (SavedReport saved in data.Reports)
			{
				state.Reports.Add(RestoreReport(saved));
			}
		}

		public static SavedReport CaptureReport(MissionReport report)
		{
			var saved = new SavedReport
			{
				IncidentId = report.IncidentId,
				MissionId = report.MissionId,
				ReportId = report.ReportId,
				CreatureId = report.CreatureId,
				ChosenOptionId = report.ChosenOptionId,
				IsSuccess = report.IsSuccess
			};
			saved.RevealedPropertyIds.AddRange(report.RevealedPropertyIds);
			return saved;
		}

		public static MissionReport RestoreReport(SavedReport saved)
		{
			var report = new MissionReport
			{
				IncidentId = saved.IncidentId,
				MissionId = saved.MissionId,
				ReportId = saved.ReportId,
				CreatureId = saved.CreatureId,
				ChosenOptionId = saved.ChosenOptionId,
				IsSuccess = saved.IsSuccess
			};
			report.RevealedPropertyIds.AddRange(saved.RevealedPropertyIds);
			return report;
		}

		private static SavedEmployee CaptureEmployee(Employee employee)
		{
			var saved = new SavedEmployee
			{
				Id = employee.Id, Name = employee.Name, RankTitle = employee.RankTitle, PortraitId = employee.PortraitId,
				Level = employee.Level, Strength = employee.BaseStats.Strength, Intellect = employee.BaseStats.Intellect,
				Combat = employee.BaseStats.Combat, Charisma = employee.BaseStats.Charisma, Agility = employee.BaseStats.Agility,
				Experience = employee.Experience, UnspentSkillPoints = employee.UnspentSkillPoints, Status = employee.Status.ToString(),
				IsInjured = employee.IsInjured, CurrentIncidentId = employee.CurrentIncidentId ?? string.Empty
			};
			saved.AbilityIds.AddRange(employee.AbilityIds);
			return saved;
		}

		private static Employee RestoreEmployee(SavedEmployee saved)
		{
			var employee = new Employee
			{
				Id = saved.Id, Name = saved.Name, RankTitle = saved.RankTitle, PortraitId = saved.PortraitId, Level = saved.Level,
				BaseStats = new StatBlock(saved.Strength, saved.Intellect, saved.Combat, saved.Agility, saved.Charisma),
				Experience = saved.Experience, UnspentSkillPoints = saved.UnspentSkillPoints, IsInjured = saved.IsInjured,
				CurrentIncidentId = string.IsNullOrWhiteSpace(saved.CurrentIncidentId) ? null : saved.CurrentIncidentId,
				Status = Enum.TryParse<EmployeeStatus>(saved.Status, true, out EmployeeStatus status) ? status : EmployeeStatus.Available
			};
			employee.AbilityIds.AddRange(saved.AbilityIds);
			return employee;
		}
	}
}
