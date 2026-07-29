using Godot;
using Kontur.Core.Content;

/// <summary>
/// Реализация порта <see cref="ITextCatalog"/> поверх автозагрузки Content.
///
/// Нужна только для сверки при загрузке: ядро спрашивает, есть ли статья под существо и
/// есть ли в ней условный блок под каждое объявленное свойство. Опечатка в id тогда
/// падает при старте с внятным сообщением, а не превращается в пустой абзац на смене.
/// Сам текст ядру не отдаётся — его разворачивают текстовые боксы.
/// </summary>
public sealed class GodotTextCatalog : ITextCatalog
{
	public bool HasEntry(string entryId)
	{
		Content content = Content.Instance;
		return content != null && content.TryGetEntry(entryId, out _);
	}

	public bool HasProperty(string entryId, string propertyId)
	{
		Content content = Content.Instance;
		if (content == null || !content.TryGetEntry(entryId, out ContentEntry entry))
		{
			return false;
		}

		foreach (ContentChunk chunk in entry.Chunks)
		{
			if (chunk.Reveal.Length > 0
				&& chunk.Reveal.Equals(propertyId, System.StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
