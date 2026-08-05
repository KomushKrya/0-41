#nullable enable

using Godot;

/// <summary>
/// Общее оформление терминала: чёрно-зелёный монитор в духе DOS.
///
/// Палитра и фабрики собраны в одном месте, потому что экранов три и каждый
/// строит свои строки сам — иначе оттенки разъезжаются при первой же правке.
/// Шрифт берётся системный, а не кладётся в репозиторий: моноширинный нужен
/// только ради ровных колонок, и тащить ради этого бинарник незачем.
/// </summary>
public static class DosTerminal
{
	public static readonly Color Background = new(0.02f, 0.05f, 0.03f);
	public static readonly Color Text = new(0.42f, 0.90f, 0.48f);
	public static readonly Color TextBright = new(0.78f, 1.00f, 0.78f);
	public static readonly Color TextDim = new(0.26f, 0.52f, 0.32f);
	public static readonly Color Border = new(0.30f, 0.66f, 0.36f);

	/// <summary>Выделение инвертирует цвета, как это делал текстовый режим.</summary>
	public static readonly Color HighlightBackground = new(0.38f, 0.85f, 0.45f);
	public static readonly Color HighlightText = new(0.02f, 0.06f, 0.03f);

	/// <summary>
	/// Слова-маркеры из текстового движка (==слово== в исходнике).
	///
	/// Янтарный, а не просто зелень поярче: на монохромном экране разница в
	/// яркости читается как случайная, а второй люминофор — как намеренная метка.
	/// </summary>
	public static readonly Color Marker = new(1.00f, 0.78f, 0.36f);

	public const int FontSize = 16;
	public const int TitleFontSize = 18;

	/// <summary>Fixedsys — консольный шрифт, тот самый растровый вид текстового режима.</summary>
	public const string FontPath = "res://assets/fonts/ofont.ru_Fixedsys.ttf";

	/// <summary>Подложка экрана: развёртка и виньетка вместо ровной заливки.</summary>
	public const string BackgroundTexturePath = "res://assets/textures/фоновый слой ПК.png";

	private static Font? _font;

	public static Font GetFont()
	{
		if (_font != null)
		{
			return _font;
		}

		_font = ResourceLoader.Load<Font>(FontPath);
		if (_font == null)
		{
			// Терминал не должен превращаться в пропорциональный текст, если шрифт
			// не доехал: колонки в таблицах набраны пробелами и разъедутся.
			GD.PushError($"{nameof(DosTerminal)}: шрифт {FontPath} не найден, беру системный моноширинный.");
			var fallback = new SystemFont();
			fallback.FontNames = new[] { "Consolas", "Courier New", "DejaVu Sans Mono", "monospace" };
			_font = fallback;
		}

		return _font;
	}

	/// <summary>
	/// Подложка экрана во всю площадь текстуры вьюпорта.
	///
	/// Пропорции сохраняются, лишнее обрезается по бокам. Растяжение по ширине
	/// сжимало снимок по вертикали вдвое, шаг развёртки становился меньше
	/// половины пикселя и на модели монитора шёл муаром. Если текстуры нет,
	/// остаётся ровная тёмная заливка.
	/// </summary>
	public static Control CreateBackground()
	{
		var texture = ResourceLoader.Load<Texture2D>(BackgroundTexturePath);
		if (texture == null)
		{
			GD.PushWarning($"{nameof(DosTerminal)}: подложка {BackgroundTexturePath} не найдена.");
			var fill = new ColorRect { Color = Background, MouseFilter = Control.MouseFilterEnum.Ignore };
			fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			return fill;
		}

		var background = new TextureRect
		{
			Texture = texture,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return background;
	}

	/// <summary>Тема на корень терминала: дальше всё наследуется само.</summary>
	public static Theme CreateTheme()
	{
		var theme = new Theme
		{
			DefaultFont = GetFont(),
			DefaultFontSize = FontSize
		};

		theme.SetColor("font_color", "Label", Text);

		theme.SetColor("font_color", "Button", Text);
		theme.SetColor("font_hover_color", "Button", TextBright);
		theme.SetColor("font_pressed_color", "Button", TextBright);
		theme.SetColor("font_focus_color", "Button", Text);
		theme.SetColor("font_disabled_color", "Button", TextDim);
		theme.SetStylebox("normal", "Button", CreateFlatStyle(Colors.Transparent));
		theme.SetStylebox("hover", "Button", CreateFlatStyle(new Color(0.10f, 0.22f, 0.13f)));
		theme.SetStylebox("pressed", "Button", CreateFlatStyle(new Color(0.16f, 0.34f, 0.20f)));
		theme.SetStylebox("focus", "Button", CreateFlatStyle(Colors.Transparent));
		theme.SetStylebox("disabled", "Button", CreateFlatStyle(Colors.Transparent));

		theme.SetColor("default_color", "RichTextLabel", Text);
		theme.SetFont("normal_font", "RichTextLabel", GetFont());
		theme.SetFontSize("normal_font_size", "RichTextLabel", FontSize);
		theme.SetStylebox("normal", "RichTextLabel", CreateFlatStyle(Colors.Transparent));

		theme.SetStylebox("panel", "PanelContainer", CreateFrameStyle());
		ApplyScrollBarStyle(theme, "VScrollBar");
		ApplyScrollBarStyle(theme, "HScrollBar");
		return theme;
	}

	/// <summary>
	/// Полоса прокрутки под остальной терминал: тёмный жёлоб и зелёный ползунок.
	///
	/// Штатная полоса Godot приходит серой со скруглениями и на чёрно-зелёном
	/// экране читается как чужой элемент — единственное место, где видно, что
	/// это не текстовый режим, а движок с темой по умолчанию.
	/// </summary>
	private static void ApplyScrollBarStyle(Theme theme, string type)
	{
		theme.SetStylebox("scroll", type, CreateScrollTrack());
		theme.SetStylebox("scroll_focus", type, CreateScrollTrack());
		theme.SetStylebox("grabber", type, CreateScrollGrabber(Border));
		theme.SetStylebox("grabber_highlight", type, CreateScrollGrabber(Text));
		theme.SetStylebox("grabber_pressed", type, CreateScrollGrabber(TextBright));
	}

	private static StyleBoxFlat CreateScrollTrack()
	{
		// Ширина полосы задаётся полями жёлоба: отдельной константы у ScrollBar нет.
		// Жёлоб полупрозрачный — сквозь него видна развёртка подложки.
		return new StyleBoxFlat
		{
			BgColor = new Color(0.06f, 0.13f, 0.08f, 0.55f),
			ContentMarginLeft = 5.0f,
			ContentMarginRight = 5.0f,
			ContentMarginTop = 5.0f,
			ContentMarginBottom = 5.0f
		};
	}

	private static StyleBoxFlat CreateScrollGrabber(Color color)
	{
		return new StyleBoxFlat { BgColor = color };
	}

	public static StyleBoxFlat CreateFlatStyle(Color color)
	{
		return new StyleBoxFlat
		{
			BgColor = color,
			ContentMarginLeft = 6.0f,
			ContentMarginRight = 6.0f,
			ContentMarginTop = 1.0f,
			ContentMarginBottom = 1.0f
		};
	}

	/// <summary>
	/// Рамка панели. Ровные линии, а не псевдографика из символов: рамку из
	/// «═╔╗» пришлось бы держать выровненной вручную и она поехала бы на первом
	/// же шрифте без нужных глифов.
	/// </summary>
	public static StyleBoxFlat CreateFrameStyle()
	{
		var style = new StyleBoxFlat
		{
			// Прозрачная заливка: под рамками лежит текстура экрана, и сплошной
			// фон вырезал бы из неё прямоугольники.
			BgColor = Colors.Transparent,
			BorderColor = Border,
			ContentMarginLeft = 10.0f,
			ContentMarginRight = 10.0f,
			ContentMarginTop = 6.0f,
			ContentMarginBottom = 6.0f
		};

		style.SetBorderWidthAll(2);
		return style;
	}

	/// <summary>Панель с заголовком-подписью и разделителем под ним.</summary>
	public static VBoxContainer CreateFramedColumn(string caption, out PanelContainer frame)
	{
		return CreateFramedColumn(caption, out frame, out _);
	}

	/// <param name="captionLabel">Ярлык подписи — нужен тем, у кого она меняется по ходу.</param>
	public static VBoxContainer CreateFramedColumn(string caption, out PanelContainer frame, out Label captionLabel)
	{
		frame = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 4);
		frame.AddChild(column);

		captionLabel = CreateCaption(caption);
		if (!string.IsNullOrEmpty(caption))
		{
			column.AddChild(captionLabel);
			column.AddChild(CreateSeparator());
		}

		return column;
	}

	public static Label CreateCaption(string text)
	{
		var caption = new Label { Text = text };
		caption.AddThemeColorOverride("font_color", TextBright);
		caption.AddThemeFontSizeOverride("font_size", TitleFontSize);
		return caption;
	}

	public static Control CreateSeparator()
	{
		return new ColorRect
		{
			Color = Border,
			CustomMinimumSize = new Vector2(0.0f, 1.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
	}

	public static Label CreateLine(string text, Color? color = null)
	{
		var label = new Label { Text = text };
		label.AddThemeColorOverride("font_color", color ?? Text);
		return label;
	}

	/// <summary>Строка списка: обычная кнопка, которая при выборе инвертируется.</summary>
	public static Button CreateRow(string text)
	{
		var row = new Button
		{
			Text = text,
			Alignment = HorizontalAlignment.Left,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			ClipText = true
		};

		return row;
	}

	public static void SetRowSelected(Button row, bool isSelected)
	{
		if (isSelected)
		{
			row.AddThemeStyleboxOverride("normal", CreateFlatStyle(HighlightBackground));
			row.AddThemeStyleboxOverride("hover", CreateFlatStyle(HighlightBackground));
			row.AddThemeColorOverride("font_color", HighlightText);
			row.AddThemeColorOverride("font_hover_color", HighlightText);
			return;
		}

		row.RemoveThemeStyleboxOverride("normal");
		row.RemoveThemeStyleboxOverride("hover");
		row.RemoveThemeColorOverride("font_color");
		row.RemoveThemeColorOverride("font_hover_color");
	}

	/// <summary>
	/// Дополняет строку до нужной ширины. Колонки набираются пробелами, как в
	/// текстовом режиме, — поэтому шрифт и обязан быть моноширинным.
	/// </summary>
	public static string Column(string text, int width)
	{
		text ??= string.Empty;
		if (text.Length < width)
		{
			return text.PadRight(width);
		}

		// Обрезаем на символ раньше: иначе длинное название вплотную упирается
		// в соседнюю колонку и склеивается с ней в одно слово.
		return text.Substring(0, width - 1) + " ";
	}
}
