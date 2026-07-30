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

	/// <summary>Имена подстановок {{имя}}, встреченные в тексте: что игра должна заполнить.</summary>
	public IReadOnlyList<string> Variables = new List<string>();

	public IReadOnlyList<ContentChunk> Chunks = new List<ContentChunk>();
	public IReadOnlyList<ContentOption> Options = new List<ContentOption>();
}

public sealed class ContentChunk
{
	/// <summary>Обычная реплика или абзац.</summary>
	public const string KindText = "text";

	/// <summary>Служебная шапка звонка — [ЗВОНОК ПЕРЕНАПРАВЛЕН ...], рендерится отдельно.</summary>
	public const string KindCallMeta = "call_meta";

	public string Text = string.Empty;
	public string Kind = KindText;
	public string Reveal = string.Empty;

	public bool IsCallMeta => Kind == KindCallMeta;
}

public sealed class ContentOption
{
	public string Name = string.Empty;
	public string Canon = string.Empty;
	public int RequirementModifier;
	public IReadOnlyList<ContentChunk> Chunks = new List<ContentChunk>();
}
