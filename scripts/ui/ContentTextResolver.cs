using System;
using System.Text;

/// <summary>
/// Небольшой адаптер для старых Label-интерфейсов. Он даёт им читать готовый
/// текст текстового движка по id, пока экраны не переведены на ContentTextBox.
/// Пустой или отсутствующий id всегда возвращает fallback и не ломает сцену.
/// </summary>
public static class ContentTextResolver
{
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

	/// <summary>
	/// Человеческое название записи — то, что автор написал в поле name.
	///
	/// Заголовок экрана звонка и рации: имя задания («Жалоба пенсионера»).
	/// Имена заданий лежат записями типа mission_id под id самой миссии.
	/// </summary>
	public static string ResolveEntryName(string contentId, string fallback)
	{
		ContentEntry entry = FindEntry(contentId);
		return entry != null && !string.IsNullOrWhiteSpace(entry.Name)
			? entry.Name
			: (fallback ?? string.Empty);
	}

	private static ContentEntry FindEntry(string contentId)
	{
		return string.IsNullOrWhiteSpace(contentId) || Content.Instance == null
			? null
			: Content.Instance.GetEntry(contentId);
	}
}
