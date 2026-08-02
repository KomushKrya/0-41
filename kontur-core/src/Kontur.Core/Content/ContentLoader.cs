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
		public const string ZonesFile = "zones.json";

		public const string BuildingsFile = "buildings.json";
		public const string AbilitiesFile = "abilities.json";
		public const string EquipmentFile = "equipment.json";
		public const string CreaturesFile = "creatures.json";
		public const string RosterFile = "employees.json";
		public const string MissionsFile = "missions.json";
		public const string MissionEventsFile = "mission_events.json";

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

			LoadZones(source, database);
			LoadBuildings(source, database);
			LoadAbilities(source, database);
			LoadEquipment(source, database);
			LoadCreatures(source, database);
			LoadRoster(source, database);
			LoadMissionEvents(source, database, textCatalog);
			LoadMissions(source, database);

			Validate(database, textCatalog);
			return database;
		}

		/// <summary>
		/// Дома на карте. Файла может не быть: пока карта рисуется, ядро обходится
		/// одними зонами — метка тогда ставится по координатам района.
		/// </summary>
		private static void LoadBuildings(IContentSource source, ContentDatabase database)
		{
			if (!source.Exists(BuildingsFile))
			{
				return;
			}

			List<BuildingDto> buildings = ReadList<BuildingDto>(source, BuildingsFile);
			foreach (BuildingDto dto in buildings)
			{
				if (string.IsNullOrWhiteSpace(dto.Id))
				{
					throw new ContentException($"{BuildingsFile}: у здания отсутствует id.");
				}

				var building = new BuildingDefinition
				{
					Id = dto.Id,
					IsDispatchTarget = dto.IsDispatchTarget,
					IsHeadquarters = dto.IsHeadquarters,
					ZoneId = dto.ZoneId
				};

				if (database.Buildings.ContainsKey(building.Id))
				{
					throw new ContentException($"{BuildingsFile}: повторяющийся id '{building.Id}'.");
				}

				database.Buildings[building.Id] = building;
			}
		}

		private static void LoadZones(IContentSource source, ContentDatabase database)
		{
			List<ZoneDto> zones = ReadList<ZoneDto>(source, ZonesFile);
			foreach (ZoneDto dto in zones)
			{
				var zone = new Zone
				{
					Id = dto.Id,
					Name = dto.Name,
					BaseWeight = dto.BaseWeight,
					MapX = dto.MapX,
					MapY = dto.MapY
				};

				database.Zones[zone.Id] = zone;
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
					Name = dto.Name,
					Description = dto.Description,
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

			if (roster.Generator != null)
			{
				LoadGenerator(roster.Generator, database.Generator);
			}
		}

		/// <summary>
		/// Настройки фабрики кандидатов. Ошибки в названиях характеристик здесь не глушатся:
		/// опечатка в «strenght» тихо обнулила бы архетип, и кандидаты пошли бы одинаковые.
		/// </summary>
		private static void LoadGenerator(GeneratorDto dto, EmployeeGeneratorSettings settings)
		{
			settings.CandidatesPerShift = dto.CandidatesPerShift;
			settings.CandidatesChoiceMargin = dto.CandidatesChoiceMargin;
			settings.LevelLagBehindDay = dto.LevelLagBehindDay;
			settings.LevelSpread = dto.LevelSpread;
			settings.MinAge = dto.MinAge;
			settings.MaxAge = dto.MaxAge;

			if (dto.BioSlots != null)
			{
				settings.BioSlots.AddRange(dto.BioSlots);
			}
			settings.MinStat = dto.MinStat;
			settings.MaxStat = dto.MaxStat;
			settings.StatPointsBase = dto.StatPointsBase;
			settings.StatPointsPerLevel = dto.StatPointsPerLevel;
			settings.PrimaryWeight = dto.PrimaryWeight;
			settings.SecondaryWeight = dto.SecondaryWeight;
			settings.StartingChoicePoolSize = dto.StartingChoicePoolSize;
			settings.AbilitiesBase = dto.AbilitiesBase;
			settings.SecondAbilityFromLevel = dto.SecondAbilityFromLevel;

			if (dto.Surnames != null)
			{
				settings.Surnames.AddRange(dto.Surnames);
			}

			if (dto.Initials != null)
			{
				settings.Initials.AddRange(dto.Initials);
			}

			if (dto.Portraits != null)
			{
				settings.PortraitIds.AddRange(dto.Portraits);
			}

			if (dto.LevelsByDay != null)
			{
				foreach (LevelRangeDto range in dto.LevelsByDay)
				{
					settings.LevelsByDay.Add(new LevelRange
					{
						FromDay = range.FromDay,
						MinLevel = range.MinLevel,
						MaxLevel = range.MaxLevel
					});
				}

				settings.LevelsByDay.Sort((left, right) => left.FromDay.CompareTo(right.FromDay));
			}

			if (dto.Archetypes == null)
			{
				return;
			}

			foreach (ArchetypeDto archetypeDto in dto.Archetypes)
			{
				var archetype = new EmployeeArchetype
				{
					Id = archetypeDto.Id,
					Weight = archetypeDto.Weight,
					RankTitle = archetypeDto.RankTitle
				};

				AddStats(archetypeDto.Primary, archetype.PrimaryStats, archetypeDto.Id, "primary");
				AddStats(archetypeDto.Secondary, archetype.SecondaryStats, archetypeDto.Id, "secondary");

				if (archetypeDto.Abilities != null)
				{
					archetype.AbilityIds.AddRange(archetypeDto.Abilities);
				}

				if (archetypeDto.Portraits != null)
				{
					archetype.PortraitIds.AddRange(archetypeDto.Portraits);
				}

				settings.Archetypes.Add(archetype);
			}
		}

		private static void AddStats(List<string>? names, List<StatKind> target, string archetypeId, string field)
		{
			if (names == null)
			{
				return;
			}

			foreach (string name in names)
			{
				StatKind kind;
				if (!StatKinds.TryParse(name, out kind))
				{
					throw new ContentException(
						$"Архетип '{archetypeId}', поле '{field}': неизвестная характеристика '{name}'. " +
						"Допустимые: strength, combat, agility, charisma, intellect.");
				}

				target.Add(kind);
			}
		}

		/// <summary>
		/// Собирает вмешательство из двух половин: ключи и числа сложности приходят из текста
		/// (там их правит автор вместе с формулировкой), риски и последствия — из data.
		/// Расхождение наборов ключей — ошибка: молча потерянный вариант хуже упавшей загрузки.
		/// </summary>
		private static void LoadMissionEvents(IContentSource source, ContentDatabase database, ITextCatalog? textCatalog)
		{
			List<MissionEventDto> events = ReadList<MissionEventDto>(source, MissionEventsFile);

			foreach (MissionEventDto dto in events)
			{
				var missionEvent = new MissionEventDefinition { Id = dto.Id };
				var balance = dto.Options ?? new Dictionary<string, MissionEventOptionDto>();

				IReadOnlyList<TextOption> textOptions = textCatalog == null
					? new List<TextOption>()
					: textCatalog.GetOptions(dto.Id);

				if (textOptions.Count > 0)
				{
					// Порядок вариантов задаёт текст: игрок видит их в порядке файла.
					for (int i = 0; i < textOptions.Count; i++)
					{
						TextOption textOption = textOptions[i];
						MissionEventOptionDto? optionDto;
						balance.TryGetValue(textOption.Id, out optionDto);

						missionEvent.Options.Add(ToOption(textOption, optionDto, database.Config));
					}
				}
				else
				{
					// Без каталога (headless-прогон) остаётся только баланс: сложность нулевая,
					// порядок — как в файле данных. Для расчётов этого хватает.
					foreach (KeyValuePair<string, MissionEventOptionDto> pair in balance)
					{
						missionEvent.Options.Add(ToOption(
							new TextOption(pair.Key, MissionEventQuality.Neutral, null, new List<StatKind>()),
							pair.Value,
							database.Config));
					}
				}

				database.MissionEvents[missionEvent.Id] = missionEvent;
			}
		}

		/// <summary>
		/// Собирает вариант из текста и данных. Числа, которых автор не написал, берутся
		/// из умолчаний по типу диалога: `quality: bad` без модификатора всё равно будет
		/// дороже хорошего, и это не придётся держать в голове.
		/// </summary>
		private static MissionEventOption ToOption(
			TextOption textOption,
			MissionEventOptionDto? dto,
			SimulationConfig config)
		{
			MissionEventQualityConfig defaults = config.MissionEvents.For(textOption.Quality);

			return new MissionEventOption
			{
				Id = textOption.Id,
				Quality = textOption.Quality,
				CheckedStats = textOption.CheckedStats,
				RequirementModifier = textOption.RequirementModifier ?? defaults.RequirementModifier,
				DeathChanceMultiplier = dto == null || dto.DeathChanceMultiplier == null
					? defaults.RiskMultiplier
					: dto.DeathChanceMultiplier.Value,
				InjuryChanceMultiplier = dto == null || dto.InjuryChanceMultiplier == null
					? defaults.RiskMultiplier
					: dto.InjuryChanceMultiplier.Value,
				ExtraScales = dto == null || dto.ExtraScales == null ? ScaleDelta.Zero : dto.ExtraScales.ToModel(),
				RevealsPropertyId = dto == null ? null : dto.RevealsPropertyId,
				ConsequenceCapOverride = dto == null ? null : ParseOptionalCap(dto.ConsequenceCap, MissionEventsFile, textOption.Id)
			};
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
					Tier = ParseEnum(dto.Tier, MissionTier.Filler, MissionsFile, dto.Id, "tier"),
					ConsequenceCapOverride = ParseOptionalCap(dto.ConsequenceCap, MissionsFile, dto.Id),
					ZoneId = dto.ZoneId,
					CreatureId = dto.CreatureId,
					CallId = dto.CallId,
					MissionEventId = dto.MissionEventId,
					Requirements = dto.Requirements == null ? StatBlock.Zero : dto.Requirements.ToModel(),
					PrimaryStat = ParsePrimaryStat(dto.PrimaryStat, dto.Id),
					SquadLimit = dto.SquadLimit,
					TravelSeconds = dto.TravelSeconds,
					OnSiteSeconds = dto.OnSiteSeconds,
					ReturnSeconds = dto.ReturnSeconds,
					ScalesOnSuccess = ToDelta(dto.ScalesOnSuccess),
					ScalesOnFailure = ToDelta(dto.ScalesOnFailure),
					ScalesOnMissedCall = ToDelta(dto.ScalesOnMissedCall),
					ScalesOnExpiredMarker = ToDelta(dto.ScalesOnExpiredMarker),
					ExperienceOnSuccess = dto.ExperienceOnSuccess,
					ExperienceOnFailure = dto.ExperienceOnFailure,
					InjuryChance = dto.InjuryChance,
					DeathChance = dto.DeathChance,
				};

				if (dto.Reports != null)
				{
					foreach (KeyValuePair<string, ReportPairDto> pair in dto.Reports)
					{
						mission.Reports[pair.Key] = new MissionReportPair
						{
							SuccessId = pair.Value.Success,
							FailureId = pair.Value.Failure
						};
					}
				}

				if (dto.ManifestedPropertyIds != null)
				{
					mission.ManifestedPropertyIds.AddRange(dto.ManifestedPropertyIds);
				}

				database.Missions[mission.Id] = mission;
			}
		}

		private static void Validate(ContentDatabase database, ITextCatalog? textCatalog)
		{
			var errors = new List<string>();

			foreach (KeyValuePair<string, MissionDefinition> pair in database.Missions)
			{
				MissionDefinition mission = pair.Value;

				if (!database.Zones.ContainsKey(mission.ZoneId))
				{
					errors.Add($"Миссия '{mission.Id}': неизвестная зона '{mission.ZoneId}'.");
				}

				// Существо необязательно: не за каждой аномалией стоит статья энциклопедии.
				if (!string.IsNullOrEmpty(mission.CreatureId))
				{
					CreatureDefinition? creature = database.FindCreature(mission.CreatureId);
					if (creature == null)
					{
						errors.Add($"Миссия '{mission.Id}': неизвестное существо '{mission.CreatureId}'.");
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
				}
				else if (mission.ManifestedPropertyIds.Count > 0)
				{
					errors.Add($"Миссия '{mission.Id}': свойства заявлены, но существо не указано.");
				}

				MissionEventDefinition? missionEvent = null;
				if (mission.HasMissionEvent)
				{
					missionEvent = database.FindMissionEvent(mission.MissionEventId);
					if (missionEvent == null)
					{
						errors.Add($"Миссия '{mission.Id}': неизвестное вмешательство '{mission.MissionEventId}'.");
					}

					// Треск радио — обещание игроку, что вызов серьёзный. На филлере
					// это обещание было бы ложным.
					if (mission.Tier != MissionTier.Story)
					{
						errors.Add(
							$"Миссия '{mission.Id}': вмешательство по радио бывает только у сюжетных вызовов, " +
							$"а tier={mission.Tier}.");
					}
				}

				if (mission.Tier != MissionTier.Story && ConsequenceCaps.AllowsDeath(mission.EffectiveCap))
				{
					errors.Add(
						$"Миссия '{mission.Id}': филлерный вызов не может позволять гибель " +
						$"(consequenceCap={mission.EffectiveCap}).");
				}

				ValidateOptionCaps(mission, missionEvent, errors);

				if (mission.Requirements.Total == 0)
				{
					errors.Add($"Миссия '{mission.Id}': не задано ни одного порога по характеристикам.");
				}

				if (mission.PrimaryStat.HasValue && mission.Requirements[mission.PrimaryStat.Value] <= 0)
				{
					errors.Add(
						$"Миссия '{mission.Id}': главная характеристика " +
						$"{StatKinds.GetDisplayName(mission.PrimaryStat.Value)} не входит в требования.");
				}

				ValidateMissionTexts(mission, missionEvent, textCatalog, errors);
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

			foreach (KeyValuePair<string, MissionDefinition> pair in database.Missions)
			{
				MissionDefinition mission = pair.Value;
				if (mission.SquadLimit < 1)
				{
					errors.Add(
						$"Миссия '{mission.Id}': squadLimit={mission.SquadLimit} — " +
						"отправить некого. Минимум один.");
				}
			}

			ValidatePersonnelArt(database, textCatalog, errors);

			// Лимит штата считается как номер смены плюс смещение. Смещение меньше
			// нуля дало бы на первой смене штат из одного человека или пустой отдел,
			// и игра встала бы на первом же вызове.
			if (database.Config.GetStaffLimit(1) < 1)
			{
				errors.Add(
					$"employees.staffLimitOffset={database.Config.Employees.StaffLimitOffset}: " +
					"на первой смене в штате не остаётся никого.");
			}

			ValidateGenerator(database, errors);
			ValidateBuildings(database, errors);

			foreach (KeyValuePair<string, MissionEventDefinition> pair in database.MissionEvents)
			{
				if (pair.Value.Options.Count == 0)
				{
					errors.Add($"Вмешательство '{pair.Key}': нет вариантов решения.");
				}

				if (textCatalog == null)
				{
					continue;
				}

				if (!textCatalog.HasEntry(pair.Key))
				{
					errors.Add($"Вмешательство '{pair.Key}': нет записи mission_event с таким id.");
					continue;
				}

				// Ключи вариантов должны совпадать в тексте и в данных: вариант без баланса
				// молча получил бы нейтральные риски, а баланс без текста — не показался бы игроку.
				var inText = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				IReadOnlyList<TextOption> textOptions = textCatalog.GetOptions(pair.Key);
				for (int i = 0; i < textOptions.Count; i++)
				{
					inText.Add(textOptions[i].Id);
				}

				foreach (MissionEventOption option in pair.Value.Options)
				{
					if (!inText.Contains(option.Id))
					{
						errors.Add($"Вмешательство '{pair.Key}': вариант '{option.Id}' есть в данных, но не в тексте.");
					}
				}

				// Проверять, остался ли «вариант для слабых», незачем: выбрать можно любой.
				// А вот проверка по характеристике, которой нет в требованиях миссии, —
				// молчаливый автоуспех: порог подставится нулевой. Это ошибка данных.
				foreach (MissionEventOption option in pair.Value.Options)
				{
					for (int i = 0; i < option.CheckedStats.Count; i++)
					{
						StatKind kind = option.CheckedStats[i];
						if (!EventStatIsRequired(database, pair.Key, kind))
						{
							errors.Add(
								$"Вмешательство '{pair.Key}': вариант '{option.Id}' проверяет "
								+ $"{StatKinds.GetDisplayName(kind)}, но у миссии нет такого требования — "
								+ "подставится ноль и проверка станет бесплатной.");
						}
					}
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

		/// <summary>
		/// Есть ли у миссии, которая запускает это вмешательство, порог по такой характеристике.
		/// Вариант берёт числа у своей миссии, поэтому проверка по «чужой» характеристике
		/// оказалась бы бесплатной.
		/// </summary>
		private static bool EventStatIsRequired(ContentDatabase database, string eventId, StatKind kind)
		{
			foreach (KeyValuePair<string, MissionDefinition> pair in database.Missions)
			{
				if (string.Equals(pair.Value.MissionEventId, eventId, StringComparison.OrdinalIgnoreCase)
					&& pair.Value.Requirements[kind] > 0)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Сверяет id текстов миссии с каталогом. Без каталога проверок нет — ядро
		/// должно прогоняться и без движка.
		/// </summary>
		/// <summary>Вариант умеет только ужесточать потолок миссии, но не ослаблять его.</summary>
		private static void ValidateOptionCaps(
			MissionDefinition mission,
			MissionEventDefinition? missionEvent,
			List<string> errors)
		{
			if (missionEvent == null)
			{
				return;
			}

			foreach (MissionEventOption option in missionEvent.Options)
			{
				if (option.ConsequenceCapOverride == null)
				{
					continue;
				}

				if (option.ConsequenceCapOverride.Value > mission.EffectiveCap)
				{
					errors.Add(
						$"Вмешательство '{missionEvent.Id}', вариант '{option.Id}': потолок " +
						$"{option.ConsequenceCapOverride.Value} мягче, чем у миссии '{mission.Id}' " +
						$"({mission.EffectiveCap}) — вариант может только ужесточать.");
				}
			}
		}

		private static void ValidateMissionTexts(
			MissionDefinition mission,
			MissionEventDefinition? missionEvent,
			ITextCatalog? textCatalog,
			List<string> errors)
		{
			if (string.IsNullOrEmpty(mission.CallId))
			{
				errors.Add($"Миссия '{mission.Id}': не указан callId — игроку нечего услышать в трубке.");
			}

			if (textCatalog == null)
			{
				return;
			}

			if (!string.IsNullOrEmpty(mission.CallId) && !textCatalog.HasEntry(mission.CallId))
			{
				errors.Add($"Миссия '{mission.Id}': нет записи call с id '{mission.CallId}'.");
			}

			foreach (KeyValuePair<string, MissionReportPair> pair in mission.Reports)
			{
				if (pair.Key.Length > 0 && missionEvent != null && missionEvent.FindOption(pair.Key) == null)
				{
					errors.Add($"Миссия '{mission.Id}': отчёт привязан к варианту '{pair.Key}', которого нет во вмешательстве.");
				}

				CheckReport(mission, pair.Value.SuccessId, textCatalog, errors);
				CheckReport(mission, pair.Value.FailureId, textCatalog, errors);
			}
		}

		private static void CheckReport(MissionDefinition mission, string reportId, ITextCatalog textCatalog, List<string> errors)
		{
			if (!string.IsNullOrEmpty(reportId) && !textCatalog.HasEntry(reportId))
			{
				errors.Add($"Миссия '{mission.Id}': нет записи report с id '{reportId}'.");
			}
		}

		/// <summary>
		/// Досье и портреты кандидатов.
		///
		/// Фразы досье живут в текстовом движке, а не в data/employees.json: второй
		/// список тех же id разошёлся бы с текстами на первой правке. Здесь они
		/// вытягиваются из каталога один раз при загрузке — фабрике движок уже не нужен.
		///
		/// Портреты проверяются на количество. Два одинаковых лица на экране игрок
		/// читает как ошибку игры, поэтому лучше сказать художнику при старте, сколько
		/// картинок не хватает, чем показать близнецов посреди смены.
		/// </summary>
		private static void ValidatePersonnelArt(
			ContentDatabase database,
			ITextCatalog? textCatalog,
			List<string> errors)
		{
			EmployeeGeneratorSettings settings = database.Generator;
			if (!settings.IsEnabled)
			{
				return;
			}

			if (textCatalog != null)
			{
				for (int i = 0; i < settings.BioSlots.Count; i++)
				{
					string slot = settings.BioSlots[i];
					IReadOnlyList<string> lines = textCatalog.GetBioLines(slot);

					if (lines.Count == 0)
					{
						errors.Add(
							$"Досье: в слоте '{slot}' нет ни одной фразы. Ожидаются записи " +
							$"типа bio_line в content/raw/<локаль>/personnel/bio/{slot}/.");
						continue;
					}

					settings.BioLinesBySlot[slot] = new List<string>(lines);
				}
			}

			// Худший случай: весь штат жив, и рядом полный список найма.
			int needed = 0;
			for (int day = 1; day <= database.Config.Days.Count; day++)
			{
				int limit = database.Config.GetStaffLimit(day);
				int candidates = limit + settings.CandidatesChoiceMargin;
				if (candidates < settings.CandidatesPerShift)
				{
					candidates = settings.CandidatesPerShift;
				}

				int onScreen = limit + candidates;
				if (onScreen > needed)
				{
					needed = onScreen;
				}
			}

			int available = settings.PortraitIds.Count;
			for (int i = 0; i < settings.Archetypes.Count; i++)
			{
				available += settings.Archetypes[i].PortraitIds.Count;
			}

			if (available < needed)
			{
				errors.Add(
					$"Портретов в пуле {available}, а на экране одновременно бывает до {needed} " +
					"человек (штат плюс список найма в самый тяжёлый день). Не хватает " +
					$"{needed - available}: либо добавьте картинки в generator.portraits, " +
					"либо уменьшите candidatesChoiceMargin.");
			}
		}

		/// <summary>
		/// Проверки фабрики. Все они ловят ошибки, которые иначе всплыли бы не как падение,
		/// а как странный баланс через час игры: кандидаты без перков, потолок ниже минимума,
		/// бюджет, который некуда потратить.
		/// </summary>
		private static void ValidateGenerator(ContentDatabase database, List<string> errors)
		{
			EmployeeGeneratorSettings settings = database.Generator;
			if (settings.CandidatesPerShift <= 0)
			{
				// Фабрика выключена намеренно — предлагаются только прописанные кандидаты.
				return;
			}

			if (settings.Archetypes.Count == 0)
			{
				errors.Add("Фабрика кандидатов: включена (candidatesPerShift > 0), но нет ни одного архетипа.");
			}

			// Отрицательный запас урезал бы список ниже числа свободных мест, и штат
			// перестал бы добираться — ровно та поломка, ради которой запас и вводился.
			if (settings.CandidatesChoiceMargin < 0)
			{
				errors.Add(
					$"Фабрика кандидатов: candidatesChoiceMargin={settings.CandidatesChoiceMargin} — " +
					"кандидатов станет меньше, чем свободных мест, и штат будет нечем добрать.");
			}

			if (settings.Surnames.Count == 0)
			{
				errors.Add("Фабрика кандидатов: пустой список фамилий.");
			}

			if (settings.MaxStat < settings.MinStat)
			{
				errors.Add(
					$"Фабрика кандидатов: потолок характеристики ({settings.MaxStat}) " +
					$"ниже стартового значения ({settings.MinStat}).");
			}

			// Весь бюджет должен помещаться в пять характеристик, иначе очки просто пропадут.
			int capacity = (settings.MaxStat - settings.MinStat) * StatKinds.Count;
			int maxLevel = 1;
			for (int i = 0; i < settings.LevelsByDay.Count; i++)
			{
				if (settings.LevelsByDay[i].MaxLevel > maxLevel)
				{
					maxLevel = settings.LevelsByDay[i].MaxLevel;
				}
			}

			int maxBudget = settings.StatPointsBase + (settings.StatPointsPerLevel * (maxLevel - 1));
			if (maxBudget > capacity)
			{
				errors.Add(
					$"Фабрика кандидатов: на {maxLevel} уровне бюджет {maxBudget} очков, " +
					$"а вместить характеристики могут только {capacity}. Поднимите maxStat или срежьте statPointsPerLevel.");
			}

			// Перевёрнутые веса дают силуэт, обратный задуманному: второстепенная
			// характеристика окажется выше основных. На глаз это не видно вообще.
			if (settings.PrimaryWeight < settings.SecondaryWeight)
			{
				errors.Add(
					$"Фабрика кандидатов: primaryWeight ({settings.PrimaryWeight}) меньше " +
					$"secondaryWeight ({settings.SecondaryWeight}) — второстепенные характеристики " +
					"окажутся выше основных. Если это и нужно, поменяйте местами primary и secondary.");
			}

			foreach (EmployeeArchetype archetype in settings.Archetypes)
			{
				if (archetype.PrimaryStats.Count == 0)
				{
					errors.Add($"Архетип '{archetype.Id}': не указана ни одна основная характеристика.");
				}

				if (archetype.Weight <= 0.0)
				{
					errors.Add($"Архетип '{archetype.Id}': вес {archetype.Weight} — такой архетип никогда не выпадет.");
				}

				foreach (string abilityId in archetype.AbilityIds)
				{
					if (!database.Abilities.ContainsKey(abilityId))
					{
						errors.Add($"Архетип '{archetype.Id}': неизвестная способность '{abilityId}'.");
					}
				}
			}

			foreach (LevelRange range in settings.LevelsByDay)
			{
				if (range.MinLevel > range.MaxLevel)
				{
					errors.Add(
						$"Фабрика кандидатов, день {range.FromDay}: minLevel {range.MinLevel} " +
						$"больше maxLevel {range.MaxLevel}.");
				}
			}
		}

		/// <summary>
		/// Дома на карте. Проверок немного, но каждая ловит поломку, которая иначе
		/// выглядела бы как «метки почему-то не появляются».
		/// </summary>
		private static void ValidateBuildings(ContentDatabase database, List<string> errors)
		{
			if (database.Buildings.Count == 0)
			{
				// Домов нет вовсе — это допустимо: метка ставится по координатам зоны.
				return;
			}

			int dispatchTargets = 0;
			int headquarters = 0;

			foreach (KeyValuePair<string, BuildingDefinition> pair in database.Buildings)
			{
				BuildingDefinition building = pair.Value;

				if (building.IsDispatchTarget)
				{
					dispatchTargets++;
				}

				if (building.IsHeadquarters)
				{
					headquarters++;

					if (building.IsDispatchTarget)
					{
						errors.Add(
							$"Здание '{building.Id}': помечено и как главное управление, и как цель " +
							"отправки — группа получила бы вызов в собственную контору.");
					}
				}

				if (building.ZoneId.Length > 0 && !database.Zones.ContainsKey(building.ZoneId))
				{
					errors.Add($"Здание '{building.Id}': неизвестный район '{building.ZoneId}'.");
				}
			}

			if (dispatchTargets == 0)
			{
				errors.Add(
					$"{BuildingsFile}: нет ни одного здания с isDispatchTarget — " +
					"отправлять группу будет некуда.");
			}

			if (headquarters > 1)
			{
				errors.Add($"{BuildingsFile}: главных управлений {headquarters}, а должно быть одно.");
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

		private static StatKind? ParsePrimaryStat(string? value, string missionId)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			StatKind parsed;
			if (!StatKinds.TryParse(value, out parsed))
			{
				throw new ContentException(
					$"Миссия '{missionId}': неизвестная главная характеристика '{value}'.");
			}

			return parsed;
		}

		private static ConsequenceCap? ParseOptionalCap(string? value, string file, string id)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			return ParseEnum(value, ConsequenceCap.Death, file, id, "consequenceCap");
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
