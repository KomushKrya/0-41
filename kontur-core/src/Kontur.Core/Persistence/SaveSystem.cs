using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kontur.Core.Content;
using Kontur.Core.Model;
using Kontur.Core.Systems;

namespace Kontur.Core.Persistence
{
	/// <summary>
	/// Перекладывание состояния партии в снимок и обратно.
	///
	/// Правило, которого стоит держаться при правках: сюда попадает только то, что игрок
	/// наиграл. Всё, что можно перечитать из контента — требования миссий, характеристики
	/// снаряжения, тексты, — не сохраняется намеренно. Иначе исправленная опечатка
	/// или подкрученный баланс не доехали бы до уже начатых партий.
	/// </summary>
	public static class SaveSystem
	{
		private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};

		public static string ToJson(SaveData data)
		{
			return JsonSerializer.Serialize(data, Options);
		}

		/// <summary>Разбирает файл сохранения. Возвращает null и причину, если файл негодный.</summary>
		public static SaveData? FromJson(string json, out string error)
		{
			if (string.IsNullOrWhiteSpace(json))
			{
				error = "Файл сохранения пуст.";
				return null;
			}

			SaveData? data;
			try
			{
				data = JsonSerializer.Deserialize<SaveData>(json, Options);
			}
			catch (JsonException exception)
			{
				error = "Файл сохранения повреждён: " + exception.Message;
				return null;
			}

			if (data == null)
			{
				error = "Файл сохранения пуст.";
				return null;
			}

			if (data.Version != SaveData.CurrentVersion)
			{
				error = $"Сохранение версии {data.Version}, игра понимает версию {SaveData.CurrentVersion}.";
				return null;
			}

			error = string.Empty;
			return data;
		}

		// ------------------------------------------------------------------ снятие

		public static SaveData Capture(GameState state, ShiftDirector director, int seed, ulong randomState)
		{
			var data = new SaveData
			{
				Version = SaveData.CurrentVersion,
				SavedAtUtc = DateTime.UtcNow.ToString("o"),
				Seed = seed,
				RandomState = randomState,
				Day = state.Day,
				Infection = state.Scales.Infection,
				Publicity = state.Scales.Publicity,
				Loyalty = state.Scales.Loyalty,
				IsGameOver = state.IsGameOver,
				GameOverReason = state.GameOverReason.HasValue ? state.GameOverReason.Value.ToString() : string.Empty,
				HireOffersDay = state.HireOffersDay,
				StartingRosterConfirmed = state.StartingRosterConfirmed
			};

			for (int i = 0; i < state.Roster.Count; i++)
			{
				data.Roster.Add(CaptureEmployee(state.Roster[i], 1));
			}

			foreach (KeyValuePair<string, Zone> pair in state.Zones)
			{
				data.Zones.Add(new SavedZone
				{
					Id = pair.Value.Id,
					State = pair.Value.State.ToString(),
					SuccessStreak = pair.Value.SuccessStreak,
					FailStreak = pair.Value.FailStreak
				});
			}

			foreach (KeyValuePair<string, EquipmentStack> pair in state.Inventory.Stacks)
			{
				data.Inventory.Add(new SavedStack
				{
					Id = pair.Value.DefinitionId,
					Quantity = pair.Value.Quantity,
					IsShiftOnly = pair.Value.IsShiftOnly
				});
			}

			foreach (string flag in state.Flags.All)
			{
				data.Flags.Add(flag);
			}

			foreach (string creatureId in state.Encyclopedia.GetKnownCreatureIds())
			{
				var knowledge = new SavedCreatureKnowledge { CreatureId = creatureId };
				knowledge.RevealedPropertyIds.AddRange(state.Encyclopedia.GetRevealedProperties(creatureId));
				data.Encyclopedia.Add(knowledge);
			}

			data.HiredCandidateIds.AddRange(state.HiredCandidateIds);
			data.UsedMissionIds.AddRange(state.UsedMissionIds);

			for (int i = 0; i < state.Reports.Count; i++)
			{
				data.Reports.Add(CaptureReport(state.Reports[i]));
			}

			for (int i = 0; i < state.HireOffers.Count; i++)
			{
				data.HireOffers.Add(CaptureEmployee(
					state.HireOffers[i].Template,
					state.HireOffers[i].AvailableFromDay));
			}

			data.Shift = director.CaptureShift();
			return data;
		}

		private static SavedEmployee CaptureEmployee(Employee employee, int availableFromDay)
		{
			var saved = new SavedEmployee
			{
				Id = employee.Id,
				Name = employee.Name,
				RankTitle = employee.RankTitle,
				PortraitId = employee.PortraitId,
				ArchetypeId = employee.ArchetypeId,
				Level = employee.Level,
				Strength = employee.BaseStats.Strength,
				Intellect = employee.BaseStats.Intellect,
				Combat = employee.BaseStats.Combat,
				Agility = employee.BaseStats.Agility,
				Charisma = employee.BaseStats.Charisma,
				Experience = employee.Experience,
				UnspentSkillPoints = employee.UnspentSkillPoints,
				Status = employee.Status.ToString(),
				IsInjured = employee.IsInjured,
				CurrentIncidentId = employee.CurrentIncidentId ?? string.Empty,
				AvailableFromDay = availableFromDay
			};

			saved.AbilityIds.AddRange(employee.AbilityIds);
			return saved;
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

		// ------------------------------------------------------------------ восстановление

		/// <summary>
		/// Сверка сохранения с текущим контентом. Делается целиком до того, как хоть что-то
		/// применено: половина загруженной партии хуже, чем отказ загружать.
		///
		/// Пропавший сотрудник или предмет — не беда, а сознательное упрощение: их можно
		/// просто выкинуть. А вот пропавшая миссия ломает инцидент в работе, и молча
		/// проглотить это нельзя.
		/// </summary>
		public static bool Validate(SaveData data, ContentDatabase content, out string error)
		{
			if (data.Shift != null)
			{
				var missing = new List<string>();
				CollectMissingMissions(data.Shift.Pending, content, missing);
				CollectMissingMissions(data.Shift.Incidents, content, missing);

				if (missing.Count > 0)
				{
					error = "В сохранении есть вызовы по миссиям, которых больше нет в контенте: "
						+ string.Join(", ", missing) + ".";
					return false;
				}
			}

			error = string.Empty;
			return true;
		}

		private static void CollectMissingMissions(
			List<SavedIncident> incidents,
			ContentDatabase content,
			List<string> missing)
		{
			for (int i = 0; i < incidents.Count; i++)
			{
				string missionId = incidents[i].MissionId;
				if (content.FindMission(missionId) == null && !missing.Contains(missionId))
				{
					missing.Add(missionId);
				}
			}
		}

		/// <summary>Раскладывает снимок обратно по состоянию. Вызывать только после Validate.</summary>
		public static void Apply(SaveData data, GameState state, ContentDatabase content)
		{
			state.Day = data.Day;
			state.Scales = new ScaleValues(data.Infection, data.Publicity, data.Loyalty);
			state.IsGameOver = data.IsGameOver;

			GameOverReason reason;
			state.GameOverReason = Enum.TryParse<GameOverReason>(data.GameOverReason, true, out reason)
				? reason
				: (GameOverReason?)null;

			state.Roster.Clear();
			for (int i = 0; i < data.Roster.Count; i++)
			{
				state.Roster.Add(RestoreEmployee(data.Roster[i]));
			}

			// Зоны берутся из контента (там имя, вес, координаты), а из сохранения
			// накладывается только то, что игрок наиграл: штриховка и серии.
			state.Zones.Clear();
			foreach (KeyValuePair<string, Zone> pair in content.Zones)
			{
				state.Zones[pair.Key] = new Zone
				{
					Id = pair.Value.Id,
					Name = pair.Value.Name,
					State = pair.Value.State,
					BaseWeight = pair.Value.BaseWeight,
					MapX = pair.Value.MapX,
					MapY = pair.Value.MapY
				};
			}

			for (int i = 0; i < data.Zones.Count; i++)
			{
				SavedZone savedZone = data.Zones[i];
				Zone? zone = state.FindZone(savedZone.Id);
				if (zone == null)
				{
					continue;
				}

				ZoneState zoneState;
				if (Enum.TryParse<ZoneState>(savedZone.State, true, out zoneState))
				{
					zone.State = zoneState;
				}

				zone.SuccessStreak = savedZone.SuccessStreak;
				zone.FailStreak = savedZone.FailStreak;
			}

			state.Inventory.Clear();
			for (int i = 0; i < data.Inventory.Count; i++)
			{
				SavedStack stack = data.Inventory[i];
				if (content.FindEquipment(stack.Id) != null)
				{
					state.Inventory.Add(stack.Id, stack.Quantity, stack.IsShiftOnly);
				}
			}

			state.Flags.Clear();
			for (int i = 0; i < data.Flags.Count; i++)
			{
				state.Flags.Set(data.Flags[i]);
			}

			state.Encyclopedia.Clear();
			for (int i = 0; i < data.Encyclopedia.Count; i++)
			{
				SavedCreatureKnowledge knowledge = data.Encyclopedia[i];
				if (content.FindCreature(knowledge.CreatureId) == null)
				{
					continue;
				}

				state.Encyclopedia.Identify(knowledge.CreatureId);
				for (int p = 0; p < knowledge.RevealedPropertyIds.Count; p++)
				{
					state.Encyclopedia.RevealProperty(knowledge.CreatureId, knowledge.RevealedPropertyIds[p]);
				}
			}

			state.HiredCandidateIds.Clear();
			for (int i = 0; i < data.HiredCandidateIds.Count; i++)
			{
				state.HiredCandidateIds.Add(data.HiredCandidateIds[i]);
			}

			state.UsedMissionIds.Clear();
			for (int i = 0; i < data.UsedMissionIds.Count; i++)
			{
				state.UsedMissionIds.Add(data.UsedMissionIds[i]);
			}

			state.Reports.Clear();
			for (int i = 0; i < data.Reports.Count; i++)
			{
				state.Reports.Add(RestoreReport(data.Reports[i]));
			}

			// Кандидаты восстанавливаются как есть: сгенерированного человека
			// заново не собрать — фабрика к этому моменту ушла вперёд по своей
			// последовательности, и получились бы другие люди.
			state.HireOffers.Clear();
			for (int i = 0; i < data.HireOffers.Count; i++)
			{
				SavedEmployee saved = data.HireOffers[i];
				state.HireOffers.Add(new HireCandidate(RestoreEmployee(saved), saved.AvailableFromDay));
			}

			state.HireOffersDay = data.HireOffersDay;

			// Стартовый выбор в снимок не кладётся: он живёт считаные секунды
			// до первой смены, и сохраниться в этот момент негде.
			state.StartingChoice.Clear();
			state.StartingRosterConfirmed = data.StartingRosterConfirmed;
		}

		private static Employee RestoreEmployee(SavedEmployee saved)
		{
			var employee = new Employee
			{
				Id = saved.Id,
				Name = saved.Name,
				RankTitle = saved.RankTitle,
				PortraitId = saved.PortraitId,
				ArchetypeId = saved.ArchetypeId,
				Level = saved.Level,
				BaseStats = new StatBlock(
					saved.Strength,
					saved.Intellect,
					saved.Combat,
					saved.Agility,
					saved.Charisma),
				Experience = saved.Experience,
				UnspentSkillPoints = saved.UnspentSkillPoints,
				IsInjured = saved.IsInjured,
				CurrentIncidentId = string.IsNullOrEmpty(saved.CurrentIncidentId) ? null : saved.CurrentIncidentId
			};

			EmployeeStatus status;
			employee.Status = Enum.TryParse<EmployeeStatus>(saved.Status, true, out status)
				? status
				: EmployeeStatus.Available;

			employee.AbilityIds.AddRange(saved.AbilityIds);
			return employee;
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
	}
}
