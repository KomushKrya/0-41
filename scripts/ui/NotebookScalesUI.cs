using System;
using Godot;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Блокнот на столе: три шкалы состояния (ДД, раздел 11).
///
/// Собственного состояния у виджета нет. Истина живёт в ядре: стартовые значения
/// берутся снимком <c>GetStatus()</c>, дальше приходят сигналом <see cref="ScalesChanged"/>.
/// Виджет только анимирует переход к последнему известному значению.
///
/// Ручные +/- остались для отладочных сцен и по умолчанию скрыты
/// (<see cref="ShowDebugControls"/>). Команды «поставить шкалу» у ядра нет,
/// поэтому кнопки двигают только картинку — при включённой отладке блокнот
/// разъедется с ядром до ближайшего ScalesChanged.
/// </summary>
public partial class NotebookScalesUI : Control
{
	/// <summary>Показать ручные +/- и поле ввода дельты. Только для отладочных сцен.</summary>
	[Export] public bool ShowDebugControls { get; set; }

	/// <summary>Что рисовать, пока ядро не поднялось: заражение / гласность / лояльность.</summary>
	[Export] public Vector3 FallbackScales { get; set; } = new(20.0f, 15.0f, 70.0f);

	/// <summary>
	/// Поднимать блокнот к лицу, когда шкалы просели по вине игрока.
	/// Выключается флажком в инспекторе, если приём окажется навязчивым.
	/// </summary>
	[Export] public bool RaiseOnPenalty { get; set; } = true;

	/// <summary>Сколько секунд держать подпись изменения перед тем, как гасить её.</summary>
	[Export] public double DeltaHoldSeconds { get; set; } = 20.0;

	/// <summary>Сколько секунд гаснет подпись изменения после выдержки.</summary>
	[Export] public double DeltaFadeSeconds { get; set; } = 3.0;

	[Export] public double HoldChangePerSecond { get; set; } = 25.0;
	[Export] public double BaseAnimationSpeed { get; set; } = 12.0;
	[Export] public double DeltaAnimationSpeedMultiplier { get; set; } = 1.15;
	[Export] public int DisplayDecimals { get; set; } = 2;

	private const double MinValue = 0.0;
	private const double MaxValue = 100.0;
	private const double ValueStep = 0.01;
	private const double Epsilon = 0.001;

	/// <summary>Палитра подписей. Взята из самого блокнота, чтобы вставка не выбивалась.</summary>
	private static readonly Color BadColor = new(1.0f, 0.42f, 0.32f);
	private static readonly Color GoodColor = new(0.55f, 1.0f, 0.6f);
	private static readonly Color CalmColor = new(0.52f, 0.78f, 0.48f);

	private Meter _infection = null!;
	private Meter _publicity = null!;
	private Meter _loyalty = null!;
	private Meter _heldMeter = null!;
	private double _heldDirection;

	private Label _summaryLine = null!;
	private double _summaryHold;

	private IDisposable _scalesSubscription;
	private IDisposable _shiftStartedSubscription;

	public override void _Ready()
	{
		// Куда шкале расти — вопрос не косметический: от него зависит, каким цветом
		// подписать изменение. Рост заражения и рост лояльности — события с
		// противоположным знаком для игрока, хотя арифметически оба «плюс».
		_infection = CreateMeter("Parameters/InfectionRow/MarginContainer/RowContent", FallbackScales.X, false);
		_publicity = CreateMeter("Parameters/PublicityRow/MarginContainer/RowContent", FallbackScales.Y, false);
		_loyalty = CreateMeter("Parameters/LoyaltyRow/MarginContainer/RowContent", FallbackScales.Z, true);

		_summaryLine = GetNode<Label>("SummaryLine");
		ShowSummary(null);

		SetupMeter(_infection);
		SetupMeter(_publicity);
		SetupMeter(_loyalty);

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			GD.PushWarning("NotebookScalesUI: GameRuntime is not ready; showing fallback scales.");
			return;
		}

		SyncFromCore();

		_scalesSubscription = runtime.Session.Events.Subscribe<ScalesChanged>(OnScalesChanged);

		// Начало смены — единственный момент, когда значения могут смениться
		// без сигнала (новая партия, загрузка сохранения).
		_shiftStartedSubscription = runtime.Session.Events.Subscribe<ShiftStarted>(_ => SyncFromCore());
	}

	public override void _ExitTree()
	{
		_scalesSubscription?.Dispose();
		_shiftStartedSubscription?.Dispose();
	}

	public override void _Process(double delta)
	{
		if (_heldMeter != null && !Mathf.IsZeroApprox(_heldDirection))
		{
			ApplyDelta(_heldMeter, _heldDirection * HoldChangePerSecond * delta);
		}

		UpdateMeterAnimation(_infection, delta);
		UpdateMeterAnimation(_publicity, delta);
		UpdateMeterAnimation(_loyalty, delta);

		FadeNotices(delta);
	}

	/// <summary>Перечитать шкалы снимком, без анимации: блокнот показывает то, что уже есть.</summary>
	public void SyncFromCore()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return;
		}

		ApplyScales(runtime.Session.GetStatus().Scales, false);
	}

	private void ApplyScales(ScaleValues scales, bool animate)
	{
		SetTarget(_infection, scales.Infection, animate);
		SetTarget(_publicity, scales.Publicity, animate);
		SetTarget(_loyalty, scales.Loyalty, animate);
	}

	/// <summary>
	/// Пришло изменение от ядра. Кроме самих шкал показываем, на сколько именно
	/// они сдвинулись и почему.
	///
	/// Числа берём разностью «было — стало», а не из <c>e.Delta</c>: в дельте лежит
	/// запрошенное изменение, а ядро упирает шкалу в границы. При заражении 98%
	/// штраф «+6» дал бы в блокноте +6, тогда как на деле прибавилось два.
	/// </summary>
	private void OnScalesChanged(ScalesChanged e)
	{
		double wasInfection = _infection.TargetValue;
		double wasPublicity = _publicity.TargetValue;
		double wasLoyalty = _loyalty.TargetValue;

		ApplyScales(e.Values, true);

		ShowDelta(_infection, _infection.TargetValue - wasInfection);
		ShowDelta(_publicity, _publicity.TargetValue - wasPublicity);
		ShowDelta(_loyalty, _loyalty.TargetValue - wasLoyalty);

		ShowSummary(e.Reason);

		// Штраф игрок должен увидеть, а не обнаружить. Удачи блокнот не
		// показывает: об успехе и так скажет отчёт по миссии.
		if (RaiseOnPenalty && IsPenalty(e.Reason))
		{
			NotebookAlert.RaiseNotebook(this);
		}
	}

	/// <summary>
	/// Подпись изменения справа от процента. Держится несколько секунд и гаснет:
	/// это отметка о событии, а не постоянная часть интерфейса — иначе через минуту
	/// игрок перестанет отличать свежий штраф от прошлогоднего.
	/// </summary>
	private void ShowDelta(Meter meter, double delta)
	{
		if (Mathf.Abs(delta) < ValueStep)
		{
			return;
		}

		bool good = delta > 0.0 == meter.HigherIsBetter;

		// Минус берём типографский: обычный дефис в терминальном шрифте
		// рядом с крупными цифрами теряется и читается как перенос.
		string sign = delta > 0.0 ? "+" : "−";
		meter.DeltaLabel.Text = sign + Mathf.Abs(delta).ToString($"F{DisplayDecimals}");

		Color color = good ? GoodColor : BadColor;
		meter.DeltaLabel.AddThemeColorOverride("font_color", color);
		meter.DeltaLabel.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);

		// Тем же цветом красим и бегущий сегмент на полосе, чтобы число и полоса
		// говорили об одном событии, а не жили порознь.
		meter.FlashFill.Modulate = color;

		meter.DeltaHold = DeltaHoldSeconds;
	}

	/// <summary>Строка под шкалами: чем вызван последний сдвиг. Пусто — значит всё ровно.</summary>
	private void ShowSummary(string reason)
	{
		if (_summaryLine == null)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(reason))
		{
			_summaryLine.Text = "обстановка в пределах нормы";
			_summaryLine.AddThemeColorOverride("font_color", CalmColor);
			_summaryHold = 0.0;
			return;
		}

		_summaryLine.Text = reason.ToUpperInvariant();
		_summaryLine.AddThemeColorOverride("font_color", IsPenalty(reason) ? BadColor : GoodColor);
		_summaryHold = DeltaHoldSeconds + DeltaFadeSeconds;
	}

	/// <summary>
	/// Провал это или удача, знает ядро, но в сигнале едет только человеческая
	/// причина. Разбирать её строкой — не самое чистое решение, зато оно не тянет
	/// в сигнал лишнее поле ради одной подписи. Не угадали — строка будет зелёной,
	/// а цифры рядом всё равно красными: цену игрок увидит.
	/// </summary>
	private static bool IsPenalty(string reason)
	{
		return reason.Contains("пропущ", StringComparison.OrdinalIgnoreCase)
			|| reason.Contains("провал", StringComparison.OrdinalIgnoreCase)
			|| reason.Contains("не ответил", StringComparison.OrdinalIgnoreCase)
			|| reason.Contains("не отправл", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Гасит подписи и строку сводки, когда выдержка вышла.</summary>
	private void FadeNotices(double delta)
	{
		FadeDelta(_infection, delta);
		FadeDelta(_publicity, delta);
		FadeDelta(_loyalty, delta);

		if (_summaryHold > 0.0)
		{
			_summaryHold -= delta;
			if (_summaryHold <= 0.0)
			{
				ShowSummary(null);
			}
		}
	}

	private void FadeDelta(Meter meter, double delta)
	{
		if (meter.DeltaHold <= -DeltaFadeSeconds)
		{
			return;
		}

		meter.DeltaHold -= delta;

		if (meter.DeltaHold >= 0.0)
		{
			return;
		}

		double faded = -meter.DeltaHold / Mathf.Max(DeltaFadeSeconds, 0.01);
		float alpha = (float)Mathf.Clamp(1.0 - faded, 0.0, 1.0);
		meter.DeltaLabel.Modulate = new Color(1.0f, 1.0f, 1.0f, alpha);

		if (alpha <= 0.0f)
		{
			meter.DeltaLabel.Text = string.Empty;
			meter.DeltaHold = -DeltaFadeSeconds;
		}
	}

	private Meter CreateMeter(string rowPath, double initialValue, bool higherIsBetter)
	{
		return new Meter
		{
			HigherIsBetter = higherIsBetter,
			DeltaLabel = GetNode<Label>($"{rowPath}/DeltaLabel"),
			Bar = GetNode<Control>($"{rowPath}/BarColumn/AnimatedBar"),
			MainFill = GetNode<ColorRect>($"{rowPath}/BarColumn/AnimatedBar/MainFill"),
			FlashFill = GetNode<ColorRect>($"{rowPath}/BarColumn/AnimatedBar/FlashFill"),
			ValueLabel = GetNode<Label>($"{rowPath}/ValueLabel"),
			Controls = GetNode<Control>($"{rowPath}/Controls"),
			MinusButton = GetNode<Button>($"{rowPath}/Controls/MinusButton"),
			PlusButton = GetNode<Button>($"{rowPath}/Controls/PlusButton"),
			DeltaInput = GetNode<LineEdit>($"{rowPath}/Controls/DeltaInput"),
			ApplyButton = GetNode<Button>($"{rowPath}/Controls/ApplyButton"),
			CurrentValue = initialValue,
			TargetValue = initialValue,
			FlashValue = initialValue
		};
	}

	private void SetupMeter(Meter meter)
	{
		meter.Controls.Visible = ShowDebugControls;

		meter.MinusButton.ButtonDown += () => StartHolding(meter, -1.0);
		meter.MinusButton.ButtonUp += () => StopHolding(meter);
		meter.PlusButton.ButtonDown += () => StartHolding(meter, 1.0);
		meter.PlusButton.ButtonUp += () => StopHolding(meter);
		meter.ApplyButton.Pressed += () => ApplyInputDelta(meter);
		meter.DeltaInput.TextSubmitted += _ => ApplyInputDelta(meter);

		DrawMeter(meter);
		UpdateValueLabel(meter);
	}

	private void StartHolding(Meter meter, double direction)
	{
		_heldMeter = meter;
		_heldDirection = direction;
	}

	private void StopHolding(Meter meter)
	{
		if (_heldMeter != meter)
		{
			return;
		}

		_heldMeter = null;
		_heldDirection = 0.0;
	}

	private void ApplyInputDelta(Meter meter)
	{
		if (!TryParseDelta(meter.DeltaInput.Text, out double delta))
		{
			meter.DeltaInput.Text = "0";
			return;
		}

		double clampedDelta = SnapValue(Mathf.Clamp(delta, -100.0, 100.0));
		meter.DeltaInput.Text = clampedDelta.ToString($"F{DisplayDecimals}", System.Globalization.CultureInfo.InvariantCulture);
		ApplyDelta(meter, clampedDelta);
	}

	private bool TryParseDelta(string text, out double delta)
	{
		string normalizedText = text.Trim().Replace(',', '.');
		return double.TryParse(
			normalizedText,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out delta);
	}

	private void ApplyDelta(Meter meter, double delta)
	{
		if (Mathf.IsZeroApprox(delta))
		{
			return;
		}

		SetTarget(meter, meter.TargetValue + delta, true);
	}

	private void SetTarget(Meter meter, double value, bool animate)
	{
		double nextTarget = SnapValue(Mathf.Clamp(value, MinValue, MaxValue));

		if (!animate)
		{
			meter.TargetValue = nextTarget;
			meter.CurrentValue = nextTarget;
			meter.FlashValue = nextTarget;
			meter.Direction = 0.0;
			DrawMeter(meter);
			UpdateValueLabel(meter);
			return;
		}

		double oldVisibleValue = meter.Direction < 0.0
			? Mathf.Max(meter.FlashValue, meter.TargetValue)
			: meter.TargetValue;

		if (Mathf.IsEqualApprox(nextTarget, meter.TargetValue))
		{
			return;
		}

		meter.AnimationSpeed = GetAnimationSpeed(oldVisibleValue, nextTarget);
		meter.TargetValue = nextTarget;

		if (nextTarget > meter.CurrentValue)
		{
			meter.Direction = 1.0;
			meter.FlashValue = nextTarget;
		}
		else
		{
			meter.Direction = -1.0;
			meter.FlashValue = Mathf.Max(oldVisibleValue, meter.CurrentValue);
			meter.CurrentValue = nextTarget;
		}

		DrawMeter(meter);
		UpdateValueLabel(meter);
	}

	private double GetAnimationSpeed(double fromValue, double toValue)
	{
		double delta = Mathf.Abs(toValue - fromValue);
		return BaseAnimationSpeed + (delta * DeltaAnimationSpeedMultiplier);
	}

	private void UpdateMeterAnimation(Meter meter, double delta)
	{
		if (Mathf.IsZeroApprox(meter.Direction))
		{
			DrawMeter(meter);
			return;
		}

		double step = meter.AnimationSpeed * delta;

		if (meter.Direction > 0.0)
		{
			meter.CurrentValue = MoveToward(meter.CurrentValue, meter.TargetValue, step);
			if (Mathf.Abs(meter.CurrentValue - meter.TargetValue) <= Epsilon)
			{
				meter.CurrentValue = meter.TargetValue;
				meter.FlashValue = meter.TargetValue;
				meter.Direction = 0.0;
			}
		}
		else
		{
			meter.FlashValue = MoveToward(meter.FlashValue, meter.TargetValue, step);
			if (Mathf.Abs(meter.FlashValue - meter.TargetValue) <= Epsilon)
			{
				meter.FlashValue = meter.TargetValue;
				meter.Direction = 0.0;
			}
		}

		DrawMeter(meter);
	}

	private void DrawMeter(Meter meter)
	{
		float barWidth = meter.Bar.Size.X;
		float barHeight = meter.Bar.Size.Y;

		if (barWidth <= 0.0f || barHeight <= 0.0f)
		{
			return;
		}

		SetFillRect(meter.MainFill, 0.0, meter.CurrentValue, barWidth, barHeight);

		if (meter.Direction > 0.0)
		{
			SetFillRect(meter.FlashFill, meter.CurrentValue, meter.TargetValue, barWidth, barHeight);
		}
		else if (meter.Direction < 0.0)
		{
			SetFillRect(meter.FlashFill, meter.TargetValue, meter.FlashValue, barWidth, barHeight);
		}
		else
		{
			meter.FlashFill.Visible = false;
		}
	}

	private void SetFillRect(ColorRect fill, double fromValue, double toValue, float barWidth, float barHeight)
	{
		double clampedFrom = Mathf.Clamp(fromValue, MinValue, MaxValue);
		double clampedTo = Mathf.Clamp(toValue, MinValue, MaxValue);
		double leftValue = Mathf.Min(clampedFrom, clampedTo);
		double rightValue = Mathf.Max(clampedFrom, clampedTo);
		float left = (float)(barWidth * (leftValue / MaxValue));
		float right = (float)(barWidth * (rightValue / MaxValue));

		fill.Position = new Vector2(left, 0.0f);
		fill.Size = new Vector2(Mathf.Max(0.0f, right - left), barHeight);
		fill.Visible = fill.Size.X > 0.5f;
	}

	private void UpdateValueLabel(Meter meter)
	{
		meter.ValueLabel.Text = $"{meter.TargetValue.ToString($"F{DisplayDecimals}")}%";
	}

	private static double MoveToward(double from, double to, double delta)
	{
		if (from < to)
		{
			return Mathf.Min(from + delta, to);
		}

		return Mathf.Max(from - delta, to);
	}

	private static double SnapValue(double value)
	{
		return Mathf.Snapped(value, ValueStep);
	}

	private sealed class Meter
	{
		public Control Bar = null!;
		public ColorRect MainFill = null!;
		public ColorRect FlashFill = null!;
		public Label ValueLabel = null!;
		public Label DeltaLabel = null!;

		/// <summary>Рост шкалы — это хорошо для игрока? Верно только для лояльности.</summary>
		public bool HigherIsBetter;

		/// <summary>Сколько ещё держать подпись. Уходит в минус на время затухания.</summary>
		public double DeltaHold = -1000.0;

		public Control Controls = null!;
		public Button MinusButton = null!;
		public Button PlusButton = null!;
		public LineEdit DeltaInput = null!;
		public Button ApplyButton = null!;
		public double CurrentValue;
		public double TargetValue;
		public double FlashValue;
		public double Direction;
		public double AnimationSpeed;
	}
}
