using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Геймплейная сторона существа. Прозы здесь нет: имя и абзацы энциклопедии живут
	/// в текстовом движке (content/raw/creatures) под тем же id, что и это определение.
	///
	/// Свойства (ДД, раздел 10 — 3 на существо) — это только их идентификаторы. Ядро
	/// решает, какое свойство проявилось и раскрыто ли оно; какой абзац за ним стоит,
	/// знает текст, где абзац помечен %% reveal: <id свойства> %%. Номера абзацев
	/// намеренно не хранятся: вставка абзаца в текст сдвигала бы нумерацию и молча
	/// ломала уже сохранённые раскрытия.
	/// </summary>
	public sealed class CreatureDefinition
	{
		public string Id { get; set; } = string.Empty;

		/// <summary>Теги для условий спецспособностей: «мимик», «перекожник» и т. д.</summary>
		public List<string> Tags { get; } = new List<string>();

		/// <summary>Id скрытых свойств. Каждому в тексте соответствует условный абзац.</summary>
		public List<string> Properties { get; } = new List<string>();

		public string IllustrationId { get; set; } = string.Empty;

		public bool HasProperty(string propertyId)
		{
			for (int i = 0; i < Properties.Count; i++)
			{
				if (string.Equals(Properties[i], propertyId, System.StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}
	}
}
