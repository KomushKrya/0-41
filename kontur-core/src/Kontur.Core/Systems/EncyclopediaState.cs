using System;
using System.Collections.Generic;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Что игрок знает о существах (ДД, раздел 10).
	/// Абзац 0 — базовый, открывается при первом опознании существа.
	/// Абзацы 1..3 открываются, когда соответствующее свойство проявилось и было замечено.
	/// </summary>
	public sealed class EncyclopediaState
	{
		private readonly Dictionary<string, HashSet<int>> _revealedParagraphs =
			new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

		public void Clear()
		{
			_revealedParagraphs.Clear();
		}

		public bool IsCreatureKnown(string creatureId)
		{
			return _revealedParagraphs.ContainsKey(creatureId);
		}

		public bool IsParagraphRevealed(string creatureId, int paragraphIndex)
		{
			HashSet<int>? set;
			return _revealedParagraphs.TryGetValue(creatureId, out set) && set.Contains(paragraphIndex);
		}

		public IReadOnlyCollection<int> GetRevealedParagraphs(string creatureId)
		{
			HashSet<int>? set;
			if (_revealedParagraphs.TryGetValue(creatureId, out set))
			{
				return set;
			}

			return Array.Empty<int>();
		}

		public IReadOnlyCollection<string> GetKnownCreatureIds()
		{
			return _revealedParagraphs.Keys;
		}

		/// <summary>Возвращает true, если существо опознано впервые.</summary>
		public bool Identify(string creatureId)
		{
			if (_revealedParagraphs.ContainsKey(creatureId))
			{
				return false;
			}

			_revealedParagraphs[creatureId] = new HashSet<int> { 0 };
			return true;
		}

		/// <summary>Возвращает true, если абзац действительно открыт этим вызовом.</summary>
		public bool RevealParagraph(string creatureId, int paragraphIndex)
		{
			HashSet<int>? set;
			if (!_revealedParagraphs.TryGetValue(creatureId, out set))
			{
				set = new HashSet<int> { 0 };
				_revealedParagraphs[creatureId] = set;
			}

			return set.Add(paragraphIndex);
		}
	}
}
