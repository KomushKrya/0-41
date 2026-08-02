using System;
using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Всё изменяемое состояние партии. Единственный источник истины для рантайма.
	/// Контент (ContentDatabase) остаётся неизменным — здесь только то, что меняется по ходу игры.
	/// </summary>
	public sealed class GameState
	{
		public int Day { get; set; }

		public ScaleValues Scales { get; set; }

		public bool IsGameOver { get; set; }

		public GameOverReason? GameOverReason { get; set; }

		public Dictionary<string, Zone> Zones { get; } = new Dictionary<string, Zone>(StringComparer.OrdinalIgnoreCase);

		public List<Employee> Roster { get; } = new List<Employee>();

		public EncyclopediaState Encyclopedia { get; } = new EncyclopediaState();

		/// <summary>Сюжетные флаги — условные абзацы текстов, requirements, последствия выборов.</summary>
		public FlagSet Flags { get; } = new FlagSet();

		public EquipmentInventory Inventory { get; } = new EquipmentInventory();

		/// <summary>Кандидаты, уже нанятые в предыдущие дни — повторно не предлагаются.</summary>
		public HashSet<string> HiredCandidateIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Кандидаты, показываемые в меню найма прямо сейчас.
		///
		/// Хранятся, а не пересобираются на каждый запрос: интерфейс дёргает список при каждой
		/// перерисовке, и генерация на лету меняла бы кандидатов под курсором игрока.
		/// </summary>
		public List<Content.HireCandidate> HireOffers { get; } = new List<Content.HireCandidate>();

		/// <summary>День, на который собран HireOffers. -1 — не собирался ни разу.</summary>
		public int HireOffersDay { get; set; } = -1;

		/// <summary>
		/// Кандидаты, из которых игрок набирает стартовый состав перед первой сменой.
		///
		/// Хранятся, а не пересобираются: экран выбора перерисовывается на каждое
		/// нажатие, и генерация на лету меняла бы людей под курсором. Список живёт
		/// до подтверждения выбора, после чего очищается.
		/// </summary>
		public List<Content.HireCandidate> StartingChoice { get; } = new List<Content.HireCandidate>();

		/// <summary>Стартовый состав уже собран — выбор больше не предлагается.</summary>
		public bool StartingRosterConfirmed { get; set; }

		/// <summary>Отчёты на компьютере — накапливаются за партию.</summary>
		public List<MissionReport> Reports { get; } = new List<MissionReport>();

		/// <summary>Уже использованные в этой партии миссии — чтобы не повторять один и тот же вызов.</summary>
		public HashSet<string> UsedMissionIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public Employee? FindEmployee(string employeeId)
		{
			for (int i = 0; i < Roster.Count; i++)
			{
				if (string.Equals(Roster[i].Id, employeeId, StringComparison.OrdinalIgnoreCase))
				{
					return Roster[i];
				}
			}

			return null;
		}

		public Zone? FindZone(string zoneId)
		{
			Zone? zone;
			return Zones.TryGetValue(zoneId, out zone) ? zone : null;
		}

		public int CountLivingEmployees()
		{
			int count = 0;
			for (int i = 0; i < Roster.Count; i++)
			{
				if (Roster[i].IsAlive)
				{
					count++;
				}
			}

			return count;
		}
	}
}
