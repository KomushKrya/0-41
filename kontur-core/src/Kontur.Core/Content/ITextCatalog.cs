using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Content
{
	/// <summary>Порт текстового движка: ядро хранит только id и числа вариантов.</summary>
	public interface ITextCatalog
	{
		bool HasEntry(string entryId);
		bool HasProperty(string entryId, string propertyId);
		IReadOnlyList<TextOption> GetOptions(string entryId);
		IReadOnlyList<string> GetBioLines(string slot);

		/// <summary>Флаги, без которых вызов не должен попасть в расписание смены.</summary>
		IReadOnlyList<string> GetRequirements(string entryId);
	}

	/// <summary>
	/// Вариант решения на выезде так, как его видит ядро: id для связи с балансом и
	/// список характеристик, по которым идёт проверка. Ничего про «хороший/плохой»
	/// вариант тут нет и быть не должно: строгость варианта — это и есть его набор
	/// характеристик, а цена и риск живут в data/radio.json.
	/// </summary>
	public sealed class TextOption
	{
		public TextOption(string id, IReadOnlyList<StatKind> checkedStats)
		{
			Id = id;
			CheckedStats = checkedStats ?? new List<StatKind>();
		}
		public string Id { get; }
		public IReadOnlyList<StatKind> CheckedStats { get; }
	}
}
