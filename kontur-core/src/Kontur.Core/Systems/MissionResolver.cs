using System;
using System.Collections.Generic;
using Kontur.Core.Config;
using Kontur.Core.Content;
using Kontur.Core.Model;
using Kontur.Core.Simulation;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Механика успеха миссии (ДД, раздел 7).
	///
	/// 1. Требования миссии масштабируются: множитель дня × множитель зоны × множитель радио-варианта.
	/// 2. Считается сумма характеристик всей группы + бонусы снаряжения + сработавшие спецспособности.
	/// 3. Покрыл требования полностью — автоматический успех без броска.
	/// 4. Не покрыл — бросок: шанс = покрытие^Exponent (согласованная кривая), потолок 95 %.
	/// 5. Пропущенное радио не даёт автопровала, но умножает шанс успеха на штрафной коэффициент.
	/// </summary>
	public sealed class MissionResolver
	{
		private readonly ContentDatabase _content;
		private readonly SimulationConfig _config;
		private readonly IRandomSource _random;

		public MissionResolver(ContentDatabase content, SimulationConfig config, IRandomSource random)
		{
			_content = content;
			_config = config;
			_random = random;
		}

		public StatBlock ComputeEffectiveRequirements(
			MissionDefinition mission,
			Zone? zone,
			ZoneSystem zoneSystem,
			MissionEventOption? chosenOption,
			int day)
		{
			double multiplier = _config.GetDay(day).RequirementMultiplier;

			if (zone != null)
			{
				multiplier *= zoneSystem.GetRequirementMultiplier(zone);
			}

			StatBlock requirements = mission.Requirements.Scale(multiplier);

			// Вариант сужает проверку до тех характеристик, которые назвал текст: пороги
			// берутся у миссии, но спрашивают только за то, чем игрок решил взять.
			// Вариант без списка проверок оставляет требования миссии как есть.
			if (chosenOption != null && chosenOption.CheckedStats.Count > 0)
			{
				requirements = chosenOption.ResolveRequirements(requirements);
			}

			// Вариант решения добавляет надбавку к каждой требуемой характеристике.
			// Именно надбавку, а не множитель: так число из текста читается одинаково
			// и на миссии с одной требуемой характеристикой, и на миссии с четырьмя.
			if (chosenOption != null && chosenOption.RequirementModifier != 0)
			{
				for (int i = 0; i < StatKinds.All.Length; i++)
				{
					StatKind kind = StatKinds.All[i];
					if (requirements[kind] > 0)
					{
						requirements = requirements.Add(kind, chosenOption.RequirementModifier);
					}
				}

				requirements = requirements.ClampMin(0);
			}

			return requirements;
		}

		/// <summary>
		/// Профиль группы: **сумма характеристик отряда**.
		///
		/// Порог «Интеллект 9» закрывают трое по три так же, как один с девяткой: миссию
		/// тянет группа целиком, и численность — такой же ресурс, как отдельный специалист.
		/// Поэтому отправить больше людей действительно помогает, а состав решает, чем
		/// именно группа сильна.
		///
		/// Способности считаются по каждому сотруднику отдельно и входят в сумму,
		/// снаряжение наоборот выдаётся на группу и добавляется поверх суммы один раз.
		/// </summary>
		public StatBlock ComputeSquadStats(
			IReadOnlyList<Employee> squad,
			IReadOnlyList<EquipmentDefinition> equipment,
			CreatureDefinition? creature)
		{
			var creatureTags = new List<string>();
			if (creature != null)
			{
				creatureTags.AddRange(creature.Tags);
			}

			var equipmentIds = new List<string>();
			for (int i = 0; i < equipment.Count; i++)
			{
				equipmentIds.Add(equipment[i].Id);
			}

			StatBlock total = StatBlock.Zero;

			for (int i = 0; i < squad.Count; i++)
			{
				Employee employee = squad[i];
				StatBlock profile = employee.GetEffectiveStats(_config.Employees.InjuryPenaltyPerStat);

				for (int a = 0; a < employee.AbilityIds.Count; a++)
				{
					Ability? ability = _content.FindAbility(employee.AbilityIds[a]);
					if (ability != null && ability.IsActive(creatureTags, equipmentIds))
					{
						profile = profile.Add(ability.GetEffectiveBonus());
					}
				}

				total = total.Add(profile);
			}

			// Снаряжение действует на группу целиком (ДД, раздел 6).
			for (int i = 0; i < equipment.Count; i++)
			{
				total = total.Add(equipment[i].GetEffectiveBonus());
			}

			return total;
		}

		/// <summary>
		/// Разбор профиля по характеристикам: что требуется, кто закрывает, насколько.
		/// Это же уходит на экран отправки — там строки красятся по Rating.
		/// </summary>
		public IReadOnlyList<StatMatch> EvaluateMatches(
			StatBlock requirements,
			StatBlock squadStats,
			StatKind? primaryStat)
		{
			StatMatchConfig match = _config.Match;
			var results = new List<StatMatch>();

			for (int i = 0; i < StatKinds.All.Length; i++)
			{
				StatKind kind = StatKinds.All[i];
				int required = requirements[kind];

				// Ноль означает «на этом вызове характеристика не нужна» — в расчёт не идёт.
				if (required <= 0)
				{
					continue;
				}

				int available = squadStats[kind];
				int margin = available - required;

				StatMatchRating rating;
				double score;

				if (margin >= match.ExceedsMargin)
				{
					rating = StatMatchRating.Exceeds;
					score = 1.0;
				}
				else if (margin >= 0)
				{
					rating = StatMatchRating.Meets;
					score = match.MeetsScore;
				}
				else
				{
					rating = StatMatchRating.Below;
					score = match.MeetsScore * Math.Pow(match.BelowFalloff, -margin);
				}

				bool isPrimary = primaryStat.HasValue && primaryStat.Value == kind;
				results.Add(new StatMatch(kind, required, available, isPrimary, rating, score));
			}

			return results;
		}

		/// <summary>
		/// Итоговое совпадение профилей, 0..1. Главная характеристика вызова весит вдвое.
		/// Требований нет вовсе — считается закрытым.
		/// </summary>
		public double ComputeMatchScore(IReadOnlyList<StatMatch> matches)
		{
			if (matches.Count == 0)
			{
				return 1.0;
			}

			StatMatchConfig config = _config.Match;
			double weighted = 0.0;
			double totalWeight = 0.0;

			for (int i = 0; i < matches.Count; i++)
			{
				double weight = matches[i].IsPrimary ? config.PrimaryWeight : config.SecondaryWeight;
				weighted += matches[i].Score * weight;
				totalWeight += weight;
			}

			return totalWeight <= 0.0 ? 1.0 : weighted / totalWeight;
		}

		/// <summary>Все пороги закрыты с запасом — успех без броска (зелёный профиль).</summary>
		public static bool IsPerfectMatch(IReadOnlyList<StatMatch> matches)
		{
			for (int i = 0; i < matches.Count; i++)
			{
				if (matches[i].Rating != StatMatchRating.Exceeds)
				{
					return false;
				}
			}

			return true;
		}

		public double ComputeSuccessChance(double matchScore, IReadOnlyList<EquipmentDefinition> equipment, bool radioMissed)
		{
			ResolutionConfig resolution = _config.Resolution;

			// Процент — это и есть совпадение профилей: кривую не накладываем, ступени
			// уже заложены в оценку каждой характеристики.
			double chance = matchScore;

			for (int i = 0; i < equipment.Count; i++)
			{
				chance += equipment[i].SuccessChanceBonus;
			}

			if (radioMissed)
			{
				chance *= resolution.RadioMissedChanceMultiplier;
			}

			return Math.Max(resolution.MinDiceChance, Math.Min(resolution.MaxDiceChance, chance));
		}

		/// <summary>Полный расчёт итога вызова, включая травмы и гибель.</summary>
		public MissionOutcome Resolve(ResolutionRequest request)
		{
			var outcome = new MissionOutcome
			{
				IncidentId = request.IncidentId,
				MissionId = request.Mission.Id,
				ZoneId = request.Mission.ZoneId,
				CreatureId = request.Mission.CreatureId,
				RadioWasTriggered = request.RadioWasTriggered,
				RadioWasMissed = request.RadioWasMissed,
				ChosenRadioOptionId = request.ChosenOption == null ? null : request.ChosenOption.Id,
				EffectiveRequirements = request.EffectiveRequirements,
				SquadStats = request.SquadStats
			};

			for (int i = 0; i < request.Squad.Count; i++)
			{
				outcome.EmployeeIds.Add(request.Squad[i].Id);
			}

			for (int i = 0; i < request.Equipment.Count; i++)
			{
				outcome.EquipmentIds.Add(request.Equipment[i].Id);
			}

			IReadOnlyList<StatMatch> matches = EvaluateMatches(
				request.EffectiveRequirements,
				request.SquadStats,
				request.Mission.PrimaryStat);

			double matchScore = ComputeMatchScore(matches);
			outcome.Coverage = matchScore;

			foreach (StatMatch match in matches)
			{
				outcome.StatMatches.Add(match);
			}

			bool isSuccess;
			if (IsPerfectMatch(matches))
			{
				// Все пороги закрыты с запасом — Dispatch называет это perfect match.
				isSuccess = true;
				outcome.SuccessChance = 1.0;
				outcome.Reason = MissionResolutionReason.StatsCovered;
			}
			else
			{
				double chance = ComputeSuccessChance(matchScore, request.Equipment, request.RadioWasMissed);
				double roll = _random.NextDouble();

				outcome.SuccessChance = chance;
				outcome.Roll = roll;

				isSuccess = roll < chance;
				outcome.Reason = isSuccess ? MissionResolutionReason.DiceSuccess : MissionResolutionReason.DiceFailure;
			}

			outcome.Kind = isSuccess ? MissionResultKind.Success : MissionResultKind.Failure;

			ApplyCasualties(request, outcome, matchScore, isSuccess);

			outcome.SquadWiped = request.Squad.Count > 0 && outcome.KilledEmployeeIds.Count == request.Squad.Count;

			return outcome;
		}

		private void ApplyCasualties(ResolutionRequest request, MissionOutcome outcome, double matchScore, bool isSuccess)
		{
			ResolutionConfig resolution = _config.Resolution;

			double riskFactor = 1.0 + ((1.0 - matchScore) * resolution.RiskCoverageInfluence);

			double injuryMultiplier = riskFactor;
			double deathMultiplier = riskFactor;

			if (isSuccess)
			{
				injuryMultiplier *= resolution.SuccessInjuryMultiplier;
				deathMultiplier *= resolution.SuccessDeathMultiplier;
			}

			if (request.ChosenOption != null)
			{
				injuryMultiplier *= request.ChosenOption.InjuryChanceMultiplier;
				deathMultiplier *= request.ChosenOption.DeathChanceMultiplier;
			}

			for (int i = 0; i < request.Equipment.Count; i++)
			{
				deathMultiplier *= request.Equipment[i].DeathChanceMultiplier;
			}

			double injuryChance = request.Mission.InjuryChance * injuryMultiplier;
			double deathChance = request.Mission.DeathChance * deathMultiplier;

			// Потолок последствий режет шансы последним — уже после всех множителей.
			// Иначе «безопасный» вызов мог бы убить через множитель варианта или снаряжения,
			// и обещание игроку зависело бы от порядка вычислений.
			ConsequenceCap cap = request.Mission.EffectiveCap;
			if (request.ChosenOption != null && request.ChosenOption.ConsequenceCapOverride != null)
			{
				cap = ConsequenceCaps.Tighten(cap, request.ChosenOption.ConsequenceCapOverride.Value);
			}

			outcome.AppliedCap = cap;

			if (!ConsequenceCaps.AllowsDeath(cap))
			{
				deathChance = 0.0;
			}

			if (!ConsequenceCaps.AllowsInjury(cap))
			{
				injuryChance = 0.0;
			}

			for (int i = 0; i < request.Squad.Count; i++)
			{
				Employee employee = request.Squad[i];

				if (_random.Chance(deathChance))
				{
					outcome.KilledEmployeeIds.Add(employee.Id);
					continue;
				}

				if (!employee.IsInjured && _random.Chance(injuryChance))
				{
					outcome.InjuredEmployeeIds.Add(employee.Id);
				}
			}
		}
	}

	/// <summary>Входные данные для расчёта — собираются ShiftDirector'ом.</summary>
	public sealed class ResolutionRequest
	{
		public string IncidentId { get; set; } = string.Empty;

		public MissionDefinition Mission { get; set; } = new MissionDefinition();

		public IReadOnlyList<Employee> Squad { get; set; } = new List<Employee>();

		public IReadOnlyList<EquipmentDefinition> Equipment { get; set; } = new List<EquipmentDefinition>();

		public StatBlock EffectiveRequirements { get; set; } = StatBlock.Zero;

		public StatBlock SquadStats { get; set; } = StatBlock.Zero;

		public MissionEventOption? ChosenOption { get; set; }

		public bool RadioWasTriggered { get; set; }

		public bool RadioWasMissed { get; set; }
	}
}
