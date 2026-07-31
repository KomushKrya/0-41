// ЭСКИЗ ДЛЯ ИНТЕГРАЦИИ. В Godot-проект пока не копируется — см. docs/INTEGRATION.md.
//
// Кладётся в scripts/kontur/GodotContentSource.cs основного проекта после мержа ветки.

using Godot;
using Kontur.Core.Content;

namespace Kontur.Integration
{
	/// <summary>
	/// Источник контента поверх res://. System.IO для res:// не годится:
	/// в экспортированной сборке ресурсы лежат в pck, а не на диске.
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
			using Godot.FileAccess file = Godot.FileAccess.Open(_root + fileName, Godot.FileAccess.ModeFlags.Read);
			if (file == null)
			{
				throw new ContentException($"Не удалось открыть {_root}{fileName}: {Godot.FileAccess.GetOpenError()}");
			}

			return file.GetAsText();
		}
	}
}
