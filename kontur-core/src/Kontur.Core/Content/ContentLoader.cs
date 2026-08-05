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
		// В новом ядре файл radio.json хранит баланс MissionEvent. Название осталось
		// radio.json хранит баланс MissionEvent; текст решений остаётся в Content.
		public const string MissionEventsFile = RadioFile;
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
			LoadMissionEvents(source, database, textCatalog);
			LoadGeneratorBioLines(database, textCatalog);
			LoadMissions(source, database, textCatalog);
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
				AbilityConditionKind condition = ParseEnum(dto.Condition, AbilityConditionKind.Always, EquipmentFile, dto.Id, "condition");
				if (condition == AbilityConditionKind.WithEquipment)
				{
					throw new ContentException($"{EquipmentFile}: запись '{dto.Id}' — снаряжение не может зависеть от другого снаряжения (condition WithEquipment).");
				}

				var equipment = new EquipmentDefinition
				{
					Id = dto.Id,
					Name = dto.Name,
					Description = dto.Description,
					Kind = ParseEnum(dto.Kind, EquipmentKind.Consumable, EquipmentFile, dto.Id, "kind"),
					Condition = condition,
					ConditionValue = dto.ConditionValue,
					Bonus = dto.Bonus == null ? StatBlock.Zero : dto.Bonus.ToModel(),
					AllStatsBonus = dto.AllStatsBonus
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

			if (roster.Generator != null)
			{
				LoadGenerator(roster.Generator, database.Generator);
			}
		}

		private static void LoadGenerator(GeneratorDto dto, EmployeeGeneratorSettings settings)
		{
			settings.CandidatesPerShift = dto.CandidatesPerShift;
			settings.CandidatesChoiceMargin = dto.CandidatesChoiceMargin;
			settings.LevelLagBehindDay = dto.LevelLagBehindDay;
			settings.LevelSpread = dto.LevelSpread;
			settings.MinAge = dto.MinAge; settings.MaxAge = dto.MaxAge;
			settings.MinStat = dto.MinStat; settings.MaxStat = dto.MaxStat;
			settings.StatPointsBase = dto.StatPointsBase; settings.StatPointsPerLevel = dto.StatPointsPerLevel;
			settings.PrimaryWeight = dto.PrimaryWeight; settings.SecondaryWeight = dto.SecondaryWeight;
			settings.StartingChoicePoolSize = dto.StartingChoicePoolSize;
			settings.AbilitiesBase = dto.AbilitiesBase; settings.SecondAbilityFromLevel = dto.SecondAbilityFromLevel;
			if (dto.BioSlots != null) settings.BioSlots.AddRange(dto.BioSlots);
			if (dto.Surnames != null) settings.Surnames.AddRange(dto.Surnames);
			if (dto.Initials != null) settings.Initials.AddRange(dto.Initials);
			if (dto.Portraits != null) settings.PortraitIds.AddRange(dto.Portraits);
			if (dto.LevelsByDay != null) foreach (LevelRangeDto range in dto.LevelsByDay) settings.LevelsByDay.Add(new LevelRange { FromDay = range.FromDay, MinLevel = range.MinLevel, MaxLevel = range.MaxLevel });
			if (dto.Archetypes == null) return;
			foreach (ArchetypeDto source in dto.Archetypes)
			{
				var archetype = new EmployeeArchetype { Id = source.Id, Weight = source.Weight, RankTitle = source.RankTitle };
				AddGeneratorStats(source.Primary, archetype.PrimaryStats, source.Id, "primary");
				AddGeneratorStats(source.Secondary, archetype.SecondaryStats, source.Id, "secondary");
				if (source.Abilities != null) archetype.AbilityIds.AddRange(source.Abilities);
				if (source.Portraits != null) archetype.PortraitIds.AddRange(source.Portraits);
				settings.Archetypes.Add(archetype);
			}
		}

		private static void AddGeneratorStats(List<string>? names, List<StatKind> target, string archetypeId, string field)
		{
			if (names == null) return;
			foreach (string name in names)
			{
				if (!StatKinds.TryParse(name, out StatKind kind)) throw new ContentException($"Архетип '{archetypeId}', поле '{field}': неизвестная характеристика '{name}'.");
				target.Add(kind);
			}
		}

		/// <summary>Био-строки принадлежат текстовому движку; ядро хранит только их id.</summary>
		private static void LoadGeneratorBioLines(ContentDatabase database, ITextCatalog? textCatalog)
		{
			if (textCatalog == null || !database.Generator.IsEnabled) return;
			foreach (string slot in database.Generator.BioSlots)
			{
				IReadOnlyList<string> lines = textCatalog.GetBioLines(slot);
				if (lines.Count > 0) database.Generator.BioLinesBySlot[slot] = new List<string>(lines);
			}
		}

		private static void LoadMissions(IContentSource source, ContentDatabase database, ITextCatalog? textCatalog)
		{
			List<MissionDto> missions = ReadList<MissionDto>(source, MissionsFile);
			foreach (MissionDto dto in missions)
			{
				var mission = new MissionDefinition
				{
					Id = dto.Id,
					Day = dto.Day,
					Tier = ParseEnum(dto.Tier, MissionTier.Filler, MissionsFile, dto.Id, "tier"),
					ConsequenceCapOverride = ParseOptionalCap(dto.ConsequenceCap, MissionsFile, dto.Id),
					CreatureId = dto.CreatureId,
					CallId = dto.CallId,
					Requirements = dto.Requirements == null ? StatBlock.Zero : dto.Requirements.ToModel(),
					PrimaryStat = ParsePrimaryStat(dto.PrimaryStat, dto.Id),
					SquadLimit = dto.SquadLimit,
					TravelSeconds = dto.TravelSeconds,
					OnSiteSeconds = dto.OnSiteSeconds,
					ReturnSeconds = dto.ReturnSeconds,
					MissionEventId = string.IsNullOrWhiteSpace(dto.MissionEventId) ? null : dto.MissionEventId,
					ScalesOnSuccess = ToDelta(dto.ScalesOnSuccess),
					ScalesOnFailure = ToDelta(dto.ScalesOnFailure),
					ScalesOnMissedCall = ToDelta(dto.ScalesOnMissedCall),
					ScalesOnExpiredMarker = ToDelta(dto.ScalesOnExpiredMarker),
					ExperienceOnSuccess = dto.ExperienceOnSuccess,
					ExperienceOnFailure = dto.ExperienceOnFailure,
					InjuryChance = dto.InjuryChance,
					DeathChance = dto.DeathChance
				};

				if (dto.Reports != null)
				{
					foreach (KeyValuePair<string, ReportPairDto> report in dto.Reports)
					{
						mission.Reports[report.Key] = new MissionReportPair
						{
							SuccessId = report.Value.Success,
							FailureId = report.Value.Failure
						};
					}
				}

				if (dto.ManifestedPropertyIds != null)
				{
					mission.ManifestedPropertyIds.AddRange(dto.ManifestedPropertyIds);
				}

				// Условие появления миссии живёт во фронтматтере её звонка: там его пишет автор.
				if (textCatalog != null)
				{
					mission.RequiredFlags = textCatalog.GetRequirements(mission.CallId);
				}

				database.Missions[mission.Id] = mission;
			}
		}
		}

		private static void LoadMissionEvents(IContentSource source, ContentDatabase database, ITextCatalog? textCatalog)
		{
			foreach (MissionEventDto dto in ReadList<MissionEventDto>(source, MissionEventsFile))
			{
				var definition = new MissionEventDefinition { Id = dto.Id };
				Dictionary<string, MissionEventOptionDto> balance = dto.Options ?? new Dictionary<string, MissionEventOptionDto>();
				IReadOnlyList<TextOption> textOptions = textCatalog == null
					? Array.Empty<TextOption>()
					: textCatalog.GetOptions(dto.Id);

				if (textOptions.Count > 0)
				{
					for (int i = 0; i < textOptions.Count; i++)
					{
						MissionEventOptionDto? balanceOption;
						balance.TryGetValue(textOptions[i].Id, out balanceOption);
						definition.Options.Add(ToMissionEventOption(textOptions[i], balanceOption, database.Config));
					}
				}
				else foreach (KeyValuePair<string, MissionEventOptionDto> pair in balance)
				{
					definition.Options.Add(ToMissionEventOption(new TextOption(pair.Key, Array.Empty<StatKind>()), pair.Value, database.Config));
				}

				database.MissionEvents[definition.Id] = definition;
			}
		}

		private static MissionEventOption ToMissionEventOption(TextOption textOption, MissionEventOptionDto? dto, SimulationConfig config)
		{
			MissionEventConfig defaults = config.MissionEvents;
			return new MissionEventOption
			{
				Id = textOption.Id,
				CheckedStats = textOption.CheckedStats,
				RequirementModifier = defaults.RequirementModifier,
				DeathChanceMultiplier = dto?.DeathChanceMultiplier ?? defaults.RiskMultiplier,
				InjuryChanceMultiplier = dto?.InjuryChanceMultiplier ?? defaults.RiskMultiplier,
				ExtraScales = dto?.ExtraScales == null ? ScaleDelta.Zero : ToDelta(dto.ExtraScales),
				RevealsPropertyId = dto?.RevealsPropertyId,
				SetsFlagId = dto?.SetsFlagId,
				ConsequenceCapOverride = dto == null ? null : ParseOptionalCap(dto.ConsequenceCap, MissionEventsFile, textOption.Id),
				RequiresEquipmentId = dto?.RequiresEquipmentId
			};
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

				CreatureDefinition? creature = string.IsNullOrWhiteSpace(mission.CreatureId)
					? null
					: database.FindCreature(mission.CreatureId);
				if (!string.IsNullOrWhiteSpace(mission.CreatureId) && creature == null)
				{
					errors.Add($"Миссия '{mission.Id}': неизвестное существо '{mission.CreatureId}'.");
				}
				else if (creature != null)
				{
					foreach (string propertyId in mission.ManifestedPropertyIds)
					{
						if (!creature.HasProperty(propertyId))
						{
							errors.Add($"Миссия '{mission.Id}': существо '{creature.Id}' не имеет свойства '{propertyId}'.");
						}
					}
				}

				if (mission.HasMissionEvent && !database.MissionEvents.ContainsKey(mission.MissionEventId!))
				{
					errors.Add($"Миссия '{mission.Id}': неизвестное событие миссии '{mission.MissionEventId}'.");
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

			foreach (Employee employee in database.StartingRoster)
			{
				ValidateEmployeeAbilities(database, employee, errors);
			}

			foreach (HireCandidate candidate in database.HirePool)
			{
				ValidateEmployeeAbilities(database, candidate.Template, errors);
			}

			ValidateGenerator(database, textCatalog, errors);
			ValidateMissionEvents(database, textCatalog, errors);
			ValidateMissionRules(database, textCatalog, errors);
			ValidatePersonnelArt(database, textCatalog, errors);
			if (database.Config.GetStaffLimit(1) < 1) errors.Add("employees: first-day staff limit must be at least one.");
			ValidateTextReferences(database, textCatalog, errors);

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

		private static void ValidateTextReferences(
			ContentDatabase database,
			ITextCatalog? textCatalog,
			List<string> errors)
		{
			if (textCatalog == null)
			{
				return;
			}

			foreach (KeyValuePair<string, MissionDefinition> pair in database.Missions)
			{
				MissionDefinition mission = pair.Value;
				ValidateTextEntry(textCatalog, mission.CallId, $"mission '{mission.Id}', callId", errors);
			}

			// Записка сменщика приходит игроку по id, а не значением: неверный id раньше
			// превратился бы в пустой лист на столе, теперь падает на загрузке.
			foreach (DayConfig day in database.Config.Days)
			{
				ValidateTextEntry(textCatalog, day.ShiftNoteId, $"day {day.Day}, shiftNoteId", errors);
			}
		}

		private static void ValidateGenerator(ContentDatabase database, ITextCatalog? textCatalog, List<string> errors)
		{
			EmployeeGeneratorSettings generator = database.Generator;
			if (generator.CandidatesPerShift <= 0) return;
			if (generator.Archetypes.Count == 0) errors.Add("employee generator: no archetypes.");
			if (generator.Surnames.Count == 0) errors.Add("employee generator: no surnames.");
			if (generator.CandidatesChoiceMargin < 0) errors.Add("employee generator: negative choice margin.");
			if (generator.MaxAge < generator.MinAge) errors.Add("employee generator: invalid age range.");
			if (generator.MaxStat < generator.MinStat) errors.Add("employee generator: invalid stat range.");
			if (generator.PrimaryWeight < generator.SecondaryWeight) errors.Add("employee generator: primary weight is below secondary weight.");
			int maxLevel = 1;
			foreach (LevelRange range in generator.LevelsByDay) maxLevel = Math.Max(maxLevel, range.MaxLevel);
			int capacity = (generator.MaxStat - generator.MinStat) * StatKinds.Count;
			int budget = generator.StatPointsBase + generator.StatPointsPerLevel * (maxLevel - 1);
			if (budget > capacity) errors.Add("employee generator: stat budget exceeds available stat capacity.");
			for (int i = 0; i < generator.Archetypes.Count; i++)
			{
				EmployeeArchetype archetype = generator.Archetypes[i];
				if (string.IsNullOrWhiteSpace(archetype.Id) || archetype.Weight <= 0 || archetype.PrimaryStats.Count == 0)
					errors.Add($"employee generator: invalid archetype at index {i}.");
				foreach (string abilityId in archetype.AbilityIds)
					if (!database.Abilities.ContainsKey(abilityId)) errors.Add($"employee generator: unknown ability '{abilityId}' in '{archetype.Id}'.");
			}
			foreach (LevelRange range in generator.LevelsByDay)
				if (range.FromDay < 1 || range.MinLevel < 1 || range.MaxLevel < range.MinLevel)
					errors.Add("employee generator: invalid level range.");
			if (textCatalog != null)
				foreach (string slot in generator.BioSlots)
					if (textCatalog.GetBioLines(slot).Count == 0) errors.Add($"employee generator: no bio text for slot '{slot}'.");
		}

		private static void ValidateMissionEvents(ContentDatabase database, ITextCatalog? textCatalog, List<string> errors)
		{
			foreach (KeyValuePair<string, MissionEventDefinition> pair in database.MissionEvents)
			{
				MissionEventDefinition missionEvent = pair.Value;
				if (missionEvent.Options.Count == 0) errors.Add($"mission event '{missionEvent.Id}' has no options.");
				var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (MissionEventOption option in missionEvent.Options)
				{
					if (string.IsNullOrWhiteSpace(option.Id) || !ids.Add(option.Id)) errors.Add($"mission event '{missionEvent.Id}' has an invalid option id.");
					if (!string.IsNullOrWhiteSpace(option.RequiresEquipmentId) && !database.Equipment.ContainsKey(option.RequiresEquipmentId))
						errors.Add($"mission event '{missionEvent.Id}': unknown equipment '{option.RequiresEquipmentId}'.");
				}
				if (textCatalog == null) continue;
				ValidateTextEntry(textCatalog, missionEvent.Id, $"mission event '{missionEvent.Id}'", errors);
				IReadOnlyList<TextOption> textOptions = textCatalog.GetOptions(missionEvent.Id);
				foreach (MissionEventOption option in missionEvent.Options)
				{
					bool found = false;
					for (int i = 0; i < textOptions.Count; i++) if (string.Equals(textOptions[i].Id, option.Id, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
					if (!found) errors.Add($"mission event '{missionEvent.Id}': no text option '{option.Id}'.");
				}
			}
		}

		private static void ValidateMissionRules(ContentDatabase database, ITextCatalog? textCatalog, List<string> errors)
		{
			foreach (KeyValuePair<string, MissionDefinition> pair in database.Missions)
			{
				MissionDefinition mission = pair.Value;
				MissionEventDefinition? missionEvent = mission.HasMissionEvent ? database.FindMissionEvent(mission.MissionEventId!) : null;
				if (mission.ManifestedPropertyIds.Count > 0 && string.IsNullOrWhiteSpace(mission.CreatureId)) errors.Add($"mission '{mission.Id}': properties require a creature.");
				if (mission.HasMissionEvent && mission.Tier != MissionTier.Story) errors.Add($"mission '{mission.Id}': radio event is only allowed for Story tier.");
				if (mission.Tier != MissionTier.Story && ConsequenceCaps.AllowsDeath(mission.EffectiveCap)) errors.Add($"mission '{mission.Id}': Filler tier cannot allow death.");
				if (mission.Requirements.Total == 0) errors.Add($"mission '{mission.Id}': at least one stat requirement is required.");
				if (mission.PrimaryStat.HasValue && mission.Requirements[mission.PrimaryStat.Value] <= 0) errors.Add($"mission '{mission.Id}': primary stat must be in requirements.");
				if (mission.SquadLimit < 1) errors.Add($"mission '{mission.Id}': squad limit must be at least one.");
				foreach (KeyValuePair<string, MissionReportPair> report in mission.Reports)
				{
					if (report.Key.Length > 0 && (missionEvent == null || missionEvent.FindOption(report.Key) == null)) errors.Add($"mission '{mission.Id}': report references an unknown option '{report.Key}'.");
					if (textCatalog != null)
					{
						ValidateTextEntry(textCatalog, report.Value.SuccessId, $"mission '{mission.Id}', success report", errors);
						ValidateTextEntry(textCatalog, report.Value.FailureId, $"mission '{mission.Id}', failure report", errors);
					}
				}
				if (missionEvent == null) continue;
				foreach (MissionEventOption option in missionEvent.Options)
				{
					if (option.ConsequenceCapOverride.HasValue && option.ConsequenceCapOverride.Value > mission.EffectiveCap) errors.Add($"mission event '{missionEvent.Id}', option '{option.Id}': consequence cap cannot loosen the mission cap.");
					foreach (StatKind kind in option.CheckedStats)
						if (!EventStatIsRequired(database, missionEvent.Id, kind)) errors.Add($"mission event '{missionEvent.Id}', option '{option.Id}': checked stat is not required by a linked mission.");
				}
			}
		}

		private static bool EventStatIsRequired(ContentDatabase database, string eventId, StatKind kind)
		{
			foreach (KeyValuePair<string, MissionDefinition> pair in database.Missions)
				if (string.Equals(pair.Value.MissionEventId, eventId, StringComparison.OrdinalIgnoreCase) && pair.Value.Requirements[kind] > 0) return true;
			return false;
		}

		private static void ValidatePersonnelArt(ContentDatabase database, ITextCatalog? textCatalog, List<string> errors)
		{
			EmployeeGeneratorSettings settings = database.Generator;
			if (!settings.IsEnabled) return;
			if (textCatalog != null)
				foreach (string slot in settings.BioSlots)
				{
					IReadOnlyList<string> lines = textCatalog.GetBioLines(slot);
					if (lines.Count == 0) errors.Add($"personnel bio: no lines for slot '{slot}'.");
					else settings.BioLinesBySlot[slot] = new List<string>(lines);
				}
			int needed = 0;
			for (int day = 1; day <= database.Config.Days.Count; day++) needed = Math.Max(needed, database.Config.GetStaffLimit(day) + Math.Max(settings.CandidatesPerShift, database.Config.GetStaffLimit(day) + settings.CandidatesChoiceMargin));
			int portraits = settings.PortraitIds.Count;
			foreach (EmployeeArchetype archetype in settings.Archetypes) portraits += archetype.PortraitIds.Count;
			if (portraits < needed) errors.Add($"personnel portraits: {portraits} available but {needed} can be visible at once.");
		}

		private static void ValidateTextEntry(ITextCatalog textCatalog, string contentId, string owner, List<string> errors)
		{
			if (!string.IsNullOrWhiteSpace(contentId) && !textCatalog.HasEntry(contentId))
			{
				errors.Add($"{owner}: missing text entry '{contentId}'.");
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
				Age = dto.Age,
				BaseStats = dto.Stats == null ? StatBlock.Zero : dto.Stats.ToModel()
			};

			if (dto.Abilities != null)
			{
				employee.AbilityIds.AddRange(dto.Abilities);
			}

			if (dto.Bio != null)
			{
				employee.BioIds.AddRange(dto.Bio);
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

		private static StatKind? ParsePrimaryStat(string? value, string missionId)
		{
			if (string.IsNullOrWhiteSpace(value)) return null;
			if (StatKinds.TryParse(value, out StatKind parsed)) return parsed;
			throw new ContentException($"Миссия '{missionId}': неизвестная главная характеристика '{value}'.");
		}

		private static ConsequenceCap? ParseOptionalCap(string? value, string file, string id)
		{
			return string.IsNullOrWhiteSpace(value)
				? null
				: ParseEnum(value, ConsequenceCap.Death, file, id, "consequenceCap");
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
