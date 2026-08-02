using System;
using System.Collections.Generic;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Пак сюжетных флагов партии: имя — установлен или нет, без значений и без типов.
	///
	/// Нужен всему, что не сводится к уже существующим системам: условные абзацы в
	/// текстах (<c>%% reveal: ... %%</c>), поля <c>requirements</c> у записей контента,
	/// последствия выборов, которые должны всплыть через несколько дней. Раскрытия
	/// энциклопедии живут отдельно, в <see cref="EncyclopediaState"/>, потому что там
	/// свои правила (абзац существа, а не свободное имя).
	///
	/// Ядро само флаги не выставляет — их ставит игровой код и UI через
	/// <c>KonturSimulation.SetFlag</c>. Регистр имени не важен.
	/// </summary>
	public sealed class FlagSet
	{
		private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public int Count
		{
			get { return _flags.Count; }
		}

		public IReadOnlyCollection<string> All
		{
			get { return _flags; }
		}

		public bool IsSet(string flag)
		{
			return !string.IsNullOrEmpty(flag) && _flags.Contains(flag);
		}

		/// <summary>Возвращает true, если значение действительно изменилось.</summary>
		public bool Set(string flag, bool value = true)
		{
			if (string.IsNullOrEmpty(flag))
			{
				return false;
			}

			return value ? _flags.Add(flag) : _flags.Remove(flag);
		}

		/// <summary>Переключает флаг и возвращает новое значение.</summary>
		public bool Toggle(string flag)
		{
			if (string.IsNullOrEmpty(flag))
			{
				return false;
			}

			if (_flags.Remove(flag))
			{
				return false;
			}

			_flags.Add(flag);
			return true;
		}

		public void Clear()
		{
			_flags.Clear();
		}
	}
}
