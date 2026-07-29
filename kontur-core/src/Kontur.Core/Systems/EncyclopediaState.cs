using System;
using System.Collections.Generic;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Что игрок знает об существах (ДД, раздел 10).
	///
	/// Ключ раскрытия — id свойства, а не номер абзаца: текст статьи можно править и
	/// переставлять, не ломая уже сохранённые раскрытия. Какой абзац стоит за свойством,
	/// знает текстовый движок — там абзац помечен %% reveal: <id свойства> %%.
	///
	/// Существо, попавшее в словарь, считается опознанным: вводный абзац статьи
	/// показывается без всяких свойств, поэтому отдельного признака для него не нужно.
	/// </summary>
	public sealed class EncyclopediaState
	{
		private readonly Dictionary<string, HashSet<string>> _revealed =
			new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

		public void Clear()
		{
			_revealed.Clear();
		}

		public bool IsCreatureKnown(string creatureId)
		{
			return _revealed.ContainsKey(creatureId);
		}

		public bool IsPropertyRevealed(string creatureId, string propertyId)
		{
			HashSet<string>? set;
			return _revealed.TryGetValue(creatureId, out set) && set.Contains(propertyId);
		}

		public IReadOnlyCollection<string> GetRevealedProperties(string creatureId)
		{
			HashSet<string>? set;
			if (_revealed.TryGetValue(creatureId, out set))
			{
				return set;
			}

			return Array.Empty<string>();
		}

		public IReadOnlyCollection<string> GetKnownCreatureIds()
		{
			return _revealed.Keys;
		}

		/// <summary>Возвращает true, если существо опознано впервые.</summary>
		public bool Identify(string creatureId)
		{
			if (_revealed.ContainsKey(creatureId))
			{
				return false;
			}

			_revealed[creatureId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			return true;
		}

		/// <summary>Возвращает true, если свойство действительно открыто этим вызовом.</summary>
		public bool RevealProperty(string creatureId, string propertyId)
		{
			HashSet<string>? set;
			if (!_revealed.TryGetValue(creatureId, out set))
			{
				set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_revealed[creatureId] = set;
			}

			return set.Add(propertyId);
		}
	}
}
