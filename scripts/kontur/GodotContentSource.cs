using Kontur.Core.Content;

/// <summary>
/// Источник JSON-контента поверх res://.
/// System.IO для res:// не годится: в экспортированной сборке ресурсы лежат в .pck,
/// а не на диске, и путь res:// не является путём файловой системы.
/// </summary>
public sealed class GodotContentSource : IContentSource
{
	private readonly string _root;

	public GodotContentSource(string root)
	{
		_root = root.EndsWith("/") ? root : root + "/";
	}

	public bool Exists(string fileName)
	{
		return Godot.FileAccess.FileExists(_root + fileName);
	}

	public string ReadAllText(string fileName)
	{
		string path = _root + fileName;

		using Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
		{
			throw new ContentException($"Не удалось открыть '{path}': {Godot.FileAccess.GetOpenError()}");
		}

		return file.GetAsText();
	}
}
