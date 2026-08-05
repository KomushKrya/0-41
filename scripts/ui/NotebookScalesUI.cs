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
/// </summary>
public partial class NotebookScalesUI : Control
{
	/// <summary>Что рисовать, пока ядро не поднялось: заражение / гласность / лояльность.</summary>
	[Export] public Vector3 FallbackScales { get; set; } = new(20.0f, 15.0f, 70.0f);

	[Export] public double BaseAnimationSpeed { get; set; } = 12.0;
	[Export] public double DeltaAnimationSpeedMultiplier { get; set; } = 1.15;

	private const double MinValue = 0.0;
	private const double MaxValue = 100.0;
	private const double ValueStep = 0.01;
	private const double Epsilon = 0.001;

	private Meter _infection = null!;
	private Meter _publicity = null!;
	private Meter _loyalty = null!;

	private IDisposable _scalesSubscription;
	private IDisposable _shiftStartedSubscription;

	public override void _Ready()
	{
		_infection = CreateMeter("Parameters/InfectionRow/MarginContainer/RowContent", FallbackScales.X);
		_publicity = CreateMeter("Parameters/PublicityRow/MarginContainer/RowContent", FallbackScales.Y);
		_loyalty = CreateMeter("Parameters/LoyaltyRow/MarginContainer/RowContent", FallbackScales.Z);

		DrawMeter(_infection);
		DrawMeter(_publicity);
		DrawMeter(_loyalty);

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			GD.PushWarning("NotebookScalesUI: GameRuntime is not ready; showing fallback scales.");
			return;
		}

		SyncFromCore();

		_scalesSubscription = runtime.Session.Events.Subscribe<ScalesChanged>(e => ApplyScales(e.Values, true));

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
		UpdateMeterAnimation(_infection, delta);
		UpdateMeterAnimation(_publicity, delta);
		UpdateMeterAnimation(_loyalty, delta);
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

	private Meter CreateMeter(string rowPath, double initialValue)
	{
		return new Meter
		{
			Bar = GetNode<Control>($"{rowPath}/BarColumn/AnimatedBar"),
			MainFill = GetNode<Control>($"{rowPath}/BarColumn/AnimatedBar/MainFill"),
			FlashFill = GetNode<ColorRect>($"{rowPath}/BarColumn/AnimatedBar/FlashFill"),
			CurrentValue = initialValue,
			TargetValue = initialValue,
			FlashValue = initialValue
		};
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

	private void SetFillRect(Control fill, double fromValue, double toValue, float barWidth, float barHeight)
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
		public Control MainFill = null!;
		public ColorRect FlashFill = null!;
		public double CurrentValue;
		public double TargetValue;
		public double FlashValue;
		public double Direction;
		public double AnimationSpeed;
	}
}
