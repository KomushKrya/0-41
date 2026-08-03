using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>Единая номенклатура характеристик из main.</summary>
	public enum StatKind
	{
		Strength = 0,
		Combat = 1,
		Agility = 2,
		Charisma = 3,
		Intellect = 4
	}

	public static class StatKinds
	{
		public const int Count = 5;
		public static readonly StatKind[] All =
		{
			StatKind.Strength, StatKind.Combat, StatKind.Agility,
			StatKind.Charisma, StatKind.Intellect
		};

		private static readonly Dictionary<StatKind, string> Names = new()
		{
			{ StatKind.Strength, "Сила" },
			{ StatKind.Combat, "Боевая подготовка" },
			{ StatKind.Agility, "Ловкость" },
			{ StatKind.Charisma, "Харизма" },
			{ StatKind.Intellect, "Интеллект" }
		};

		public static string GetDisplayName(StatKind kind)
		{
			return Names.TryGetValue(kind, out string? name) ? name : kind.ToString();
		}

		public static bool TryParse(string value, out StatKind kind)
		{
			return System.Enum.TryParse(value, true, out kind);
		}
	}
}
