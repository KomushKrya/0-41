using System;
using System.Collections.Generic;
using Kontur.Core.Config;
using Kontur.Core.Content;
using Kontur.Core.Model;
using Kontur.Core.Simulation;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Планирует расписание вызовов на смену (ДД, раздел 3, п. 13–14):
	/// 5–10 вызовов, окно приёма — 5 минут, зона выбирается по своему весу (раздел 9).
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
			var usedBuildings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			for (int i = 0; i < times.Count; i++)
			{
				MissionDefinition? mission = PickMission(pool, usedThisShift);
				if (mission == null)
				{
					break;
				}

				usedThisShift.Add(mission.Id);
				_state.UsedMissionIds.Add(mission.Id);

				var incident = new IncidentRuntime($"INC-{day:00}-{i + 1:00}", mission)
				{
					ScheduledAtSeconds = times[i],
					BuildingId = PickBuilding(mission.ZoneId, usedBuildings)
				};

				if (incident.BuildingId.Length > 0)
				{
					usedBuildings.Add(incident.BuildingId);
				}

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
			double gap = timings.MinSecondsBetweenCalls;
			var usedBuildings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

				string buildingId = PickBuilding(mission.ZoneId, usedBuildings);
				if (buildingId.Length > 0)
				{
					usedBuildings.Add(buildingId);
				}

				schedule.Add(new IncidentRuntime($"INC-{day:00}-{i + 1:00}", mission)
				{
					ScheduledAtSeconds = gap * i,
					BuildingId = buildingId
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

		/// <summary>
		/// Дом под вызов. Два дома под один вызов за смену не выдаются: две метки
		/// в одном подъезде игрок прочитал бы как ошибку отрисовки.
		///
		/// Приоритет у домов нужного района. Если ни один дом района не подошёл — берём
		/// любой свободный: пустая карта хуже неточной. Пока районы в buildings.json
		/// не проставлены, работает как раз эта ветка.
		/// </summary>
		private string PickBuilding(string zoneId, HashSet<string> usedBuildings)
		{
			if (_content.Buildings.Count == 0)
			{
				return string.Empty;
			}

			var inZone = new List<BuildingDefinition>();
			var anywhere = new List<BuildingDefinition>();

			foreach (KeyValuePair<string, BuildingDefinition> pair in _content.Buildings)
			{
				BuildingDefinition building = pair.Value;
				if (!building.IsDispatchTarget || usedBuildings.Contains(building.Id))
				{
					continue;
				}

				anywhere.Add(building);

				if (building.ZoneId.Length > 0
					&& string.Equals(building.ZoneId, zoneId, StringComparison.OrdinalIgnoreCase))
				{
					inZone.Add(building);
				}
			}

			List<BuildingDefinition> candidates = inZone.Count > 0 ? inZone : anywhere;
			if (candidates.Count == 0)
			{
				return string.Empty;
			}

			// Сортировка перед выбором — ради воспроизводимости: порядок обхода словаря
			// не гарантирован, и без неё один и тот же сид давал бы разные дома.
			candidates.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
			return _random.Pick(candidates).Id;
		}

		private MissionDefinition? PickMission(IReadOnlyList<MissionDefinition> pool, HashSet<string> usedThisShift)
		{
			// Зона выбирается по своему весу, миссия — из числа привязанных к этой зоне.
			// Вес статичен: он задаёт характер района, а не реагирует на игру.
			var zoneIds = new List<string>();
			var weights = new List<double>();

			foreach (KeyValuePair<string, Zone> pair in _state.Zones)
			{
				zoneIds.Add(pair.Key);
				weights.Add(pair.Value.BaseWeight);
			}

			for (int attempt = 0; attempt < 8 && zoneIds.Count > 0; attempt++)
			{
				int zoneIndex = _random.PickWeightedIndex(weights);
				string zoneId = zoneIds[zoneIndex];

				var candidates = new List<MissionDefinition>();
				for (int i = 0; i < pool.Count; i++)
				{
					MissionDefinition mission = pool[i];
					if (usedThisShift.Contains(mission.Id))
					{
						continue;
					}

					if (string.Equals(mission.ZoneId, zoneId, System.StringComparison.OrdinalIgnoreCase))
					{
						candidates.Add(mission);
					}
				}

				if (candidates.Count > 0)
				{
					return _random.Pick(candidates);
				}
			}

			// Ни одной свободной миссии в выпавших зонах — берём любую неиспользованную.
			var fallback = new List<MissionDefinition>();
			for (int i = 0; i < pool.Count; i++)
			{
				if (!usedThisShift.Contains(pool[i].Id))
				{
					fallback.Add(pool[i]);
				}
			}

			return fallback.Count > 0 ? _random.Pick(fallback) : null;
		}
	}
}
