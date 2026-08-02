using System.Collections.Generic;

namespace Kontur.Core.Config
{
	/// <summary>
	/// Все балансные числа собраны здесь и грузятся из content/config.json.
	/// Значения по умолчанию соответствуют ДД и согласованным параметрам демо.
	/// В коде числовых литералов баланса быть не должно.
	/// </summary>
	public sealed class SimulationConfig
	{
		public TimingConfig Timings { get; set; } = new TimingConfig();

		public ScalesConfig Scales { get; set; } = new ScalesConfig();

		public ResolutionConfig Resolution { get; set; } = new ResolutionConfig();

		public EmployeeConfig Employees { get; set; } = new EmployeeConfig();

		public LootConfig Loot { get; set; } = new LootConfig();

		public MissionEventConfig MissionEvents { get; set; } = new MissionEventConfig();

		public StatMatchConfig Match { get; set; } = new StatMatchConfig();

		public List<DayConfig> Days { get; set; } = new List<DayConfig>();

		public DayConfig GetDay(int day)
		{
			for (int i = 0; i < Days.Count; i++)
			{
				if (Days[i].Day == day)
				{
					return Days[i];
				}
			}

			return new DayConfig { Day = day };
		}

		public static SimulationConfig CreateDefault()
		{
			var config = new SimulationConfig();
			config.Days.Add(new DayConfig { Day = 1, StaffLimit = 3, MinCalls = 5, MaxCalls = 6, RequirementMultiplier = 1.0, ConsumablesPerShift = 4, StandardPerShift = 1 });
			config.Days.Add(new DayConfig { Day = 2, StaffLimit = 4, MinCalls = 6, MaxCalls = 7, RequirementMultiplier = 1.15, ConsumablesPerShift = 4, StandardPerShift = 1 });
			config.Days.Add(new DayConfig { Day = 3, StaffLimit = 5, MinCalls = 7, MaxCalls = 9, RequirementMultiplier = 1.3, ConsumablesPerShift = 5, StandardPerShift = 2 });
			config.Days.Add(new DayConfig { Day = 4, StaffLimit = 6, MinCalls = 8, MaxCalls = 10, RequirementMultiplier = 1.5, ConsumablesPerShift = 5, StandardPerShift = 2 });
			return config;
		}
	}

	public sealed class TimingConfig
	{
		/// <summary>ДД, раздел 4: 15 секунд на ответ.</summary>
		public double PhoneRingSeconds { get; set; } = 15.0;

		/// <summary>ДД, раздел 4: метка держится 30 секунд.</summary>
		public double MapMarkerSeconds { get; set; } = 30.0;

		/// <summary>ДД, раздел 4: 20 секунд на реакцию по радио.</summary>
		public double RadioSeconds { get; set; } = 20.0;

		/// <summary>ДД, раздел 3, п. 14: 5 минут реального времени на приём новых вызовов.</summary>
		public double ShiftCallWindowSeconds { get; set; } = 300.0;

		/// <summary>Минимальный зазор между запланированными звонками.</summary>
		public double MinSecondsBetweenCalls { get; set; } = 12.0;

		/// <summary>Пауза перед появлением отчёта после возвращения группы.</summary>
		public double ReportDelaySeconds { get; set; } = 1.0;

		/// <summary>
		/// Пауза после того, как линия освободилась, прежде чем зазвонит следующий вызов
		/// из очереди. Без неё звонки шли бы встык и слипались в один поток.
		/// </summary>
		public double CallQueueGapSeconds { get; set; } = 2.0;
	}

	public sealed class ScalesConfig
	{
		public double Min { get; set; } = 0.0;

		public double Max { get; set; } = 100.0;

		public double StartInfection { get; set; } = 20.0;

		public double StartPublicity { get; set; } = 15.0;

		public double StartLoyalty { get; set; } = 70.0;

		/// <summary>Заражение достигает максимума — game over.</summary>
		public double InfectionLoseAt { get; set; } = 100.0;

		/// <summary>Гласность достигает максимума — game over.</summary>
		public double PublicityLoseAt { get; set; } = 100.0;

		/// <summary>Лояльность достигает минимума — game over.</summary>
		public double LoyaltyLoseAt { get; set; } = 0.0;
	}

	/// <summary>
	/// Как порог по характеристике превращается в проценты. Три ступени вместо плавной
	/// кривой — потому что игрок читает их цветом и должен понимать, что именно исправить.
	/// </summary>
	public sealed class StatMatchConfig
	{
		/// <summary>Превышение на столько и больше — зелёный, полный вклад.</summary>
		public int ExceedsMargin { get; set; } = 2;

		/// <summary>Вклад жёлтого: порог закрыт, но без запаса.</summary>
		public double MeetsScore { get; set; } = 0.8;

		/// <summary>
		/// Во сколько раз падает вклад за каждое очко недобора. 0.35 означает: не хватает
		/// одного — вклад падает втрое, двух — вдесятеро. Красный обязан быть больно.
		/// </summary>
		public double BelowFalloff { get; set; } = 0.35;

		/// <summary>Вес главной характеристики вызова относительно остальных.</summary>
		public double PrimaryWeight { get; set; } = 2.0;

		public double SecondaryWeight { get; set; } = 1.0;
	}

	public sealed class ResolutionConfig
	{
		/// <summary>Шанс = покрытие^Exponent. 2.0 — согласованная кривая.</summary>
		public double CoverageExponent { get; set; } = 2.0;

		public double MaxDiceChance { get; set; } = 0.95;

		public double MinDiceChance { get; set; } = 0.02;

		/// <summary>
		/// ДД, раздел 4: пропущенное радио — не автопровал, а бросок с повышенным шансом провала.
		/// Итоговый шанс успеха умножается на это число.
		/// </summary>
		public double RadioMissedChanceMultiplier { get; set; } = 0.5;

		/// <summary>При успехе риски всё равно есть, но сильно ниже.</summary>
		public double SuccessInjuryMultiplier { get; set; } = 0.25;

		public double SuccessDeathMultiplier { get; set; } = 0.1;

		/// <summary>Чем хуже покрытие, тем выше риски: множитель = 1 + (1 - coverage) * этот коэффициент.</summary>
		public double RiskCoverageInfluence { get; set; } = 1.5;
	}

	public sealed class EmployeeConfig
	{
		/// <summary>Травма — дебафф до конца смены: минус столько к каждой характеристике.</summary>
		public int InjuryPenaltyPerStat { get; set; } = 1;

		/// <summary>ДД, раздел 5: 3 очка навыков на уровень.</summary>
		public int SkillPointsPerLevel { get; set; } = 3;

		public int MaxLevel { get; set; } = 10;

		/// <summary>Опыт до уровня N = Base + Step * (N - 1).</summary>
		public int ExperiencePerLevelBase { get; set; } = 150;

		public int ExperiencePerLevelStep { get; set; } = 100;

		public int MaxStatValue { get; set; } = 20;
	}

	public sealed class LootConfig
	{
		/// <summary>Небольшой шанс найти расходник по итогам удачной миссии (ДД, раздел 6).</summary>
		public double ConsumableFindChance { get; set; } = 0.2;

		/// <summary>Слоты снаряжения — на группу (ДД, раздел 6).</summary>
		public int StandardOrStorySlots { get; set; } = 1;

		public int ConsumableSlots { get; set; } = 2;
	}

	/// <summary>
	/// Умолчания по типу диалога. Автор пишет `quality: good` и может не писать числа —
	/// они возьмутся отсюда. Явно написанное в тексте всегда сильнее умолчания.
	/// </summary>
	public sealed class MissionEventConfig
	{
		public MissionEventQualityConfig Good { get; set; } =
			new MissionEventQualityConfig { RequirementModifier = 0, RiskMultiplier = 1.0 };

		public MissionEventQualityConfig Neutral { get; set; } =
			new MissionEventQualityConfig { RequirementModifier = 1, RiskMultiplier = 1.0 };

		public MissionEventQualityConfig Bad { get; set; } =
			new MissionEventQualityConfig { RequirementModifier = 2, RiskMultiplier = 1.5 };

		public MissionEventQualityConfig For(Kontur.Core.Model.MissionEventQuality quality)
		{
			switch (quality)
			{
				case Kontur.Core.Model.MissionEventQuality.Good: return Good;
				case Kontur.Core.Model.MissionEventQuality.Bad: return Bad;
				default: return Neutral;
			}
		}
	}

	public sealed class MissionEventQualityConfig
	{
		public int RequirementModifier { get; set; }

		/// <summary>Множитель травм и гибели, если у варианта не заданы свои.</summary>
		public double RiskMultiplier { get; set; } = 1.0;
	}

	public sealed class DayConfig
	{
		public int Day { get; set; } = 1;

		/// <summary>Лимит штата: 3 / 4 / 5 / 6 (ДД, раздел 5).</summary>
		public int StaffLimit { get; set; } = 3;

		/// <summary>ДД, раздел 3, п. 13: от 5 до 10 вызовов за смену.</summary>
		public int MinCalls { get; set; } = 5;

		public int MaxCalls { get; set; } = 6;

		/// <summary>Рост требуемых показателей по дням (ДД, раздел 12).</summary>
		public double RequirementMultiplier { get; set; } = 1.0;

		public int ConsumablesPerShift { get; set; } = 4;

		public int StandardPerShift { get; set; } = 1;

		/// <summary>Записка сменщика и ролик — просто идентификаторы контента.</summary>
		public string ShiftNoteId { get; set; } = string.Empty;

		public string OutroCutsceneId { get; set; } = string.Empty;

		// --- обучающая смена (ДД, раздел 12: сложность растёт только через контент и параметры) ---

		/// <summary>
		/// Отключить таймеры звонка, метки и радио: ядро ждёт действия игрока сколько угодно.
		/// Таймеры дороги, работы на объекте и возвращения продолжают идти — иначе вызов не завершится.
		/// </summary>
		public bool DisableTimers { get; set; }

		/// <summary>
		/// Сколько первых вызовов приходят строго по одному: следующий не поступит,
		/// пока не закрыт предыдущий. 0 — обычное расписание с наложением.
		/// </summary>
		public int SequentialCallCount { get; set; }

		/// <summary>
		/// Жёсткий сценарий смены: список Id миссий в нужном порядке.
		/// Если не пуст — заменяет случайный подбор, а MinCalls/MaxCalls игнорируются.
		/// </summary>
		public List<string> MissionOrder { get; set; } = new List<string>();

		public bool IsScripted
		{
			get { return MissionOrder.Count > 0; }
		}
	}
}
