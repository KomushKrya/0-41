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
	}

	public sealed class TextOption
	{
		public TextOption(string id, MissionEventQuality quality, int? requirementModifier, IReadOnlyList<StatKind> checkedStats)
		{
			Id = id;
			Quality = quality;
			RequirementModifier = requirementModifier;
			CheckedStats = checkedStats ?? new List<StatKind>();
		}
		public string Id { get; }
		public MissionEventQuality Quality { get; }
		public int? RequirementModifier { get; }
		public IReadOnlyList<StatKind> CheckedStats { get; }
	}
}
