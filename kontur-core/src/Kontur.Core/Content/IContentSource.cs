using System;
using System.Collections.Generic;
using System.IO;

namespace Kontur.Core.Content
{
	/// <summary>
	/// Абстракция над файловой системой. Ядро не знает, откуда пришёл JSON:
	/// в headless-прогоне это папка на диске, в Godot — res:// через FileAccess.
	/// Именно поэтому здесь нет ни одной ссылки на движок.
	/// </summary>
	public interface IContentSource
	{
		bool Exists(string fileName);

		string ReadAllText(string fileName);
	}

	public sealed class DirectoryContentSource : IContentSource
	{
		private readonly string _rootPath;

		public DirectoryContentSource(string rootPath)
		{
			_rootPath = rootPath;
		}

		public bool Exists(string fileName)
		{
			return File.Exists(Path.Combine(_rootPath, fileName));
		}

		public string ReadAllText(string fileName)
		{
			return File.ReadAllText(Path.Combine(_rootPath, fileName));
		}
	}

	/// <summary>Источник в памяти — удобен для юнит-тестов и быстрых экспериментов с балансом.</summary>
	public sealed class InMemoryContentSource : IContentSource
	{
		private readonly Dictionary<string, string> _files =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public void Add(string fileName, string json)
		{
			_files[fileName] = json;
		}

		public bool Exists(string fileName)
		{
			return _files.ContainsKey(fileName);
		}

		public string ReadAllText(string fileName)
		{
			return _files[fileName];
		}
	}

	public sealed class ContentException : Exception
	{
		public ContentException(string message)
			: base(message)
		{
		}
	}
}
