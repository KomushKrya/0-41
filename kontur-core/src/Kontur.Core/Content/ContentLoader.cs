using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kontur.Core.Config;
using Kontur.Core.Model;

namespace Kontur.Core.Content
{
	/// <summary>
	/// Читает JSON-контент и собирает ContentDatabase.
	/// Валидирует ссылочную целостность: битая ссылка на зону/существо/радио — это ошибка контента,
	/// и лучше узнать о ней при загрузке, чем на середине смены.
	/// </summary>
	public static class ContentLoader
	{
		public const string ConfigFile = "config.json";
		public const string BuildingsFile = "buildings.json";
		public const string AbilitiesFile = "abilities.json";
		public const string EquipmentFile = "equipment.json";
		public const string CreaturesFile = "creatures.json";
		public const string RosterFile = "employees.json";
		public const string MissionsFile = "missions.json";
		public const string RadioFile = "radio.json";
		public const string ShiftNotesFile = "shift_notes.json";

		private static readonly JsonSerializerOptions Options = CreateOptions();

		/// <param name="textCatalog">
		/// Текстовый движок для сверки id. Null — сверка пропускается: ядро должно
		/// собираться и прогоняться без движка.
		/// </param>
		public static ContentDatabase Load(IContentSource source, ITextCatalog? textCatalog = null)
		{
			if (source == null)
			{
				throw new ArgumentNullException(nameof(source));
		}

			var database = new ContentDatabase();

			database.Config = ReadRequired<SimulationConfig>(source, ConfigFile);
			if (database.Config.Days.Count == 0)
			{
				database.Config.Days.AddRange(SimulationConfig.CreateDefault().Days);
			}

			LoadBuildings(source, database);
			LoadAbilities(source, database);
			LoadEquipment(source, database);
			LoadCreatures(source, database);
			LoadRoster(source, database);
			LoadRadio(source, database);
			LoadMissions(source, database);
			LoadShiftNotes(source, database);

			Validate(database, textCatalog);
			return database;
		}

		private static void LoadBuildings(IContentSource source, ContentDatabase database)
		{
			List<BuildingDto> buildings = ReadList<BuildingDto>(source, BuildingsFile);
			foreach (BuildingDto dto in buildings)
			{
				if (string.IsNullOrWhiteSpace(dto.Id))
				{
					throw new ContentException($"{BuildingsFile}: у здания отсутствует id.");
				}

				if (!database.Buildings.TryAdd(dto.Id, new BuildingDefinition
				{
					Id = dto.Id,
					IsDispatchTarget = dto.IsDispatchTarget,
					IsHeadquarters = dto.IsHeadquarters
				}))
				{
					throw new ContentException($"{BuildingsFile}: повторяющийся id '{dto.Id}'.");
				}
			}
		}

		private static void LoadAbilities(IContentSource source, ContentDatabase database)
		{
			List<AbilityDto> abilities = ReadList<AbilityDto>(source, AbilitiesFile);
			foreach (AbilityDto dto in abilities)
			{
				var ability = new Ability
				{
					Id = dto.Id,
					Condition = ParseEnum(dto.Condition, AbilityConditionKind.Always, AbilitiesFile, dto.Id, "condition"),
					ConditionValue = dto.ConditionValue,
					Bonus = dto.Bonus == null ? StatBlock.Zero : dto.Bonus.ToModel(),
					AllStatsBonus = dto.AllStatsBonus
				};

				database.Abilities[ability.Id] = ability;
			}
		}

		private static void LoadEquipment(IContentSource source, ContentDatabase database)
		{
			List<EquipmentDto> items = ReadList<EquipmentDto>(source, EquipmentFile);
			foreach (EquipmentDto dto in items)
			{
				var equipment = new EquipmentDefinition
				{
					Id = dto.Id,
					Kind = ParseEnum(dto.Kind, EquipmentKind.Consumable, EquipmentFile, dto.Id, "kind"),
					Bonus = dto.Bonus == null ? StatBlock.Zero : dto.Bonus.ToModel(),
					AllStatsBonus = dto.AllStatsBonus,
					SuccessChanceBonus = dto.SuccessChanceBonus,
					DeathChanceMultiplier = dto.DeathChanceMultiplier
				};

				database.Equipment[equipment.Id] = equipment;
			}
		}

		private static void LoadCreatures(IContentSource source, ContentDatabase database)
		{
			List<CreatureDto> creatures = ReadList<CreatureDto>(source, CreaturesFile);
			foreach (CreatureDto dto in creatures)
			{
				var creature = new CreatureDefinition
				{
					Id = dto.Id,
					IllustrationId = dto.IllustrationId
				};

				if (dto.Tags != null)
				{
					creature.Tags.AddRange(dto.Tags);
				}

				if (dto.Properties != null)
				{
					creature.Properties.AddRange(dto.Properties);
				}

				database.Creatures[creature.Id] = creature;
			}
		}

		private static void LoadRoster(IContentSource source, ContentDatabase database)
		{
			RosterDto roster = ReadRequired<RosterDto>(source, RosterFile);

			if (roster.StartingRoster != null)
			{
				foreach (EmployeeDto dto in roster.StartingRoster)
				{
					database.StartingRoster.Add(ToEmployee(dto));
				}
			}

			if (roster.HirePool != null)
			{
				foreach (EmployeeDto dto in roster.HirePool)
				{
					database.HirePool.Add(new HireCandidate(ToEmployee(dto), dto.AvailableFromDay));
				}
			}
		}

		private static void LoadRadio(IContentSource source, ContentDatabase database)
		{
			List<RadioEncounterDto> encounters = ReadList<RadioEncounterDto>(source, RadioFile);
			foreach (RadioEncounterDto dto in encounters)
			{
				var encounter = new RadioEncounter
				{
					Id = dto.Id,
					SituationText = dto.SituationText
				};

				if (dto.Options != null)
				{
					foreach (RadioOptionDto optionDto in dto.Options)
					{
						encounter.Options.Add(new RadioOption
						{
							Id = optionDto.Id,
							Text = optionDto.Text,
							RequirementMultiplier = optionDto.RequirementMultiplier,
							DeathChanceMultiplier = optionDto.DeathChanceMultiplier,
							InjuryChanceMultiplier = optionDto.InjuryChanceMultiplier,
							ExtraScales = optionDto.ExtraScales == null ? ScaleDelta.Zero : optionDto.ExtraScales.ToModel(),
							RevealsPropertyId = optionDto.RevealsPropertyId,
							Quality = ParseEnum(optionDto.Quality, RadioOptionQuality.Good, RadioFile, optionDto.Id, "quality")
						});
					}
				}

				database.RadioEncounters[encounter.Id] = encounter;
			}
		}

		private static void LoadMissions(IContentSource source, ContentDatabase database)
		{
			List<MissionDto> missions = ReadList<MissionDto>(source, MissionsFile);
			foreach (MissionDto dto in missions)
			{
				var mission = new MissionDefinition
				{
					Id = dto.Id,
					Day = dto.Day,
					CreatureId = dto.CreatureId,
					Title = dto.Title,
					CallerName = dto.CallerName,
					BriefingText = dto.BriefingText,
					Requirements = dto.Requirements == null ? StatBlock.Zero : dto.Requirements.ToModel(),
					TravelSeconds = dto.TravelSeconds,
					OnSiteSeconds = dto.OnSiteSeconds,
					ReturnSeconds = dto.ReturnSeconds,
					RadioEncounterId = string.IsNullOrWhiteSpace(dto.RadioEncounterId) ? null : dto.RadioEncounterId,
					ScalesOnSuccess = ToDelta(dto.ScalesOnSuccess),
					ScalesOnFailure = ToDelta(dto.ScalesOnFailure),
					ScalesOnMissedCall = ToDelta(dto.ScalesOnMissedCall),
					ScalesOnExpiredMarker = ToDelta(dto.ScalesOnExpiredMarker),
					ExperienceOnSuccess = dto.ExperienceOnSuccess,
					ExperienceOnFailure = dto.ExperienceOnFailure,
					InjuryChance = dto.InjuryChance,
					DeathChance = dto.DeathChance,
					ReportSuccessText = dto.ReportSuccessText,
					ReportFailureText = dto.ReportFailureText
				};

				if (dto.ManifestedPropertyIds != null)
				{
					mission.ManifestedPropertyIds.AddRange(dto.ManifestedPropertyIds);
				}

				database.Missions[mission.Id] = mission;
			}
		}

		private static void LoadShiftNotes(IContentSource source, ContentDatabase database)
		{
			if (!source.Exists(ShiftNotesFile))
			{
				return;
			}

			List<ShiftNoteDto> notes = ReadList<ShiftNoteDto>(source, ShiftNotesFile);
			foreach (ShiftNoteDto note in notes)
			{
				database.ShiftNotes[note.Day] = note;
			}
		}

		private static void Validate(ContentDatabase database, ITextCatalog? textCatalog)
		{
			var errors = new List<string>();

			bool hasDispatchTarget = false;
			int headquartersCount = 0;
			foreach (KeyValuePair<string, BuildingDefinition> pair in database.Buildings)
			{
				if (pair.Value.IsHeadquarters)
				{
					headquartersCount++;
					if (pair.Value.IsDispatchTarget)
					{
						errors.Add($"{BuildingsFile}: штаб '{pair.Value.Id}' не может быть целью вызова.");
					}
				}

				if (pair.Value.IsDispatchTarget)
				{
					hasDispatchTarget = true;
				}
		}

			if (!hasDispatchTarget)
			{
				errors.Add($"{BuildingsFile}: нет зданий, доступных для отправки.");
			}

			if (headquartersCount != 1)
			{
				errors.Add($"{BuildingsFile}: должен быть отмечен ровно один штаб.");
			}

			foreach (KeyValuePair<string, MissionDefinition> pair in database.Missions)
			{
				MissionDefinition mission = pair.Value;

				// Пустой creatureId допустим: на филерных вызовах существа нет вообще
				// (утечка в подвале, паника во дворе), и энциклопедии открывать нечего.
				CreatureDefinition? creature = string.IsNullOrEmpty(mission.CreatureId)
					? null
					: database.FindCreature(mission.CreatureId);

				if (creature == null && !string.IsNullOrEmpty(mission.CreatureId))
				{
					errors.Add($"Миссия '{mission.Id}': неизвестное существо '{mission.CreatureId}'.");
				}
				else if (creature == null && mission.ManifestedPropertyIds.Count > 0)
				{
					errors.Add($"Миссия '{mission.Id}': свойства объявлены, а существо не указано.");
				}
				else
				{
					foreach (string propertyId in mission.ManifestedPropertyIds)
					{
						if (!creature.HasProperty(propertyId))
						{
							errors.Add($"Миссия '{mission.Id}': существо '{creature.Id}' не имеет свойства '{propertyId}'.");
						}
					}
				}

				if (mission.RadioEncounterId != null && !database.RadioEncounters.ContainsKey(mission.RadioEncounterId))
				{
					errors.Add($"Миссия '{mission.Id}': неизвестный радио-энкаунтер '{mission.RadioEncounterId}'.");
				}
			}

			foreach (KeyValuePair<string, CreatureDefinition> pair in database.Creatures)
			{
				CreatureDefinition creature = pair.Value;
				if (creature.Properties.Count == 0)
				{
					errors.Add($"Существо '{creature.Id}': не объявлено ни одного свойства.");
				}

				// Абзацы и имя живут в текстовом движке. Без каталога сверить нечем —
				// ядро прогоняется и без движка, поэтому это не ошибка.
				if (textCatalog == null)
				{
					continue;
				}

				if (!textCatalog.HasEntry(creature.Id))
				{
					errors.Add($"Существо '{creature.Id}': нет статьи энциклопедии с таким id.");
					continue;
				}

				foreach (string propertyId in creature.Properties)
				{
					if (!textCatalog.HasProperty(creature.Id, propertyId))
					{
						errors.Add(
							$"Существо '{creature.Id}': в статье нет блока " +
							$"%% reveal: {propertyId} %% под объявленное свойство.");
					}
				}
			}

			// Название и описание снаряжения тоже живут в текстовом движке (content/raw/equipment)
			// под тем же id — сверяем по той же схеме, что и статьи существ.
			if (textCatalog != null)
			{
				foreach (KeyValuePair<string, EquipmentDefinition> pair in database.Equipment)
				{
					if (!textCatalog.HasEntry(pair.Value.Id))
					{
						errors.Add($"Снаряжение '{pair.Value.Id}': нет текста с таким id.");
					}
				}
			}

			foreach (Employee employee in database.StartingRoster)
			{
				ValidateEmployeeAbilities(database, employee, errors);
			}

			foreach (HireCandidate candidate in database.HirePool)
			{
				ValidateEmployeeAbilities(database, candidate.Template, errors);
			}

			foreach (KeyValuePair<string, RadioEncounter> pair in database.RadioEncounters)
			{
				if (pair.Value.Options.Count == 0)
				{
					errors.Add($"Радио-энкаунтер '{pair.Key}': нет вариантов ответа.");
				}
			}

			if (errors.Count > 0)
			{
				var builder = new StringBuilder("Контент не прошёл валидацию:");
				foreach (string error in errors)
				{
					builder.Append(Environment.NewLine).Append("  - ").Append(error);
				}

				throw new ContentException(builder.ToString());
			}
		}

		private static void ValidateEmployeeAbilities(ContentDatabase database, Employee employee, List<string> errors)
		{
			foreach (string abilityId in employee.AbilityIds)
			{
				if (!database.Abilities.ContainsKey(abilityId))
				{
					errors.Add($"Сотрудник '{employee.Id}': неизвестная способность '{abilityId}'.");
				}
			}
		}

		private static Employee ToEmployee(EmployeeDto dto)
		{
			var employee = new Employee
			{
				Id = dto.Id,
				Name = dto.Name,
				Level = dto.Level,
				RankTitle = dto.RankTitle,
				PortraitId = dto.PortraitId,
				BaseStats = dto.Stats == null ? StatBlock.Zero : dto.Stats.ToModel()
			};

			if (dto.Abilities != null)
			{
				employee.AbilityIds.AddRange(dto.Abilities);
			}

			return employee;
		}

		private static ScaleDelta ToDelta(ScaleDeltaDto? dto)
		{
			return dto == null ? ScaleDelta.Zero : dto.ToModel();
		}

		private static TEnum ParseEnum<TEnum>(string value, TEnum fallback, string file, string id, string field)
			where TEnum : struct
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return fallback;
			}

			TEnum parsed;
			if (Enum.TryParse<TEnum>(value, true, out parsed))
			{
				return parsed;
			}

			throw new ContentException($"{file}: запись '{id}', поле '{field}' — недопустимое значение '{value}'.");
		}

		private static T ReadRequired<T>(IContentSource source, string fileName)
			where T : class
		{
			if (!source.Exists(fileName))
			{
				throw new ContentException($"Не найден обязательный файл контента '{fileName}'.");
			}

			string json = source.ReadAllText(fileName);
			T? result;
			try
			{
				result = JsonSerializer.Deserialize<T>(json, Options);
			}
			catch (JsonException exception)
			{
				throw new ContentException($"Ошибка разбора '{fileName}': {exception.Message}");
			}

			if (result == null)
			{
				throw new ContentException($"Файл '{fileName}' пуст или содержит null.");
			}

			return result;
		}

		private static List<T> ReadList<T>(IContentSource source, string fileName)
		{
			if (!source.Exists(fileName))
			{
				throw new ContentException($"Не найден обязательный файл контента '{fileName}'.");
			}

			string json = source.ReadAllText(fileName);
			List<T>? result;
			try
			{
				result = JsonSerializer.Deserialize<List<T>>(json, Options);
			}
			catch (JsonException exception)
			{
				throw new ContentException($"Ошибка разбора '{fileName}': {exception.Message}");
			}

			return result ?? new List<T>();
		}

		private static JsonSerializerOptions CreateOptions()
		{
			var options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip,
				NumberHandling = JsonNumberHandling.AllowReadingFromString
			};

			return options;
		}
	}
}
