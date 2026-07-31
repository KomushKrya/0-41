using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Авторский дизайн одного вызова. Приходит из JSON (текстовый пайплайн, ДД раздел 14).
	/// Ядро не генерирует миссии процедурно — только выбирает из пула на день.
	/// </summary>
	public sealed class MissionDefinition
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>День демо (1..4), для которого миссия доступна.</summary>
		public int Day { get; set; } = 1;

		public string CreatureId { get; set; } = string.Empty;

		public string Title { get; set; } = string.Empty;

		/// <summary>Кто звонит — для экрана телефона.</summary>
		public string CallerName { get; set; } = string.Empty;

		/// <summary>Краткое описание задания на экране после ответа на звонок.</summary>
		public string BriefingText { get; set; } = string.Empty;

		/// <summary>Требуемые показатели, сравниваются с суммой характеристик группы (ДД, раздел 7).</summary>
		public StatBlock Requirements { get; set; } = StatBlock.Zero;

		/// <summary>Время движения группы к месту (пунктирная линия на карте).</summary>
		public double TravelSeconds { get; set; } = 12.0;

		/// <summary>Время работы на объекте до резолва (после прибытия/после ответа по радио).</summary>
		public double OnSiteSeconds { get; set; } = 6.0;

		/// <summary>Время возвращения в главное управление.</summary>
		public double ReturnSeconds { get; set; } = 10.0;

		/// <summary>
		/// Радио-энкаунтер. Если null — вмешательство игрока этой миссией не предусмотрено
		/// (ДД, раздел 8: определяется дизайном задания, а не случайным шансом).
		/// </summary>
		public string? RadioEncounterId { get; set; }

		public ScaleDelta ScalesOnSuccess { get; set; } = ScaleDelta.Zero;

		public ScaleDelta ScalesOnFailure { get; set; } = ScaleDelta.Zero;

		/// <summary>Пропущенный звонок — шкалы сразу меняются в худшую сторону (ДД, раздел 4).</summary>
		public ScaleDelta ScalesOnMissedCall { get; set; } = ScaleDelta.Zero;

		/// <summary>Метка истекла — автоматический провал (ДД, раздел 4).</summary>
		public ScaleDelta ScalesOnExpiredMarker { get; set; } = ScaleDelta.Zero;

		public int ExperienceOnSuccess { get; set; } = 100;

		public int ExperienceOnFailure { get; set; } = 25;

		/// <summary>Базовые риски на сотрудника при провале; при успехе умножаются на понижающий коэффициент из конфига.</summary>
		public double InjuryChance { get; set; } = 0.25;

		public double DeathChance { get; set; } = 0.08;

		public string ReportSuccessText { get; set; } = string.Empty;

		public string ReportFailureText { get; set; } = string.Empty;

		/// <summary>
		/// Свойства существа, которые проявляются именно на этой миссии.
		/// Будут замечены (и раскроют абзац энциклопедии), если группа выжила.
		/// </summary>
		public List<string> ManifestedPropertyIds { get; } = new List<string>();
	}
}
