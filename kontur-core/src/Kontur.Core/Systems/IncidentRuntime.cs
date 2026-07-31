using System.Collections.Generic;
using Kontur.Core.Model;
using Kontur.Core.Simulation;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Один вызов в работе. Вызовы могут накладываться (ДД, раздел 3, п. 13),
	/// поэтому фаза и таймер живут на инциденте, а не глобально.
	/// </summary>
	public sealed class IncidentRuntime
	{
		public IncidentRuntime(string id, MissionDefinition mission, string buildingId)
		{
			Id = id;
			Mission = mission;
			BuildingId = buildingId;
		}

		public string Id { get; }

		public MissionDefinition Mission { get; }

		public string BuildingId { get; }

		public IncidentPhase Phase { get; set; } = IncidentPhase.Scheduled;

		/// <summary>Таймер текущей фазы. null — фаза ждёт действия игрока без ограничения времени.</summary>
		public Countdown? Timer { get; set; }

		public List<string> SquadEmployeeIds { get; } = new List<string>();

		public List<string> EquipmentIds { get; } = new List<string>();

		public RadioEncounter? Radio { get; set; }

		public RadioOption? ChosenOption { get; set; }

		public bool RadioWasTriggered { get; set; }

		public bool RadioWasMissed { get; set; }

		public MissionOutcome? Outcome { get; set; }

		public MissionReport? Report { get; set; }

		/// <summary>Момент смены (в секундах), когда должен зазвонить телефон.</summary>
		public double ScheduledAtSeconds { get; set; }

		public bool IsClosed
		{
			get { return Phase == IncidentPhase.Closed; }
		}

		public bool IsActive
		{
			get { return Phase != IncidentPhase.Scheduled && Phase != IncidentPhase.Closed; }
		}

		public double RemainingSeconds
		{
			get { return Timer == null ? 0.0 : Timer.Remaining; }
		}

		public void SetPhase(IncidentPhase phase, double? timerSeconds)
		{
			Phase = phase;
			Timer = timerSeconds.HasValue ? Countdown.Start(timerSeconds.Value) : null;
		}
	}
}
