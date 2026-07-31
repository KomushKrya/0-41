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
	/// 1. Требования миссии масштабируются: множитель дня × множитель радио-варианта.
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
			RadioOption? chosenOption,
			int day)
		{
			double multiplier = _config.GetDay(day).RequirementMultiplier;

			if (chosenOption != null)
			{
				multiplier *= chosenOption.RequirementMultiplier;
			}

			return mission.Requirements.Scale(multiplier);
		}

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
				total = total.Add(employee.GetEffectiveStats(_config.Employees.InjuryPenaltyPerStat));

				for (int a = 0; a < employee.AbilityIds.Count; a++)
				{
					Ability? ability = _content.FindAbility(employee.AbilityIds[a]);
					if (ability != null && ability.IsActive(creatureTags, equipmentIds))
					{
						total = total.Add(ability.GetEffectiveBonus());
					}
				}
			}

			// Снаряжение действует на всю группу целиком, а не на каждого сотрудника (ДД, раздел 6).
			for (int i = 0; i < equipment.Count; i++)
			{
				total = total.Add(equipment[i].GetEffectiveBonus());
			}

			return total;
		}

		public static double ComputeCoverage(StatBlock requirements, StatBlock squadStats)
		{
			int requiredTotal = 0;
			int deficit = 0;

			for (int i = 0; i < StatKinds.All.Length; i++)
			{
				StatKind kind = StatKinds.All[i];
				int required = requirements[kind];
				if (required <= 0)
				{
					continue;
				}

				requiredTotal += required;
				int missing = required - squadStats[kind];
				if (missing > 0)
				{
					deficit += missing;
				}
			}

			if (requiredTotal <= 0)
			{
				return 1.0;
			}

			double coverage = 1.0 - ((double)deficit / requiredTotal);
			return Math.Max(0.0, Math.Min(1.0, coverage));
		}

		public double ComputeSuccessChance(double coverage, IReadOnlyList<EquipmentDefinition> equipment, bool radioMissed)
		{
			ResolutionConfig resolution = _config.Resolution;

			double chance = Math.Pow(coverage, resolution.CoverageExponent);

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
				BuildingId = request.BuildingId,
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

			double coverage = ComputeCoverage(request.EffectiveRequirements, request.SquadStats);
			outcome.Coverage = coverage;

			bool isSuccess;
			if (coverage >= 1.0)
			{
				isSuccess = true;
				outcome.SuccessChance = 1.0;
				outcome.Reason = MissionResolutionReason.StatsCovered;
			}
			else
			{
				double chance = ComputeSuccessChance(coverage, request.Equipment, request.RadioWasMissed);
				double roll = _random.NextDouble();

				outcome.SuccessChance = chance;
				outcome.Roll = roll;

				isSuccess = roll < chance;
				outcome.Reason = isSuccess ? MissionResolutionReason.DiceSuccess : MissionResolutionReason.DiceFailure;
			}

			outcome.Kind = isSuccess ? MissionResultKind.Success : MissionResultKind.Failure;

			ApplyCasualties(request, outcome, coverage, isSuccess);

			outcome.SquadWiped = request.Squad.Count > 0 && outcome.KilledEmployeeIds.Count == request.Squad.Count;

			return outcome;
		}

		private void ApplyCasualties(ResolutionRequest request, MissionOutcome outcome, double coverage, bool isSuccess)
		{
			ResolutionConfig resolution = _config.Resolution;

			double riskFactor = 1.0 + ((1.0 - coverage) * resolution.RiskCoverageInfluence);

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

		public string BuildingId { get; set; } = string.Empty;

		public MissionDefinition Mission { get; set; } = new MissionDefinition();

		public IReadOnlyList<Employee> Squad { get; set; } = new List<Employee>();

		public IReadOnlyList<EquipmentDefinition> Equipment { get; set; } = new List<EquipmentDefinition>();

		public StatBlock EffectiveRequirements { get; set; } = StatBlock.Zero;

		public StatBlock SquadStats { get; set; } = StatBlock.Zero;

		public RadioOption? ChosenOption { get; set; }

		public bool RadioWasTriggered { get; set; }

		public bool RadioWasMissed { get; set; }
	}
}
