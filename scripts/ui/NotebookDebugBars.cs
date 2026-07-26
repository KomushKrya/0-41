using Godot;
using System.Globalization;

public partial class NotebookDebugBars : Control
{
	[Export] public double HoldChangePerSecond { get; set; } = 25.0;
	[Export] public double BaseAnimationSpeed { get; set; } = 12.0;
	[Export] public double DeltaAnimationSpeedMultiplier { get; set; } = 1.15;
	[Export] public int DisplayDecimals { get; set; } = 2;

	private const double MinValue = 0.0;
	private const double MaxValue = 100.0;
	private const double ValueStep = 0.01;
	private const double Epsilon = 0.001;

	private Meter _infection = null!;
	private Meter _publicity = null!;
	private Meter _loyalty = null!;
	private Meter _heldMeter = null!;
	private double _heldDirection;

	public override void _Ready()
	{
		_infection = CreateMeter("Parameters/InfectionRow/MarginContainer/RowContent", 30.0);
		_publicity = CreateMeter("Parameters/PublicityRow/MarginContainer/RowContent", 20.0);
		_loyalty = CreateMeter("Parameters/LoyaltyRow/MarginContainer/RowContent", 70.0);

		SetupMeter(_infection);
		SetupMeter(_publicity);
		SetupMeter(_loyalty);
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
	}

	private Meter CreateMeter(string rowPath, double initialValue)
	{
		return new Meter
		{
			Bar = GetNode<Control>($"{rowPath}/BarColumn/AnimatedBar"),
			MainFill = GetNode<ColorRect>($"{rowPath}/BarColumn/AnimatedBar/MainFill"),
			FlashFill = GetNode<ColorRect>($"{rowPath}/BarColumn/AnimatedBar/FlashFill"),
			ValueLabel = GetNode<Label>($"{rowPath}/ValueLabel"),
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
		meter.DeltaInput.Text = clampedDelta.ToString($"F{DisplayDecimals}", CultureInfo.InvariantCulture);
		ApplyDelta(meter, clampedDelta);
	}

	private bool TryParseDelta(string text, out double delta)
	{
		string normalizedText = text.Trim().Replace(',', '.');
		return double.TryParse(normalizedText, NumberStyles.Float, CultureInfo.InvariantCulture, out delta);
	}

	private void ApplyDelta(Meter meter, double delta)
	{
		if (Mathf.IsZeroApprox(delta))
		{
			return;
		}

		double oldVisibleValue = meter.Direction < 0.0
			? Mathf.Max(meter.FlashValue, meter.TargetValue)
			: meter.TargetValue;

		double nextTarget = SnapValue(Mathf.Clamp(meter.TargetValue + delta, MinValue, MaxValue));
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
