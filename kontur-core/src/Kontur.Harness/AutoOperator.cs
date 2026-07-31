using System;
using System.Collections.Generic;
using Kontur.Core.Api;
using Kontur.Core.Content;
using Kontur.Core.Model;
using Kontur.Core.Simulation;
using Kontur.Core.Systems;

namespace Kontur.Harness
{
	public enum RadioStrategy
	{
		/// <summary>Всегда выбирает лучший по лору вариант — потолок баланса.</summary>
		Best = 0,

		/// <summary>Всегда выбирает заведомо провальный — пол баланса.</summary>
		Worst = 1,

		/// <summary>Случайный выбор — «средний игрок».</summary>
		Random = 2,

		/// <summary>Не отвечает по радио вообще — проверка штрафа за просрочку.</summary>
		Ignore = 3
	}

	/// <summary>
	/// Автопилот-«оператор». Заменяет живого игрока в headless-прогоне.
	/// Опрашивает состояние ровно так же, как это делал бы интерфейс в Godot,
	/// и отдаёт те же самые команды — то есть проверяет реальный публичный API ядра.
	/// </summary>
	public sealed class AutoOperator
	{
		private readonly GameSession _session;
		private readonly ContentDatabase _content;
		private readonly Random _random;

		public AutoOperator(GameSession session, ContentDatabase content, RadioStrategy strategy, int seed)
		{
			_session = session;
			_content = content;
			Strategy = strategy;
			_random = new Random(seed);
		}

		public RadioStrategy Strategy { get; set; }

		/// <summary>Задержка реакции на звонок, с. Больше 15 — вызовы будут пропускаться.</summary>
		public double AnswerDelay { get; set; } = 2.0;

		/// <summary>Задержка отправки группы, с. Больше 30 — метка будет истекать.</summary>
		public double DispatchDelay { get; set; } = 4.0;

		public double RadioDelay { get; set; } = 3.0;

		/// <summary>Сколько сотрудников максимум отправлять на один вызов.</summary>
		public int MaxSquadSize { get; set; } = 3;

		public void Update()
		{
			IReadOnlyList<IncidentView> incidents = _session.GetActiveIncidents();

			for (int i = 0; i < incidents.Count; i++)
			{
				IncidentView incident = incidents[i];

				switch (incident.Phase)
				{
					case IncidentPhase.Ringing:
						if (Elapsed(_session.Config.Timings.PhoneRingSeconds, incident.RemainingSeconds) >= AnswerDelay)
						{
							_session.AnswerCall(incident.Id);
						}

						break;

					case IncidentPhase.Briefing:
						_session.ConfirmBriefing(incident.Id);
						break;

					case IncidentPhase.MarkerActive:
						if (Elapsed(_session.Config.Timings.MapMarkerSeconds, incident.RemainingSeconds) >= DispatchDelay)
						{
							TryDispatch(incident);
						}

						break;

					case IncidentPhase.RadioPending:
						if (Strategy != RadioStrategy.Ignore
							&& Elapsed(_session.Config.Timings.RadioSeconds, incident.RemainingSeconds) >= RadioDelay)
						{
							ChooseRadio(incident);
						}

						break;
				}
			}
		}

		/// <summary>Межсменные действия: раздать очки навыков и добрать штат до лимита.</summary>
		public void BetweenShifts(int nextDay)
		{
			IReadOnlyList<EmployeeView> roster = _session.GetRoster();
			for (int i = 0; i < roster.Count; i++)
			{
				EmployeeView employee = roster[i];
				for (int point = 0; point < employee.UnspentSkillPoints; point++)
				{
					StatKind weakest = FindWeakestStat(employee.Stats);
					_session.SpendSkillPoint(employee.Id, weakest);
				}
			}

			int living = 0;
			roster = _session.GetRoster();
			for (int i = 0; i < roster.Count; i++)
			{
				if (roster[i].Status != EmployeeStatus.Dead)
				{
					living++;
				}
			}

			int limit = _content.Config.GetDay(nextDay).StaffLimit;
			IReadOnlyList<HireCandidateView> candidates = _session.GetHireCandidates(nextDay);

			for (int i = 0; i < candidates.Count && living < limit; i++)
			{
				CommandResult result = _session.HireEmployee(candidates[i].Id, nextDay);
				if (result.IsSuccess)
				{
					living++;
				}
			}
		}

		private static double Elapsed(double duration, double remaining)
		{
			return duration - remaining;
		}

		private void TryDispatch(IncidentView incident)
		{
			_session.OpenDispatchScreen(incident.Id);

			List<string> squad = PickSquad(incident.Requirements);
			if (squad.Count == 0)
			{
				return;
			}

			List<string> equipment = PickEquipment();
			CommandResult result = _session.DispatchSquad(incident.Id, squad, equipment);

			if (!result.IsSuccess && equipment.Count > 0)
			{
				// Снаряжение мог занять параллельный вызов — пробуем без него.
				_session.DispatchSquad(incident.Id, squad, Array.Empty<string>());
			}
		}

		private List<string> PickSquad(StatBlock requirements)
		{
			var available = new List<EmployeeView>();
			IReadOnlyList<EmployeeView> roster = _session.GetRoster();

			for (int i = 0; i < roster.Count; i++)
			{
				if (roster[i].Status == EmployeeStatus.Available)
				{
					available.Add(roster[i]);
				}
			}

			// Сначала те, кто лучше закрывает именно требуемые характеристики.
			available.Sort((left, right) => Relevance(right.Stats, requirements).CompareTo(Relevance(left.Stats, requirements)));

			var picked = new List<string>();
			StatBlock total = StatBlock.Zero;

			for (int i = 0; i < available.Count && picked.Count < MaxSquadSize; i++)
			{
				if (MissionResolver.ComputeCoverage(requirements, total) >= 1.0)
				{
					break;
				}

				picked.Add(available[i].Id);
				total = total.Add(available[i].Stats);
			}

			return picked;
		}

		private static int Relevance(StatBlock stats, StatBlock requirements)
		{
			int score = 0;
			for (int i = 0; i < StatKinds.All.Length; i++)
			{
				StatKind kind = StatKinds.All[i];
				if (requirements[kind] > 0)
				{
					score += stats[kind];
				}
			}

			return score;
		}

		private List<string> PickEquipment()
		{
			var result = new List<string>();
			int heavy = 0;
			int consumables = 0;

			IReadOnlyList<EquipmentSlotView> available = _session.GetAvailableEquipment();
			for (int i = 0; i < available.Count; i++)
			{
				EquipmentSlotView slot = available[i];

				if (slot.Kind == EquipmentKind.Consumable)
				{
					if (consumables >= _content.Config.Loot.ConsumableSlots)
					{
						continue;
					}

					consumables++;
				}
				else
				{
					if (heavy >= _content.Config.Loot.StandardOrStorySlots)
					{
						continue;
					}

					heavy++;
				}

				result.Add(slot.Id);
			}

			return result;
		}

		private void ChooseRadio(IncidentView incident)
		{
			MissionDefinition? mission = _content.FindMission(incident.MissionId);
			if (mission == null || mission.RadioEncounterId == null)
			{
				return;
			}

			RadioEncounter? encounter = _content.FindRadioEncounter(mission.RadioEncounterId);
			if (encounter == null || encounter.Options.Count == 0)
			{
				return;
			}

			RadioOption option;
			switch (Strategy)
			{
				case RadioStrategy.Best:
					option = FindByQuality(encounter, RadioOptionQuality.Best);
					break;
				case RadioStrategy.Worst:
					option = FindByQuality(encounter, RadioOptionQuality.Bad);
					break;
				default:
					option = encounter.Options[_random.Next(encounter.Options.Count)];
					break;
			}

			_session.ChooseRadioOption(incident.Id, option.Id);
		}

		private static RadioOption FindByQuality(RadioEncounter encounter, RadioOptionQuality quality)
		{
			for (int i = 0; i < encounter.Options.Count; i++)
			{
				if (encounter.Options[i].Quality == quality)
				{
					return encounter.Options[i];
				}
			}

			return encounter.Options[0];
		}

		private static StatKind FindWeakestStat(StatBlock stats)
		{
			StatKind weakest = StatKinds.All[0];
			int weakestValue = int.MaxValue;

			for (int i = 0; i < StatKinds.All.Length; i++)
			{
				StatKind kind = StatKinds.All[i];
				if (stats[kind] < weakestValue)
				{
					weakestValue = stats[kind];
					weakest = kind;
				}
			}

			return weakest;
		}
	}
}
