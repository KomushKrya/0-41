using Godot;

/// <summary>
/// Кольцевой обратный отсчёт над целью на карте. Дуга начинается сверху
/// и уменьшается по часовой стрелке.
/// </summary>
public partial class MapMarkerCountdownRing : Control
{
	[Export] public float Radius { get; set; } = 15.0f;
	[Export] public float LineWidth { get; set; } = 10.0f;
	[Export] public Color TrackColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.0f);
	[Export] public Color ProgressColor { get; set; } = new(0.38f, 0.38f, 0.38f, 1.0f);

	private double _duration;
	private double _remaining;

	public void Start(double durationSeconds)
	{
		_duration = durationSeconds;
		SetRemaining(durationSeconds);
	}

	/// <summary>Обновляет отображение по времени, полученному из core.</summary>
	public void SetRemaining(double remainingSeconds)
	{
		_remaining = Mathf.Clamp(remainingSeconds, 0.0, _duration);
		Visible = _duration > 0.0 && _remaining > 0.0;
		QueueRedraw();
	}

	public void ShowFull(Color color)
	{
		ProgressColor = color;
		_duration = 1.0;
		_remaining = 1.0;
		Visible = true;
		QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 center = Size * 0.5f;
		DrawArc(center, Radius, 0.0f, Mathf.Tau, 64, TrackColor, LineWidth, true);

		if (_duration <= 0.0 || _remaining <= 0.0)
		{
			return;
		}

		float progress = Mathf.Clamp((float)(_remaining / _duration), 0.0f, 1.0f);
		float startAngle = -Mathf.Pi * 0.5f;
		DrawArc(center, Radius, startAngle, startAngle - Mathf.Tau * progress, 64, ProgressColor, LineWidth, true);
	}
}
