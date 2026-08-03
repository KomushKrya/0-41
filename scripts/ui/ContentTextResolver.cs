using System;
using System.Text;

/// <summary>
/// Небольшой адаптер для старых Label-интерфейсов. Он даёт им читать готовый
/// текст текстового движка по id, пока экраны не переведены на ContentTextBox.
/// Пустой или отсутствующий id всегда возвращает fallback и не ломает сцену.
/// </summary>
public static class ContentTextResolver
{
	public static string ResolveEntryText(string contentId, string fallback)
	{
		ContentEntry entry = FindEntry(contentId);
		if (entry == null || entry.Chunks.Count == 0)
		{
			return fallback ?? string.Empty;
		}

		var text = new StringBuilder();
		foreach (ContentChunk chunk in entry.Chunks)
		{
			if (chunk.Kind.Equals("call_meta", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (text.Length > 0)
			{
				text.AppendLine().AppendLine();
			}

			text.Append(chunk.Text);
		}

		return text.Length > 0 ? text.ToString() : (fallback ?? string.Empty);
	}

	public static string ResolveCallMeta(string contentId, string fallback)
	{
		ContentEntry entry = FindEntry(contentId);
		if (entry != null)
		{
			foreach (ContentChunk chunk in entry.Chunks)
			{
				if (chunk.Kind.Equals("call_meta", StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrWhiteSpace(chunk.Text))
				{
					return chunk.Text;
				}
			}
		}

		return fallback ?? string.Empty;
	}

	public static string ResolveOptionName(string contentId, string optionId, string fallback)
	{
		ContentEntry entry = FindEntry(contentId);
		if (entry != null)
		{
			foreach (ContentOption option in entry.Options)
			{
				if (option.Id.Equals(optionId, StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrWhiteSpace(option.Name))
				{
					return option.Name;
				}
			}
		}

		return fallback ?? string.Empty;
	}

	private static ContentEntry FindEntry(string contentId)
	{
		return string.IsNullOrWhiteSpace(contentId) || Content.Instance == null
			? null
			: Content.Instance.GetEntry(contentId);
	}
}
