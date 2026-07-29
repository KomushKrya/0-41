using System.Collections.Generic;
using Kontur.Core.Config;
using Kontur.Core.Content;
using Kontur.Core.Events;
using Kontur.Core.Model;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Штат: опыт, уровни, травмы, гибель, найм (ДД, раздел 5).
	/// Лимит штата растёт по дням: 3 / 4 / 5 / 6.
	/// </summary>
	public sealed class RosterSystem
	{
		private readonly GameState _state;
		private readonly ContentDatabase _content;
		private readonly EmployeeConfig _config;
		private readonly IEventBus _bus;

		public RosterSystem(GameState state, ContentDatabase content, EmployeeConfig config, IEventBus bus)
		{
			_state = state;
			_content = content;
			_config = config;
			_bus = bus;
		}

		public int GetStaffLimit(int day)
		{
			return _content.Config.GetDay(day).StaffLimit;
		}

		public int CountLiving()
		{
			return _state.CountLivingEmployees();
		}

		/// <summary>Начало смены: травмы снимаются, все живые снова доступны.</summary>
		public void BeginShift()
		{
			for (int i = 0; i < _state.Roster.Count; i++)
			{
				Employee employee = _state.Roster[i];
				if (!employee.IsAlive)
				{
					continue;
				}

				employee.IsInjured = false;
				employee.Status = EmployeeStatus.Available;
				employee.CurrentIncidentId = null;
			}
		}

		public void MarkOnMission(IReadOnlyList<Employee> squad, string incidentId)
		{
			for (int i = 0; i < squad.Count; i++)
			{
				squad[i].Status = EmployeeStatus.OnMission;
				squad[i].CurrentIncidentId = incidentId;
			}
		}

		public void MarkReturned(IReadOnlyList<string> employeeIds)
		{
			for (int i = 0; i < employeeIds.Count; i++)
			{
				Employee? employee = _state.FindEmployee(employeeIds[i]);
				if (employee == null || !employee.IsAlive)
				{
					continue;
				}

				employee.Status = EmployeeStatus.Available;
				employee.CurrentIncidentId = null;
			}
		}

		public void ApplyInjury(Employee employee, string incidentId)
		{
			if (!employee.IsAlive || employee.IsInjured)
			{
				return;
			}

			employee.IsInjured = true;
			_bus.Publish(new EmployeeInjured(employee.Id, employee.Name, incidentId));
		}

		public void ApplyDeath(Employee employee, string incidentId)
		{
			if (!employee.IsAlive)
			{
				return;
			}

			employee.Status = EmployeeStatus.Dead;
			employee.CurrentIncidentId = null;
			_bus.Publish(new EmployeeKilled(employee.Id, employee.Name, incidentId));
		}

		/// <summary>Успешный вызов даёт полный опыт, неуспешный — небольшой (ДД, раздел 5).</summary>
		public void GrantExperience(IReadOnlyList<string> employeeIds, int amount)
		{
			if (amount <= 0)
			{
				return;
			}

			for (int i = 0; i < employeeIds.Count; i++)
			{
				Employee? employee = _state.FindEmployee(employeeIds[i]);
				if (employee == null || !employee.IsAlive)
				{
					continue;
				}

				employee.Experience += amount;
				_bus.Publish(new EmployeeExperienceGained(employee.Id, amount, employee.Experience));
				CheckLevelUp(employee);
			}
		}

		public int GetExperienceForNextLevel(Employee employee)
		{
			return _config.ExperiencePerLevelBase + (_config.ExperiencePerLevelStep * (employee.Level - 1));
		}

		/// <summary>Игрок распределяет 3 очка навыков за уровень (ДД, раздел 5).</summary>
		public bool TrySpendSkillPoint(Employee employee, StatKind stat, out string error)
		{
			if (!employee.IsAlive)
			{
				error = "Сотрудник погиб.";
				return false;
			}

			if (employee.UnspentSkillPoints <= 0)
			{
				error = "Нет нераспределённых очков навыков.";
				return false;
			}

			if (employee.BaseStats[stat] >= _config.MaxStatValue)
			{
				error = "Характеристика уже на максимуме.";
				return false;
			}

			employee.BaseStats = employee.BaseStats.Add(stat, 1);
			employee.UnspentSkillPoints--;
			_bus.Publish(new EmployeeStatsChanged(employee.Id, employee.BaseStats, employee.UnspentSkillPoints));
			error = string.Empty;
			return true;
		}

		public IReadOnlyList<HireCandidate> GetAvailableCandidates(int day)
		{
			var result = new List<HireCandidate>();
			for (int i = 0; i < _content.HirePool.Count; i++)
			{
				HireCandidate candidate = _content.HirePool[i];
				if (candidate.AvailableFromDay > day)
				{
					continue;
				}

				if (_state.HiredCandidateIds.Contains(candidate.Template.Id))
				{
					continue;
				}

				result.Add(candidate);
			}

			return result;
		}

		public bool TryHire(string candidateId, int day, out string error)
		{
			if (CountLiving() >= GetStaffLimit(day))
			{
				error = "Лимит штата на этот день исчерпан.";
				return false;
			}

			IReadOnlyList<HireCandidate> candidates = GetAvailableCandidates(day);
			for (int i = 0; i < candidates.Count; i++)
			{
				if (!string.Equals(candidates[i].Template.Id, candidateId, System.StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				Employee hired = candidates[i].Template.Clone();
				hired.Status = EmployeeStatus.Available;
				_state.Roster.Add(hired);
				_state.HiredCandidateIds.Add(hired.Id);
				_bus.Publish(new EmployeeHired(hired.Id, hired.Name, day));
				error = string.Empty;
				return true;
			}

			error = "Кандидат недоступен.";
			return false;
		}

		private void CheckLevelUp(Employee employee)
		{
			while (employee.Level < _config.MaxLevel)
			{
				int required = GetExperienceForNextLevel(employee);
				if (employee.Experience < required)
				{
					return;
				}

				employee.Experience -= required;
				employee.Level++;
				employee.UnspentSkillPoints += _config.SkillPointsPerLevel;
				_bus.Publish(new EmployeeLeveledUp(employee.Id, employee.Level, employee.UnspentSkillPoints));
			}
		}
	}
}
