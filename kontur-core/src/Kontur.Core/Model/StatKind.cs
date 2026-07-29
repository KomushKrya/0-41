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
		Perception = 1,
		Endurance = 2,
		Agility = 3,
		Composure = 4
	}

	public static class StatKinds
	{
		public const int Count = 5;

		public static readonly StatKind[] All =
		{
			StatKind.Strength,
			StatKind.Perception,
			StatKind.Endurance,
			StatKind.Agility,
			StatKind.Composure
		};

		private static readonly Dictionary<StatKind, string> RussianNames = new Dictionary<StatKind, string>
		{
			{ StatKind.Strength, "Сила" },
			{ StatKind.Perception, "Восприятие" },
			{ StatKind.Endurance, "Выносливость" },
			{ StatKind.Agility, "Ловкость" },
			{ StatKind.Composure, "Хладнокровие" }
		};

		/// <summary>Отображаемое имя. В финальной игре заменяется ключом локализации.</summary>
		public static string GetDisplayName(StatKind kind)
		{
			string name;
			return RussianNames.TryGetValue(kind, out name) ? name : kind.ToString();
		}

		public static bool TryParse(string value, out StatKind kind)
		{
			return System.Enum.TryParse<StatKind>(value, true, out kind);
		}
	}
}
