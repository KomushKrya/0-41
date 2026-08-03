using System;
using System.Text;

namespace Kontur.Core.Model
{
	/// <summary>Неизменяемый набор характеристик сотрудника, миссии и снаряжения.</summary>
	public readonly struct StatBlock : IEquatable<StatBlock>
	{
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
		public static StatBlock Zero => new StatBlock(0, 0, 0, 0, 0);
		public int this[StatKind kind] => kind switch
		{
			StatKind.Strength => Strength,
			StatKind.Intellect => Intellect,
			StatKind.Combat => Combat,
			StatKind.Agility => Agility,
			StatKind.Charisma => Charisma,
			_ => 0
		};
		public int Total => Strength + Intellect + Combat + Agility + Charisma;
		public static StatBlock Uniform(int value) => new StatBlock(value, value, value, value, value);

		public StatBlock With(StatKind kind, int value) => kind switch
		{
			StatKind.Strength => new StatBlock(value, Intellect, Combat, Agility, Charisma),
			StatKind.Intellect => new StatBlock(Strength, value, Combat, Agility, Charisma),
			StatKind.Combat => new StatBlock(Strength, Intellect, value, Agility, Charisma),
			StatKind.Agility => new StatBlock(Strength, Intellect, Combat, value, Charisma),
			StatKind.Charisma => new StatBlock(Strength, Intellect, Combat, Agility, value),
			_ => this
		};
		public StatBlock Add(StatKind kind, int delta) => With(kind, this[kind] + delta);
		public StatBlock Add(StatBlock other) => new StatBlock(Strength + other.Strength, Intellect + other.Intellect, Combat + other.Combat, Agility + other.Agility, Charisma + other.Charisma);
		public StatBlock Scale(double factor) => new StatBlock(ScaleValue(Strength, factor), ScaleValue(Intellect, factor), ScaleValue(Combat, factor), ScaleValue(Agility, factor), ScaleValue(Charisma, factor));
		public StatBlock ClampMin(int minValue) => new StatBlock(Math.Max(minValue, Strength), Math.Max(minValue, Intellect), Math.Max(minValue, Combat), Math.Max(minValue, Agility), Math.Max(minValue, Charisma));
		public static StatBlock operator +(StatBlock left, StatBlock right) => left.Add(right);
		public bool Equals(StatBlock other) => Strength == other.Strength && Intellect == other.Intellect && Combat == other.Combat && Agility == other.Agility && Charisma == other.Charisma;
		public override bool Equals(object? obj) => obj is StatBlock other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Strength, Intellect, Combat, Agility, Charisma);
		public override string ToString()
		{
			var builder = new StringBuilder();
			foreach (StatKind kind in StatKinds.All)
			{
				if (this[kind] == 0) continue;
				if (builder.Length > 0) builder.Append(' ');
				builder.Append(StatKinds.GetDisplayName(kind)).Append(' ').Append(this[kind]);
			}
			return builder.Length == 0 ? "—" : builder.ToString();
		}
		private static int ScaleValue(int value, double factor) => value <= 0 ? 0 : (int)Math.Ceiling(value * factor);
	}
}
