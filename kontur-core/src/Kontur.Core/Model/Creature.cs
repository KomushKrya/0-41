using System.Collections.Generic;

namespace Kontur.Core.Model
{
	/// <summary>
	/// Скрытое свойство существа (ДД, раздел 10 — 3 свойства на существо).
	/// Раскрывается, если проявилось во время вызова и было замечено сотрудниками.
	/// </summary>
	public sealed class CreatureProperty
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		/// <summary>Индекс абзаца в Paragraphs, который откроется при раскрытии (1..3).</summary>
		public int ParagraphIndex { get; set; } = 1;
	}

	public sealed class CreatureDefinition
	{
		public string Id { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		/// <summary>Теги для условий спецспособностей: «мимик», «перекожник» и т. д.</summary>
		public List<string> Tags { get; } = new List<string>();

		/// <summary>4 абзаца. Индекс 0 доступен изначально, 1..3 открываются свойствами.</summary>
		public List<string> Paragraphs { get; } = new List<string>();

		public List<CreatureProperty> Properties { get; } = new List<CreatureProperty>();

		public string IllustrationId { get; set; } = string.Empty;

		public CreatureProperty? FindProperty(string propertyId)
		{
			for (int i = 0; i < Properties.Count; i++)
			{
				if (string.Equals(Properties[i].Id, propertyId, System.StringComparison.OrdinalIgnoreCase))
				{
					return Properties[i];
				}
			}

			return null;
		}
	}
}
