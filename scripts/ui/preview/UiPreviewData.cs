using Godot;

/// <summary>
/// Editor-only data used to populate a UI scene without starting GameSession.
/// One .tres resource represents one UI preview.
/// </summary>
[Tool]
[GlobalClass]
public partial class UiPreviewData : Resource
{
	private string _title = string.Empty;
	private string _subtitle = string.Empty;
	private string _primaryName = string.Empty;
	private string _primaryDetails = string.Empty;
	private string _description = string.Empty;
	private string _parameters = string.Empty;
	private string _listItems = string.Empty;
	private string _status = string.Empty;

	[Export] public string Title { get => _title; set => SetPreviewText(ref _title, value); }
	[Export] public string Subtitle { get => _subtitle; set => SetPreviewText(ref _subtitle, value); }
	[Export] public string PrimaryName { get => _primaryName; set => SetPreviewText(ref _primaryName, value); }
	[Export(PropertyHint.MultilineText)] public string PrimaryDetails { get => _primaryDetails; set => SetPreviewText(ref _primaryDetails, value); }
	[Export(PropertyHint.MultilineText)] public string Description { get => _description; set => SetPreviewText(ref _description, value); }
	[Export(PropertyHint.MultilineText)] public string Parameters { get => _parameters; set => SetPreviewText(ref _parameters, value); }
	[Export(PropertyHint.MultilineText)] public string ListItems { get => _listItems; set => SetPreviewText(ref _listItems, value); }
	[Export(PropertyHint.MultilineText)] public string Status { get => _status; set => SetPreviewText(ref _status, value); }

	public string[] GetListItems()
	{
		return ListItems.Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
	}

	private void SetPreviewText(ref string field, string value)
	{
		value ??= string.Empty;
		if (field == value)
		{
			return;
		}

		field = value;
		EmitChanged();
	}
}
