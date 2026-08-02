using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Content
{
	/// <summary>
	/// Порт в текстовый движок. Ядру проза не нужна — ни для баланса, ни для событий:
	/// оно рассылает id, а разворачивает их в текст интерфейс. Единственное, что ядру
	/// полезно знать, — существует ли то, на что ссылаются геймплейные данные, чтобы
	/// опечатка в id падала при загрузке, а не оборачивалась пустым экраном на смене.
	///
	/// Плюс одно исключение: у вариантов решения на выезде числовая часть живёт
	/// в тексте (`requirement_modifier`, `quality`, `requires`), потому что автор правит её вместе
	/// с формулировкой варианта. Каталог отдаёт эти числа — но не сами формулировки.
	///
	/// Реализация живёт на стороне движка (GodotTextCatalog) либо читает собранный
	/// JSON напрямую (JsonTextCatalog) — так ядро остаётся запускаемым без Godot.
	/// Если каталог не передан, ContentLoader эти проверки пропускает.
	/// </summary>
	public interface ITextCatalog
	{
		/// <summary>Есть ли запись контента с таким id.</summary>
		bool HasEntry(string entryId);

		/// <summary>Есть ли внутри записи условный блок под это свойство (%% reveal %%).</summary>
		bool HasProperty(string entryId, string propertyId);

		/// <summary>
		/// Варианты решения у записи типа mission_event, в порядке файла.
		/// Пустой список, если записи нет или вариантов у неё нет.
		/// </summary>
		IReadOnlyList<TextOption> GetOptions(string entryId);
	}

	/// <summary>
	/// Вариант решения глазами ядра: ключ, тип и числа, без единого слова прозы.
	/// Формулировку по этому же ключу возьмёт интерфейс.
	/// </summary>
	public sealed class TextOption
	{
		public TextOption(
			string id,
			MissionEventQuality quality,
			int? requirementModifier,
			IReadOnlyList<StatKind> checkedStats)
		{
			Id = id;
			Quality = quality;
			RequirementModifier = requirementModifier;
			CheckedStats = checkedStats ?? new List<StatKind>();
		}

		public string Id { get; }

		/// <summary>Хороший, нейтральный или плохой. Задаёт умолчания и проверяется на сборке.</summary>
		public MissionEventQuality Quality { get; }

		/// <summary>
		/// Надбавка к сложности: +N к каждой требуемой характеристике миссии.
		/// null — автор не писал число, берётся умолчание по типу диалога.
		/// </summary>
		public int? RequirementModifier { get; }

		/// <summary>
		/// Характеристики, по которым идёт проверка этого варианта. Чисел текст не несёт:
		/// порог по каждой — требование самой миссии, оно подставляется при расчёте.
		/// Пустой список — проверять нечего, исход варианта предрешён.
		/// </summary>
		public IReadOnlyList<StatKind> CheckedStats { get; }
	}
}
