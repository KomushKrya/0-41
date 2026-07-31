using System.Collections.Generic;
using Kontur.Core.Content;
using Kontur.Core.Events;
using Kontur.Core.Model;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Раскрытие энциклопедии (ДД, раздел 10).
	/// Свойство раскрывается, только если оно проявилось на вызове И группа выжила
	/// (иначе некому доложить).
	/// </summary>
	public sealed class EncyclopediaSystem
	{
		private readonly GameState _state;
		private readonly ContentDatabase _content;
		private readonly IEventBus _bus;

		public EncyclopediaSystem(GameState state, ContentDatabase content, IEventBus bus)
		{
			_state = state;
			_content = content;
			_bus = bus;
		}

		/// <summary>Возвращает id свойств, открытых этой миссией.</summary>
		public List<string> ProcessMissionResult(MissionDefinition mission, MissionOutcome outcome, RadioOption? chosenOption)
		{
			var revealed = new List<string>();

			if (outcome.SquadWiped)
			{
				// Никто не вернулся — компьютер не может указать, с чем столкнулась группа.
				return revealed;
			}

			CreatureDefinition? creature = _content.FindCreature(mission.CreatureId);
			if (creature == null)
			{
				return revealed;
			}

			if (_state.Encyclopedia.Identify(creature.Id))
			{
				_bus.Publish(new CreatureIdentified(creature.Id));
			}

			var propertyIds = new List<string>(mission.ManifestedPropertyIds);
			if (chosenOption != null && !string.IsNullOrEmpty(chosenOption.RevealsPropertyId))
			{
				propertyIds.Add(chosenOption.RevealsPropertyId!);
			}

			for (int i = 0; i < propertyIds.Count; i++)
			{
				string propertyId = propertyIds[i];
				if (!creature.HasProperty(propertyId))
				{
					continue;
				}

				if (!_state.Encyclopedia.RevealProperty(creature.Id, propertyId))
				{
					continue;
				}

				revealed.Add(propertyId);
				_bus.Publish(new CreatureRevealed(creature.Id, propertyId));
			}

			return revealed;
		}
	}
}
