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

		/// <summary>Возвращает индексы абзацев, открытых этой миссией.</summary>
		public List<int> ProcessMissionResult(MissionDefinition mission, MissionOutcome outcome, RadioOption? chosenOption)
		{
			var revealed = new List<int>();

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
				_bus.Publish(new CreatureIdentified(creature.Id, creature.Name));
			}

			var propertyIds = new List<string>(mission.ManifestedPropertyIds);
			if (chosenOption != null && !string.IsNullOrEmpty(chosenOption.RevealsPropertyId))
			{
				propertyIds.Add(chosenOption.RevealsPropertyId!);
			}

			for (int i = 0; i < propertyIds.Count; i++)
			{
				CreatureProperty? property = creature.FindProperty(propertyIds[i]);
				if (property == null)
				{
					continue;
				}

				if (property.ParagraphIndex < 0 || property.ParagraphIndex >= creature.Paragraphs.Count)
				{
					continue;
				}

				if (!_state.Encyclopedia.RevealParagraph(creature.Id, property.ParagraphIndex))
				{
					continue;
				}

				revealed.Add(property.ParagraphIndex);
				_bus.Publish(new CreatureRevealed(
					creature.Id,
					creature.Name,
					property.ParagraphIndex,
					creature.Paragraphs[property.ParagraphIndex],
					property.Id));
			}

			return revealed;
		}
	}
}
