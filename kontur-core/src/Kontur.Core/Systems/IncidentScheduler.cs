using System.Collections.Generic;
using Kontur.Core.Config;
using Kontur.Core.Content;
using Kontur.Core.Model;
using Kontur.Core.Simulation;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Планирует расписание вызовов на смену (ДД, раздел 3, п. 13–14):
	/// 5–10 вызовов и окно приёма в 5 минут. Здание назначается каждому инциденту из каталога доступных целей.
	/// Расписание строится один раз в начале смены — так прогон детерминирован и его легко логировать.
	/// </summary>
	public sealed class IncidentScheduler
	{
		private readonly ContentDatabase _content;
		private readonly GameState _state;
		private readonly IRandomSource _random;

		public IncidentScheduler(ContentDatabase content, GameState state, IRandomSource random)
		{
			_content = content;
			_state = state;
			_random = random;
		}

		public List<IncidentRuntime> BuildSchedule(int day)
		{
			DayConfig dayConfig = _content.Config.GetDay(day);
			TimingConfig timings = _content.Config.Timings;

			IReadOnlyList<MissionDefinition> pool = FilterByFlags(_content.GetMissionsForDay(day));
			var schedule = new List<IncidentRuntime>();

			if (pool.Count == 0)
			{
				return schedule;
			}

			// Сценарная смена (обучение): порядок задан контентом, случайный подбор не работает.
			if (dayConfig.IsScripted)
			{
				return BuildScriptedSchedule(dayConfig, timings, day);
			}

			// Смена отыгрывает весь пул дня: сколько миссий написано на день, столько
			// вызовов и придёт. Порядок тасуется здесь, а момент каждого звонка решает
			// директор — пауза отсчитывается от закрытия предыдущего разговора.
			var usedThisShift = new HashSet<string>();
			var usedBuildingIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

			for (int i = 0; i < pool.Count; i++)
			{
				MissionDefinition? mission = PickMission(pool, usedThisShift);
				BuildingDefinition? building = PickBuilding(usedBuildingIds);
				if (mission == null || building == null)
				{
					break;
				}

				usedThisShift.Add(mission.Id);
				usedBuildingIds.Add(building.Id);
				_state.UsedMissionIds.Add(mission.Id);

				// Время не назначаем: очередь выпускает вызовы по паузе, а не по расписанию.
				schedule.Add(new IncidentRuntime($"INC-{day:00}-{i + 1:00}", mission, building.Id));
			}

			return schedule;
		}

		/// <summary>
		/// Расписание по жёсткому списку миссий. Времена расставляются равномерно,
		/// но для первых SequentialCallCount вызовов они не важны: директор выпустит их
		/// по мере закрытия предыдущего.
		/// </summary>
		/// <summary>
		/// Выкидывает из пула миссии, чей звонок требует невыставленного флага: так
		/// взаимоисключающие ветки не приходят обе в одну смену.
		/// </summary>
		private List<MissionDefinition> FilterByFlags(IReadOnlyList<MissionDefinition> pool)
		{
			var allowed = new List<MissionDefinition>(pool.Count);
			for (int i = 0; i < pool.Count; i++)
			{
				bool ok = true;
				IReadOnlyList<string> required = pool[i].RequiredFlags;
				for (int f = 0; ok && f < required.Count; f++)
				{
					ok = _state.Flags.IsSet(required[f]);
				}

				if (ok)
				{
					allowed.Add(pool[i]);
				}
			}

			return allowed;
		}

		private List<IncidentRuntime> BuildScriptedSchedule(DayConfig dayConfig, TimingConfig timings, int day)
		{
			var schedule = new List<IncidentRuntime>();
			var usedBuildingIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

			for (int i = 0; i < dayConfig.MissionOrder.Count; i++)
			{
				string missionId = dayConfig.MissionOrder[i];
				MissionDefinition? mission = _content.FindMission(missionId);

				if (mission == null)
				{
					// Молча пропускать нельзя: сценарий смены — авторская работа,
					// опечатка в Id должна быть заметна сразу.
					throw new Kontur.Core.Content.ContentException(
						$"config.json, день {day}: в missionOrder указана неизвестная миссия '{missionId}'.");
				}

				_state.UsedMissionIds.Add(mission.Id);

				BuildingDefinition? building = PickBuilding(usedBuildingIds);
				if (building == null)
				{
					throw new Kontur.Core.Content.ContentException(
						$"{ContentLoader.BuildingsFile}: не хватает зданий для сценария смены {day}.");
				}

				usedBuildingIds.Add(building.Id);
				schedule.Add(new IncidentRuntime($"INC-{day:00}-{i + 1:00}", mission, building.Id));
			}

			return schedule;
		}

		private MissionDefinition? PickMission(IReadOnlyList<MissionDefinition> pool, HashSet<string> usedThisShift)
		{
			var candidates = new List<MissionDefinition>();
			for (int index = 0; index < pool.Count; index++)
			{
				MissionDefinition mission = pool[index];
				if (!usedThisShift.Contains(mission.Id))
				{
					candidates.Add(mission);
				}
			}

			return candidates.Count > 0 ? _random.Pick(candidates) : null;

		}

		private BuildingDefinition? PickBuilding(HashSet<string> usedBuildingIds)
		{
			var candidates = new List<BuildingDefinition>();
			foreach (KeyValuePair<string, BuildingDefinition> pair in _content.Buildings)
			{
				BuildingDefinition building = pair.Value;
				if (building.IsDispatchTarget && !usedBuildingIds.Contains(building.Id))
				{
					candidates.Add(building);
				}
			}

			candidates.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
			return candidates.Count > 0 ? _random.Pick(candidates) : null;
		}
	}
}
