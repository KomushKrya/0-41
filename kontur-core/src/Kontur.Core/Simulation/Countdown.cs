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

		/// <summary>
		/// Восстановить таймер из сохранения: не с начала, а с той секунды, на которой
		/// игрок сохранился. Иначе загрузка посреди звонка дарила бы полные 15 секунд.
		/// </summary>
		public static Countdown Restore(double duration, double remaining, bool isRunning)
		{
			var countdown = new Countdown(duration);
			countdown.Remaining = remaining < 0.0 ? 0.0 : remaining;
			countdown.IsRunning = isRunning;
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
