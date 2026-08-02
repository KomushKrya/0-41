using System;
using System.Text;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Неизменяемый набор из пяти характеристик. Используется и для сотрудника,
	/// и для требований миссии, и для бонусов снаряжения — арифметика одна и та же.
	/// </summary>
	public readonly struct StatBlock : IEquatable<StatBlock>
	{
		/// <summary>
		/// Порядок аргументов — исторический порядок слотов, он же порядок полей ниже.
		/// Порядок показа характеристик игроку задаёт StatKinds.All и он другой:
		/// перечисление там идёт так, как читается в интерфейсе.
		/// </summary>
		public StatBlock(int strength, int intellect, int combat, int agility, int charisma)
		{
			Strength = strength;
			Intellect = intellect;
			Combat = combat;
			Agility = agility;
			Charisma = charisma;
		}

		public int Strength { get; }

		public int Intellect { get; }

		public int Combat { get; }

		public int Agility { get; }

		public int Charisma { get; }

		public static StatBlock Zero
		{
			get { return new StatBlock(0, 0, 0, 0, 0); }
		}

		public int this[StatKind kind]
		{
			get
			{
				switch (kind)
				{
					case StatKind.Strength: return Strength;
					case StatKind.Intellect: return Intellect;
					case StatKind.Combat: return Combat;
					case StatKind.Agility: return Agility;
					case StatKind.Charisma: return Charisma;
					default: return 0;
				}
			}
		}

		public int Total
		{
			get { return Strength + Intellect + Combat + Agility + Charisma; }
		}

		public static StatBlock Uniform(int value)
		{
			return new StatBlock(value, value, value, value, value);
		}

		public StatBlock With(StatKind kind, int value)
		{
			switch (kind)
			{
				case StatKind.Strength: return new StatBlock(value, Intellect, Combat, Agility, Charisma);
				case StatKind.Intellect: return new StatBlock(Strength, value, Combat, Agility, Charisma);
				case StatKind.Combat: return new StatBlock(Strength, Intellect, value, Agility, Charisma);
				case StatKind.Agility: return new StatBlock(Strength, Intellect, Combat, value, Charisma);
				case StatKind.Charisma: return new StatBlock(Strength, Intellect, Combat, Agility, value);
				default: return this;
			}
		}

		public StatBlock Add(StatKind kind, int delta)
		{
			return With(kind, this[kind] + delta);
		}

		public StatBlock Add(StatBlock other)
		{
			return new StatBlock(
				Strength + other.Strength,
				Intellect + other.Intellect,
				Combat + other.Combat,
				Agility + other.Agility,
				Charisma + other.Charisma);
		}

		/// <summary>Поэлементное умножение с округлением вверх — используется радио-вариантами (множитель требований).</summary>
		public StatBlock Scale(double factor)
		{
			return new StatBlock(
				ScaleValue(Strength, factor),
				ScaleValue(Intellect, factor),
				ScaleValue(Combat, factor),
				ScaleValue(Agility, factor),
				ScaleValue(Charisma, factor));
		}

		public StatBlock ClampMin(int minValue)
		{
			return new StatBlock(
				Math.Max(minValue, Strength),
				Math.Max(minValue, Intellect),
				Math.Max(minValue, Combat),
				Math.Max(minValue, Agility),
				Math.Max(minValue, Charisma));
		}

		public static StatBlock operator +(StatBlock left, StatBlock right)
		{
			return left.Add(right);
		}

		public bool Equals(StatBlock other)
		{
			return Strength == other.Strength
				&& Intellect == other.Intellect
				&& Combat == other.Combat
				&& Agility == other.Agility
				&& Charisma == other.Charisma;
		}

		public override bool Equals(object? obj)
		{
			return obj is StatBlock other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Strength, Intellect, Combat, Agility, Charisma);
		}

		public override string ToString()
		{
			var builder = new StringBuilder();
			bool first = true;
			for (int i = 0; i < StatKinds.All.Length; i++)
			{
				StatKind kind = StatKinds.All[i];
				int value = this[kind];
				if (value == 0)
				{
					continue;
				}

				if (!first)
				{
					builder.Append(' ');
				}

				builder.Append(StatKinds.GetDisplayName(kind)).Append(' ').Append(value);
				first = false;
			}

			return first ? "—" : builder.ToString();
		}

		private static int ScaleValue(int value, double factor)
		{
			if (value <= 0)
			{
				return 0;
			}

			return (int)Math.Ceiling(value * factor);
		}
	}
}
