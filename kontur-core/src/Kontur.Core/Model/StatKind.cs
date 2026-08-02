using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Характеристики сотрудника (ДД, раздел 5 — «4–5 характеристик по типу S.P.E.C.I.A.L.»).
	/// Порядок значений enum зафиксирован: он используется как индекс в StatBlock и в JSON-контенте.
	/// </summary>
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
			StatKind.Strength,
			StatKind.Combat,
			StatKind.Agility,
			StatKind.Charisma,
			StatKind.Intellect
		};

		private static readonly Dictionary<StatKind, string> RussianNames = new Dictionary<StatKind, string>
		{
			{ StatKind.Strength, "Сила" },
			{ StatKind.Combat, "Боевая подготовка" },
			{ StatKind.Agility, "Ловкость" },
			{ StatKind.Charisma, "Харизма" },
			{ StatKind.Intellect, "Интеллект" }
		};

		/// <summary>Отображаемое имя. В финальной игре заменяется ключом локализации.</summary>
		public static string GetDisplayName(StatKind kind)
		{
			string? name;
			return RussianNames.TryGetValue(kind, out name) && name != null ? name : kind.ToString();
		}

		public static bool TryParse(string value, out StatKind kind)
		{
			return System.Enum.TryParse<StatKind>(value, true, out kind);
		}
	}
}
