#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;

/// <summary>
/// Картотека существ: слева список, справа собранные сведения.
///
/// Ядро знает только id и то, какие свойства вскрыты; названия и текст живут
/// в текстовом движке. Нераскрытые свойства не прячутся, а считаются: пустая
/// карточка не сообщает игроку, сколько ещё выяснять.
/// </summary>
public partial class EncyclopediaScreenUI : DosSplitScreen
{
	protected override string ListCaption => "КАРТОТЕКА";

	protected override string DetailsCaption => "СВЕДЕНИЯ";

	protected override void Subscribe(List<IDisposable> subscriptions)
	{
		GameRuntime? runtime = GetReadyRuntime(this);
		if (runtime == null)
		{
			return;
		}

		IEventBus events = runtime.Session.Events;
		subscriptions.Add(events.Subscribe<CreatureRevealed>(_ => Refresh()));
		subscriptions.Add(events.Subscribe<CreatureIdentified>(_ => Refresh()));
	}

	protected override IReadOnlyList<(string Id, string Text)> GetRows()
	{
		var rows = new List<(string, string)>();
		GameRuntime? runtime = GetReadyRuntime(this);
		if (runtime == null)
		{
			return rows;
		}

		// Ядро отдаёт только опознанных: неопознанного существа в списке быть не
		// может, поэтому название показываем всегда, даже когда свойств ноль.
		foreach (EncyclopediaEntryView entry in runtime.Session.GetEncyclopedia())
		{
			rows.Add((
				entry.CreatureId,
				$"{ResolveName(entry.CreatureId)}  [{entry.RevealedPropertyIds.Count}/{entry.TotalProperties}]"));
		}

		return rows;
	}

	protected override string GetDetails(string creatureId)
	{
		GameRuntime? runtime = GetReadyRuntime(this);
		if (runtime == null)
		{
			return string.Empty;
		}

		foreach (EncyclopediaEntryView entry in runtime.Session.GetEncyclopedia())
		{
			if (entry.CreatureId != creatureId)
			{
				continue;
			}

			var text = new System.Text.StringBuilder();
			text.AppendLine(ContentSpanFormatter.Escape(ResolveName(creatureId).ToUpperInvariant()));
			text.AppendLine();

			// Общее описание идёт первым: оно известно с самого начала, и статья
			// читается как статья — сперва о ком речь, потом что о нём выяснили.
			// Вскрытые свойства — под чертой: они копятся по ходу игры, и статья
			// растёт вниз, не сдвигая уже прочитанное начало.
			string description = ResolveDescription(creatureId);
			if (!string.IsNullOrWhiteSpace(description))
			{
				text.AppendLine(description);
				text.AppendLine();
				text.AppendLine(new string('─', 40));
				text.AppendLine();
			}

			for (int i = 0; i < entry.RevealedPropertyIds.Count; i++)
			{
				text.AppendLine("· " + ResolveProperty(creatureId, entry.RevealedPropertyIds[i]));
				text.AppendLine();
			}

			int hidden = entry.TotalProperties - entry.RevealedPropertyIds.Count;
			if (entry.RevealedPropertyIds.Count == 0)
			{
				text.AppendLine(ContentSpanFormatter.Escape("Сведений нет. Свойства вскрываются по итогам выездов."));
			}
			else if (hidden > 0)
			{
				text.AppendLine(ContentSpanFormatter.Escape($"Не выяснено: {hidden}."));
			}

			return text.ToString();
		}

		return string.Empty;
	}

	protected override string GetSummary()
	{
		GameRuntime? runtime = GetReadyRuntime(this);
		if (runtime == null)
		{
			return "ЯДРО НЕДОСТУПНО";
		}

		int revealed = 0;
		int total = 0;
		int count = 0;
		foreach (EncyclopediaEntryView entry in runtime.Session.GetEncyclopedia())
		{
			revealed += entry.RevealedPropertyIds.Count;
			total += entry.TotalProperties;
			count++;
		}

		return $"ВИДОВ: {count}   ВСКРЫТО: {revealed}/{total}";
	}

	/// <summary>
	/// Общая часть статьи — куски без метки раскрытия. Она известна с начала
	/// игры и не зависит от того, что группа успела выяснить.
	/// </summary>
	private static string ResolveDescription(string creatureId)
	{
		if (Content.Instance == null)
		{
			return string.Empty;
		}

		ContentEntry entry = Content.Instance.GetEntry(creatureId);
		if (entry == null)
		{
			return string.Empty;
		}

		var text = new System.Text.StringBuilder();
		foreach (ContentChunk chunk in entry.Chunks)
		{
			if (chunk.Reveal.Length > 0 || string.IsNullOrWhiteSpace(chunk.Text))
			{
				continue;
			}

			if (text.Length > 0)
			{
				text.AppendLine();
			}

			text.AppendLine(ContentSpanFormatter.ChunkToBbcode(chunk, DosTerminal.Marker));
		}

		return text.ToString().TrimEnd();
	}

	/// <summary>
	/// Свойство — не отдельная запись, а кусок статьи существа с меткой
	/// раскрытия: id свойства лежит в <c>chunk.Reveal</c>.
	/// </summary>
	private static string ResolveProperty(string creatureId, string propertyId)
	{
		if (Content.Instance == null)
		{
			return propertyId;
		}

		ContentEntry entry = Content.Instance.GetEntry(creatureId);
		if (entry != null)
		{
			foreach (ContentChunk chunk in entry.Chunks)
			{
				if (chunk.Reveal.Equals(propertyId, StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrWhiteSpace(chunk.Text))
				{
					return ContentSpanFormatter.ChunkToBbcode(chunk, DosTerminal.Marker);
				}
			}
		}

		return propertyId;
	}

	private static string ResolveName(string entryId)
	{
		if (Content.Instance == null)
		{
			return entryId;
		}

		ContentEntry entry = Content.Instance.GetEntry(entryId);
		return entry != null && !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : entryId;
	}
}
