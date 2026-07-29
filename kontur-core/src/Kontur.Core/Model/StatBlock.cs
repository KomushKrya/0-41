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
		public StatBlock(int strength, int perception, int endurance, int agility, int composure)
		{
			Strength = strength;
			Perception = perception;
			Endurance = endurance;
			Agility = agility;
			Composure = composure;
		}

		public int Strength { get; }

		public int Perception { get; }

		public int Endurance { get; }

		public int Agility { get; }

		public int Composure { get; }

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
					case StatKind.Perception: return Perception;
					case StatKind.Endurance: return Endurance;
					case StatKind.Agility: return Agility;
					case StatKind.Composure: return Composure;
					default: return 0;
				}
			}
		}

		public int Total
		{
			get { return Strength + Perception + Endurance + Agility + Composure; }
		}

		public static StatBlock Uniform(int value)
		{
			return new StatBlock(value, value, value, value, value);
		}

		public StatBlock With(StatKind kind, int value)
		{
			switch (kind)
			{
				case StatKind.Strength: return new StatBlock(value, Perception, Endurance, Agility, Composure);
				case StatKind.Perception: return new StatBlock(Strength, value, Endurance, Agility, Composure);
				case StatKind.Endurance: return new StatBlock(Strength, Perception, value, Agility, Composure);
				case StatKind.Agility: return new StatBlock(Strength, Perception, Endurance, value, Composure);
				case StatKind.Composure: return new StatBlock(Strength, Perception, Endurance, Agility, value);
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
				Perception + other.Perception,
				Endurance + other.Endurance,
				Agility + other.Agility,
				Composure + other.Composure);
		}

		/// <summary>Поэлементное умножение с округлением вверх — используется радио-вариантами (множитель требований).</summary>
		public StatBlock Scale(double factor)
		{
			return new StatBlock(
				ScaleValue(Strength, factor),
				ScaleValue(Perception, factor),
				ScaleValue(Endurance, factor),
				ScaleValue(Agility, factor),
				ScaleValue(Composure, factor));
		}

		public StatBlock ClampMin(int minValue)
		{
			return new StatBlock(
				Math.Max(minValue, Strength),
				Math.Max(minValue, Perception),
				Math.Max(minValue, Endurance),
				Math.Max(minValue, Agility),
				Math.Max(minValue, Composure));
		}

		public static StatBlock operator +(StatBlock left, StatBlock right)
		{
			return left.Add(right);
		}

		public bool Equals(StatBlock other)
		{
			return Strength == other.Strength
				&& Perception == other.Perception
				&& Endurance == other.Endurance
				&& Agility == other.Agility
				&& Composure == other.Composure;
		}

		public override bool Equals(object? obj)
		{
			return obj is StatBlock other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Strength, Perception, Endurance, Agility, Composure);
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
