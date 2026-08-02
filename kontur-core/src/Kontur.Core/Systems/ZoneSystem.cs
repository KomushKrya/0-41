using Kontur.Core.Config;
using Kontur.Core.Events;
using Kontur.Core.Model;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Штриховка зон (ДД, раздел 9). Обновляется сразу по итогу вызова,
	/// а не между сменами — поэтому живёт отдельной системой, а не в конце смены.
	/// </summary>
	public sealed class ZoneSystem
	{
		private readonly GameState _state;
		private readonly ZoneConfig _config;
		private readonly IEventBus _bus;

		public ZoneSystem(GameState state, ZoneConfig config, IEventBus bus)
		{
			_state = state;
			_config = config;
			_bus = bus;
		}

		/// <summary>Вес зоны в планировщике вызовов: заражённые чаще, карантин реже, очищенные почти никогда.</summary>
		public double GetSpawnWeight(Zone zone)
		{
			double stateWeight;
			switch (zone.State)
			{
				case ZoneState.Infected:
					stateWeight = _config.WeightInfected;
					break;
				case ZoneState.Quarantine:
					stateWeight = _config.WeightQuarantine;
					break;
				case ZoneState.Cleared:
					stateWeight = _config.WeightCleared;
					break;
				default:
					stateWeight = _config.WeightNormal;
					break;
			}

			return zone.BaseWeight * stateWeight;
		}

		/// <summary>Заражённая зона делает вызовы сложнее, очищенная — легче (ДД, раздел 9).</summary>
		public double GetRequirementMultiplier(Zone zone)
		{
			switch (zone.State)
			{
				case ZoneState.Infected:
					return _config.InfectedRequirementMultiplier;
				case ZoneState.Cleared:
					return _config.ClearedRequirementMultiplier;
				default:
					return 1.0;
			}
		}

		public void ApplyMissionResult(Zone zone, bool isSuccess)
		{
			if (isSuccess)
			{
				zone.SuccessStreak++;
				zone.FailStreak = 0;

				if (zone.State == ZoneState.Infected && zone.SuccessStreak >= _config.SuccessStreakToClear)
				{
					SetState(zone, ZoneState.Normal, "серия успехов в заражённом районе");
					zone.SuccessStreak = 0;
				}
				else if (zone.State == ZoneState.Normal && zone.SuccessStreak >= _config.SuccessStreakToClear)
				{
					SetState(zone, ZoneState.Cleared, "район взят под контроль");
					zone.SuccessStreak = 0;
				}

				return;
			}

			zone.FailStreak++;
			zone.SuccessStreak = 0;

			if (zone.State == ZoneState.Cleared)
			{
				SetState(zone, ZoneState.Normal, "провал в контролируемом районе");
				zone.FailStreak = 0;
			}
			else if (zone.State != ZoneState.Infected && zone.FailStreak >= _config.FailStreakToInfect)
			{
				SetState(zone, ZoneState.Infected, "серия провалов");
				zone.FailStreak = 0;
			}
		}

		/// <summary>Выбор игрока по радио может закрыть район на карантин.</summary>
		public void ApplyQuarantine(Zone zone)
		{
			if (zone.State == ZoneState.Quarantine)
			{
				return;
			}

			SetState(zone, ZoneState.Quarantine, "решение оператора: карантин");
			zone.FailStreak = 0;
			zone.SuccessStreak = 0;
		}

		public void SetState(Zone zone, ZoneState newState, string reason)
		{
			if (zone.State == newState)
			{
				return;
			}

			ZoneState oldState = zone.State;
			zone.State = newState;
			_bus.Publish(new ZoneStateChanged(zone.Id, oldState, newState, reason));
		}
	}
}
