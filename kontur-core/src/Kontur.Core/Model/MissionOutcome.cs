using System.Collections.Generic;

namespace Kontur.Core.Model
{
	public enum MissionResultKind
	{
		Success = 0,
		Failure = 1
	}

	/// <summary>Причина итога — уходит в сигнал MissionResolved (ДД, раздел 13).</summary>
	public enum MissionResolutionReason
	{
		/// <summary>Сумма характеристик покрыла требования — успех без броска.</summary>
		StatsCovered = 0,

		/// <summary>Бросок кубика удался.</summary>
		DiceSuccess = 1,

		/// <summary>Бросок кубика не удался.</summary>
		DiceFailure = 2,

		/// <summary>Не успел взять трубку за 15 секунд.</summary>
		CallMissed = 3,

		/// <summary>Не успел отправить группу за 30 секунд.</summary>
		MarkerExpired = 4
	}

	public sealed class MissionOutcome
	{
		public string IncidentId { get; set; } = string.Empty;

		public string MissionId { get; set; } = string.Empty;

		public string BuildingId { get; set; } = string.Empty;

		public string CreatureId { get; set; } = string.Empty;

		public MissionResultKind Kind { get; set; }

		public MissionResolutionReason Reason { get; set; }

		/// <summary>Требования с учётом выбранного радио-варианта.</summary>
		public StatBlock EffectiveRequirements { get; set; } = StatBlock.Zero;

		/// <summary>Итоговая сумма характеристик группы со снаряжением и способностями.</summary>
		public StatBlock SquadStats { get; set; } = StatBlock.Zero;

		/// <summary>0..1 — насколько группа покрыла требования.</summary>
		public double Coverage { get; set; }

		public double SuccessChance { get; set; }

		public double Roll { get; set; } = -1.0;

		public List<string> EmployeeIds { get; } = new List<string>();

		public List<string> InjuredEmployeeIds { get; } = new List<string>();

		public List<string> KilledEmployeeIds { get; } = new List<string>();

		public List<string> EquipmentIds { get; } = new List<string>();

		public ScaleDelta ScaleDelta { get; set; } = ScaleDelta.Zero;

		public bool RadioWasTriggered { get; set; }

		public bool RadioWasMissed { get; set; }

		public string? ChosenRadioOptionId { get; set; }

		/// <summary>Вся отправленная группа погибла — сюжетное снаряжение теряется (ДД, раздел 6).</summary>
		public bool SquadWiped { get; set; }

		public bool IsSuccess
		{
			get { return Kind == MissionResultKind.Success; }
		}
	}

	/// <summary>Отчёт, который появляется на компьютере после возвращения группы (ДД, раздел 3, п. 12).</summary>
	public sealed class MissionReport
	{
		public string IncidentId { get; set; } = string.Empty;

		public string MissionId { get; set; } = string.Empty;

		public string Title { get; set; } = string.Empty;

		public string Text { get; set; } = string.Empty;

		/// <summary>Существо, с которым столкнулась группа. Пусто, если никто не вернулся.</summary>
		public string CreatureId { get; set; } = string.Empty;

		public bool IsSuccess { get; set; }

		/// <summary>Id свойств энциклопедии, открытых этой миссией.</summary>
		public List<string> RevealedPropertyIds { get; } = new List<string>();
	}
}
