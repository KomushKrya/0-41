using System;

namespace Kontur.Core.Model
{
	/// <summary>Три шкалы состояния (ДД, раздел 11).</summary>
	public enum ScaleKind
	{
		Infection = 0,
		Publicity = 1,
		Loyalty = 2
	}

	public readonly struct ScaleValues
	{
		public ScaleValues(double infection, double publicity, double loyalty)
		{
			Infection = infection;
			Publicity = publicity;
			Loyalty = loyalty;
		}

		public double Infection { get; }

		public double Publicity { get; }

		public double Loyalty { get; }

		public double this[ScaleKind kind]
		{
			get
			{
				switch (kind)
				{
					case ScaleKind.Infection: return Infection;
					case ScaleKind.Publicity: return Publicity;
					case ScaleKind.Loyalty: return Loyalty;
					default: return 0.0;
				}
			}
		}

		public ScaleValues Apply(ScaleDelta delta, double min, double max)
		{
			return new ScaleValues(
				Clamp(Infection + delta.Infection, min, max),
				Clamp(Publicity + delta.Publicity, min, max),
				Clamp(Loyalty + delta.Loyalty, min, max));
		}

		public override string ToString()
		{
			return string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"Заражение {0:0.#} / Гласность {1:0.#} / Лояльность {2:0.#}",
				Infection,
				Publicity,
				Loyalty);
		}

		private static double Clamp(double value, double min, double max)
		{
			return Math.Min(max, Math.Max(min, value));
		}
	}

	public readonly struct ScaleDelta
	{
		public ScaleDelta(double infection, double publicity, double loyalty)
		{
			Infection = infection;
			Publicity = publicity;
			Loyalty = loyalty;
		}

		public double Infection { get; }

		public double Publicity { get; }

		public double Loyalty { get; }

		public static ScaleDelta Zero
		{
			get { return new ScaleDelta(0.0, 0.0, 0.0); }
		}

		public bool IsZero
		{
			get { return Infection == 0.0 && Publicity == 0.0 && Loyalty == 0.0; }
		}

		public ScaleDelta Add(ScaleDelta other)
		{
			return new ScaleDelta(
				Infection + other.Infection,
				Publicity + other.Publicity,
				Loyalty + other.Loyalty);
		}

		public ScaleDelta Scale(double factor)
		{
			return new ScaleDelta(Infection * factor, Publicity * factor, Loyalty * factor);
		}

		public override string ToString()
		{
			return string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"[Зар {0:+0.#;-0.#;0} | Глас {1:+0.#;-0.#;0} | Лоял {2:+0.#;-0.#;0}]",
				Infection,
				Publicity,
				Loyalty);
		}
	}

	/// <summary>Причина мгновенного проигрыша (ДД, раздел 11).</summary>
	public enum GameOverReason
	{
		InfectionMaxed = 0,
		PublicityMaxed = 1,
		LoyaltyDepleted = 2
	}
}
