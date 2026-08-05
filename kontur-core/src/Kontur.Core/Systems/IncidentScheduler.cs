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

			int count;
			if (dayConfig.CallTimes.Count > 0)
			{
				count = dayConfig.CallTimes.Count;
			}
			else
			{
				int minCalls = dayConfig.MinCalls;
				int maxCalls = dayConfig.MaxCalls < minCalls ? minCalls : dayConfig.MaxCalls;
				count = _random.NextInt(minCalls, maxCalls + 1);
			}

			List<double> times = BuildCallTimes(count, timings, dayConfig);
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

		/// <summary>
		/// Времена звонков от начала смены.
		///
		/// Раньше здесь были равномерные слоты с джиттером, и на дне со «звонком
		/// ровно один» единственный вызов мог выпасть на четвёртую минуту — смена
		/// выглядела пустой. Хуже того, времена за пределами окна молча
		/// выбрасывались: чем больше вызовов просил день, тем меньше их доходило.
		///
		/// Теперь расписание жёсткое и предсказуемое: первый звонок через
		/// FirstCallSeconds, дальше через равные CallIntervalSeconds. Никакого
		/// броска, ничего не теряется. День, которому нужен свой ритм, задаёт
		/// его списком callTimes.
		/// </summary>
		private List<double> BuildCallTimes(int count, TimingConfig timings, DayConfig dayConfig)
		{
			var times = new List<double>();
			if (count <= 0)
			{
				return times;
			}

			if (dayConfig.CallTimes.Count > 0)
			{
				times.AddRange(dayConfig.CallTimes);
				times.Sort();
				return times;
			}

			double interval = timings.CallIntervalSeconds > 0.0
				? timings.CallIntervalSeconds
				: timings.MinSecondsBetweenCalls;

			for (int i = 0; i < count; i++)
			{
				times.Add(timings.FirstCallSeconds + (interval * i));
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
