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
		private readonly EmployeeFactory _factory;

		public RosterSystem(GameState state, ContentDatabase content, EmployeeConfig config, IEventBus bus, EmployeeFactory factory)
		{
			_state = state;
			_content = content;
			_config = config;
			_bus = bus;
			_factory = factory;
		}

		public int GetStaffLimit(int day)
		{
			return _content.Config.GetStaffLimit(day);
		}

		public int CountLiving()
		{
			return _state.CountLivingEmployees();
		}

		public int CountFreeSlots(int day)
		{
			return System.Math.Max(0, GetStaffLimit(day) - CountLiving());
		}

		public int CountWantedCandidates(int day)
		{
			EmployeeGeneratorSettings settings = _content.Generator;
			return System.Math.Max(settings.CandidatesPerShift, CountFreeSlots(day) + settings.CandidatesChoiceMargin);
		}

		public IReadOnlyList<HireCandidate> GetStartingChoice()
		{
			if (_state.StartingRosterConfirmed || _content.Generator.StartingChoicePoolSize <= 0)
			{
				return new List<HireCandidate>();
			}

			if (_state.StartingChoice.Count == 0)
			{
				_state.StartingChoice.AddRange(_factory.Generate(
					1,
					_content.Generator.StartingChoicePoolSize,
					CollectNames(),
					CollectIds(),
					CollectPortraits()));
			}

			return _state.StartingChoice;
		}

		public bool TryConfirmStartingRoster(IReadOnlyList<string> candidateIds, out string error)
		{
			if (_state.StartingRosterConfirmed)
			{
				error = "Starting roster is already confirmed.";
				return false;
			}

			IReadOnlyList<HireCandidate> pool = GetStartingChoice();
			if (pool.Count == 0 || candidateIds == null || candidateIds.Count == 0 || candidateIds.Count > GetStaffLimit(1))
			{
				error = "Invalid starting roster selection.";
				return false;
			}

			var selected = new List<Employee>();
			var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < candidateIds.Count; i++)
			{
				if (!seen.Add(candidateIds[i]))
				{
					error = "An employee was selected twice.";
					return false;
				}

				HireCandidate? candidate = null;
				for (int j = 0; j < pool.Count; j++)
				{
					if (string.Equals(pool[j].Template.Id, candidateIds[i], System.StringComparison.OrdinalIgnoreCase))
					{
						candidate = pool[j];
						break;
					}
				}

				if (candidate == null)
				{
					error = "Selected employee is not in the starting pool.";
					return false;
				}
				selected.Add(candidate.Template.Clone());
			}

			_state.Roster.Clear();
			for (int i = 0; i < selected.Count; i++)
			{
				Employee employee = selected[i];
				employee.Status = EmployeeStatus.Available;
				_state.Roster.Add(employee);
				_state.HiredCandidateIds.Add(employee.Id);
				_bus.Publish(new EmployeeHired(employee.Id, employee.Name, 1));
			}
			_state.StartingChoice.Clear();
			_state.StartingRosterConfirmed = true;
			error = string.Empty;
			return true;
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
			if (_state.HireOffersDay != day) RefreshHireOffers(day);
			var result = new List<HireCandidate>();
			for (int i = 0; i < _state.HireOffers.Count; i++)
			{
				HireCandidate candidate = _state.HireOffers[i];
				if (_state.HiredCandidateIds.Contains(candidate.Template.Id))
				{
					continue;
				}

				result.Add(candidate);
			}

			return result;
		}

		public void RefreshHireOffers(int day)
		{
			_state.HireOffers.Clear();
			_state.HireOffersDay = day;
			for (int i = 0; i < _content.HirePool.Count; i++)
			{
				HireCandidate candidate = _content.HirePool[i];
				if (candidate.AvailableFromDay <= day && !_state.HiredCandidateIds.Contains(candidate.Template.Id)) _state.HireOffers.Add(candidate);
			}
			int missing = CountWantedCandidates(day) - _state.HireOffers.Count;
			if (missing <= 0) return;
			var names = new List<string>(); var ids = new List<string>(); var portraits = new List<string>();
			foreach (Employee employee in _state.Roster) { names.Add(employee.Name); ids.Add(employee.Id); portraits.Add(employee.PortraitId); }
			foreach (HireCandidate candidate in _state.HireOffers) { names.Add(candidate.Template.Name); ids.Add(candidate.Template.Id); portraits.Add(candidate.Template.PortraitId); }
			_state.HireOffers.AddRange(_factory.Generate(day, missing, names, ids, portraits));
		}

		private List<string> CollectNames()
		{
			var result = new List<string>();
			for (int i = 0; i < _state.Roster.Count; i++) result.Add(_state.Roster[i].Name);
			return result;
		}

		private List<string> CollectIds()
		{
			var result = new List<string>(_state.HiredCandidateIds);
			for (int i = 0; i < _state.Roster.Count; i++) result.Add(_state.Roster[i].Id);
			return result;
		}

		private List<string> CollectPortraits()
		{
			var result = new List<string>();
			for (int i = 0; i < _state.Roster.Count; i++)
			{
				if (_state.Roster[i].IsAlive && _state.Roster[i].PortraitId.Length > 0) result.Add(_state.Roster[i].PortraitId);
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
