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

			IReadOnlyList<MissionDefinition> pool = _content.GetMissionsForDay(day);
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

			int minCalls = dayConfig.MinCalls;
			int maxCalls = dayConfig.MaxCalls < minCalls ? minCalls : dayConfig.MaxCalls;
			int count = _random.NextInt(minCalls, maxCalls + 1);

			List<double> times = BuildCallTimes(count, timings);
			var usedThisShift = new HashSet<string>();
			var usedBuildingIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

			for (int i = 0; i < times.Count; i++)
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

				var incident = new IncidentRuntime($"INC-{day:00}-{i + 1:00}", mission, building.Id)
				{
					ScheduledAtSeconds = times[i]
				};

				schedule.Add(incident);
			}

			return schedule;
		}

		/// <summary>
		/// Расписание по жёсткому списку миссий. Времена расставляются равномерно,
		/// но для первых SequentialCallCount вызовов они не важны: директор выпустит их
		/// по мере закрытия предыдущего.
		/// </summary>
		private List<IncidentRuntime> BuildScriptedSchedule(DayConfig dayConfig, TimingConfig timings, int day)
		{
			var schedule = new List<IncidentRuntime>();
			var usedBuildingIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			double gap = timings.MinSecondsBetweenCalls;

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
				schedule.Add(new IncidentRuntime($"INC-{day:00}-{i + 1:00}", mission, building.Id)
				{
					ScheduledAtSeconds = gap * i
				});
			}

			return schedule;
		}

		private List<double> BuildCallTimes(int count, TimingConfig timings)
		{
			var times = new List<double>();
			if (count <= 0)
			{
				return times;
			}

			// Равномерные слоты с джиттером — вызовы приходят вразнобой, но не пачкой.
			double window = timings.ShiftCallWindowSeconds;
			double slot = window / count;

			double previous = -timings.MinSecondsBetweenCalls;
			for (int i = 0; i < count; i++)
			{
				double slotStart = slot * i;
				double jitter = _random.NextDouble() * slot * 0.8;
				double time = slotStart + jitter;

				if (time - previous < timings.MinSecondsBetweenCalls)
				{
					time = previous + timings.MinSecondsBetweenCalls;
				}

				if (time > window)
				{
					break;
				}

				times.Add(time);
				previous = time;
			}

			return times;
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
