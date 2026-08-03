namespace Kontur.Core.Simulation
{
	/// <summary>
	/// Обратный отсчёт в секундах симуляции. Не использует системное время —
	/// продвигается только через Tick(delta), поэтому прогон детерминирован
	/// и может идти быстрее реального времени в headless-режиме.
	/// </summary>
	public sealed class Countdown
	{
		private Countdown(double duration)
		{
			Duration = duration;
			Remaining = duration;
			IsRunning = true;
		}

		public double Duration { get; private set; }

		public double Remaining { get; private set; }

		public bool IsRunning { get; private set; }

		public double NormalizedRemaining
		{
			get { return Duration <= 0.0 ? 0.0 : Remaining / Duration; }
		}

		public static Countdown Start(double durationSeconds)
		{
			return new Countdown(durationSeconds);
		}

		public static Countdown Restore(double durationSeconds, double remainingSeconds, bool isRunning)
		{
			var countdown = new Countdown(durationSeconds)
			{
				Remaining = System.Math.Max(0.0, remainingSeconds),
				IsRunning = isRunning && remainingSeconds > 0.0
			};
			return countdown;
		}

		/// <summary>Возвращает true ровно один раз — в тик, когда таймер истёк.</summary>
		public bool Tick(double delta)
		{
			if (!IsRunning)
			{
				return false;
			}

			Remaining -= delta;
			if (Remaining > 0.0)
			{
				return false;
			}

			Remaining = 0.0;
			IsRunning = false;
			return true;
		}

		public void Stop()
		{
			IsRunning = false;
		}
	}
}
