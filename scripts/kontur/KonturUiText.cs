using System.Text;
using Kontur.Core.Api;

/// <summary>
/// Resolves the text ids in core snapshots through the Godot content catalogue.
/// Narrative text stays outside of Kontur.Core by design.
/// </summary>
public static class KonturUiText
{
	public static string MissionTitle(IncidentView incident)
	{
		ContentEntry entry = FindEntry(incident.MissionId);
		return entry != null && !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : incident.MissionId;
	}

	public static string CallerName(IncidentView incident)
	{
		ContentEntry entry = FindEntry(incident.CallId);
		return entry != null && !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : incident.CallId;
	}

	public static string CallText(IncidentView incident)
	{
		ContentEntry entry = FindEntry(incident.CallId);
		if (entry == null || entry.Chunks.Count == 0)
		{
			return string.Empty;
		}

		var text = new StringBuilder();
		foreach (ContentChunk chunk in entry.Chunks)
		{
			if (string.IsNullOrWhiteSpace(chunk.Text))
			{
				continue;
			}

			if (text.Length > 0)
			{
				text.Append(' ');
			}

			text.Append(chunk.Text);
		}

		return text.ToString();
	}

	private static ContentEntry FindEntry(string entryId)
	{
		Content content = Content.Instance;
		return content != null && content.TryGetEntry(entryId, out ContentEntry entry) ? entry : null;
	}
}
