using System.Collections.Generic;

namespace Kontur.Core.Persistence
{
	/// <summary>
	/// РЎРЅРёРјРѕРє РїР°СЂС‚РёРё РјРµР¶РґСѓ СЃРјРµРЅР°РјРё. Р’ РЅС‘Рј РЅРµС‚ СЂР°Р№РѕРЅРѕРІ Рё РЅРµС‚ С‚РµРєСЃС‚Р°: РєРѕРЅС‚РµРЅС‚ С‡РёС‚Р°РµС‚СЃСЏ
	/// Р·Р°РЅРѕРІРѕ, РїРѕСЌС‚РѕРјСѓ РїСЂР°РІРєРё Р±Р°Р»Р°РЅСЃР° Рё Р»РѕРєР°Р»РёР·Р°С†РёРё РґРѕС…РѕРґСЏС‚ РґРѕ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёС… СЃРѕС…СЂР°РЅРµРЅРёР№.
	/// </summary>
	public sealed class SaveData
	{
		public const int CurrentVersion = 2;
		public string SavedAtUtc { get; set; } = string.Empty;
		public string Label { get; set; } = string.Empty;
		public int Version { get; set; } = CurrentVersion;
		public int Seed { get; set; }
		public ulong RandomState { get; set; }
		public int Day { get; set; }
		public double Infection { get; set; }
		public double Publicity { get; set; }
		public double Loyalty { get; set; }
		public bool IsGameOver { get; set; }
		public string GameOverReason { get; set; } = string.Empty;
		public List<SavedEmployee> Roster { get; set; } = new List<SavedEmployee>();
		public List<SavedStack> Inventory { get; set; } = new List<SavedStack>();
		public List<string> Flags { get; set; } = new List<string>();
		public List<SavedKnowledge> Encyclopedia { get; set; } = new List<SavedKnowledge>();
		public List<string> HiredCandidateIds { get; set; } = new List<string>();
		public List<string> UsedMissionIds { get; set; } = new List<string>();
		public List<SavedReport> Reports { get; set; } = new List<SavedReport>();
		/// <summary>Null РѕР·РЅР°С‡Р°РµС‚ СЃРѕС…СЂР°РЅРµРЅРёРµ РјРµР¶РґСѓ СЃРјРµРЅР°РјРё.</summary>
		public SavedShift? Shift { get; set; }
	}

	public sealed class SavedEmployee
	{
		public string Id { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public string RankTitle { get; set; } = string.Empty;
		public string PortraitId { get; set; } = string.Empty;
		public int Level { get; set; }
		public int Strength { get; set; }
		public int Intellect { get; set; }
		public int Combat { get; set; }
		public int Charisma { get; set; }
		public int Agility { get; set; }
		public int Experience { get; set; }
		public int UnspentSkillPoints { get; set; }
		public string Status { get; set; } = "Available";
		public bool IsInjured { get; set; }
		public string CurrentIncidentId { get; set; } = string.Empty;
		public List<string> AbilityIds { get; set; } = new List<string>();
	}

	public sealed class SavedStack
	{
		public string Id { get; set; } = string.Empty;
		public int Quantity { get; set; }
		public bool IsShiftOnly { get; set; }
	}

	public sealed class SavedKnowledge
	{
		public string CreatureId { get; set; } = string.Empty;
		public List<string> PropertyIds { get; set; } = new List<string>();
	}

	public sealed class SavedReport
	{
		public string IncidentId { get; set; } = string.Empty;
		public string MissionId { get; set; } = string.Empty;
		public string ReportId { get; set; } = string.Empty;
		public string CreatureId { get; set; } = string.Empty;
		public string ChosenOptionId { get; set; } = string.Empty;
		public bool IsSuccess { get; set; }
		public List<string> RevealedPropertyIds { get; set; } = new List<string>();
	}

	/// <summary>РџРѕР»РЅС‹Р№ СЃРЅРёРјРѕРє СЂР°Р±РѕС‚Р°СЋС‰РµР№ СЃРјРµРЅС‹ Р±РµР· РґР°РЅРЅС‹С… СЂР°Р№РѕРЅРѕРІ.</summary>
	public sealed class SavedShift
	{
		public bool IsActive { get; set; }
		public double ShiftTime { get; set; }
		public double CallQueueCooldown { get; set; }
		public bool CallWindowClosed { get; set; }
		public int SpawnedCount { get; set; }
		public int TotalIncidents { get; set; }
		public int Successes { get; set; }
		public int Failures { get; set; }
		public int MissedCalls { get; set; }
		public int ExpiredMarkers { get; set; }
		public int Injuries { get; set; }
		public int Deaths { get; set; }
		public List<SavedIncident> Pending { get; set; } = new List<SavedIncident>();
		public List<SavedIncident> Incidents { get; set; } = new List<SavedIncident>();
	}

	public sealed class SavedIncident
	{
		public string Id { get; set; } = string.Empty;
		public string MissionId { get; set; } = string.Empty;
		public string BuildingId { get; set; } = string.Empty;
		public string Phase { get; set; } = "Scheduled";
		public double ScheduledAtSeconds { get; set; }
		public double OutboundTravelSeconds { get; set; }
		public bool HasTimer { get; set; }
		public double TimerDuration { get; set; }
		public double TimerRemaining { get; set; }
		public bool TimerRunning { get; set; }
		public List<string> SquadEmployeeIds { get; set; } = new List<string>();
		public List<string> EquipmentIds { get; set; } = new List<string>();
		public string MissionEventId { get; set; } = string.Empty;
		public string ChosenOptionId { get; set; } = string.Empty;
		public bool RadioWasTriggered { get; set; }
		public bool RadioWasMissed { get; set; }
		public bool HasOutcome { get; set; }
		public bool OutcomeWasSuccess { get; set; }
		public SavedReport? Report { get; set; }
	}
}
