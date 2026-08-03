namespace Kontur.Core.Model
{
	public sealed class BuildingDefinition
	{
		public string Id { get; set; } = string.Empty;

		public bool IsDispatchTarget { get; set; }

		public bool IsHeadquarters { get; set; }
	}
}
