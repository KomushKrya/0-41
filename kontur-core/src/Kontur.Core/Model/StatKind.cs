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
		Charisma = 3,
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
			StatKind.Charisma,
			StatKind.Composure
		};

		private static readonly Dictionary<StatKind, string> Ids = new Dictionary<StatKind, string>
		{
			{ StatKind.Strength, "strength" },
			{ StatKind.Perception, "perception" },
			{ StatKind.Endurance, "endurance" },
			{ StatKind.Charisma, "charisma" },
			{ StatKind.Composure, "composure" }
		};

		/// <summary>
		/// Id характеристики — он же id записи контента с её названием и описанием
		/// (content/raw/UI/hover_footnote/characteristics). Названий ядро не знает: показывать
		/// «Наблюдательность» или «Perception» — дело интерфейса и локали.
		/// </summary>
		public static string GetId(StatKind kind)
		{
			string id;
			return Ids.TryGetValue(kind, out id) ? id : kind.ToString().ToLowerInvariant();
		}

		public static bool TryParse(string value, out StatKind kind)
		{
			return System.Enum.TryParse<StatKind>(value, true, out kind);
		}
	}
}
