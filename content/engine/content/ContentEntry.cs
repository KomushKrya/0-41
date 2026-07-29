using System.Collections.Generic;

public sealed class ContentEntry
{
	public string Id = string.Empty;
	public string Type = string.Empty;
	public string Name = string.Empty;
	public string Outcome = string.Empty;
	public int Day;
	public IReadOnlyList<string> Requirements = new List<string>();
	public IReadOnlyList<string> Properties = new List<string>();
	public IReadOnlyList<ContentChunk> Chunks = new List<ContentChunk>();
	public IReadOnlyList<ContentOption> Options = new List<ContentOption>();
}

public sealed class ContentChunk
{
	public string Text = string.Empty;
	public string Reveal = string.Empty;
}

public sealed class ContentOption
{
	public string Name = string.Empty;
	public string Canon = string.Empty;
	public int RequirementModifier;
	public IReadOnlyList<ContentChunk> Chunks = new List<ContentChunk>();
}
