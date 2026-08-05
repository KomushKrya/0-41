using System;
using Godot;
using Kontur.Core.Config;
using Kontur.Core.Events;

/// <summary>
/// Записка сменщика на столе: флейвор-текст от дневной смены (ДД, раздел 2).
///
/// Своего текста у виджета нет. Что показывать, решает ядро: id записки лежит в
/// <c>shiftNoteId</c> текущего дня в config.json и приходит сигналом
/// <see cref="ShiftStarted"/>, а сама проза — записью <c>shift_note</c> в
/// content/raw. Поэтому на каждый игровой день записка своя, а строк в сцене нет.
/// </summary>
public partial class ShiftNoteUI : ContentTextBox
{
	[Export] public NodePath BodyPath { get; set; } = new("Body");

	/// <summary>Что показать, пока ядро не подняло смену: первый день.</summary>
	[Export] public string FallbackShiftNoteId { get; set; } = "shift_note_day_1";

	/// <summary>Кегль записки: подбирается под длину текста, чтобы влезть на страницу.</summary>
	[Export(PropertyHint.Range, "8,80,1")] public int MaxFontSize { get; set; } = 34;

	[Export(PropertyHint.Range, "8,80,1")] public int MinFontSize { get; set; } = 18;

	private Label _body = null!;

	private IDisposable _shiftStartedSubscription;

	public override void _Ready()
	{
		_body = GetNode<Label>(BodyPath);

		// Текст приходит по id смены, а не из ContentId в сцене: базовый _Ready
		// открыл бы пустую строку и заругался в Output.
		OpenOnReady = false;
		base._Ready();

		OpenCurrentShiftNote();

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady)
		{
			_shiftStartedSubscription = runtime.Session.Events.Subscribe<ShiftStarted>(e => Open(e.ShiftNoteId));
		}
	}

	public override void _ExitTree()
	{
		_shiftStartedSubscription?.Dispose();
		_shiftStartedSubscription = null;
		base._ExitTree();
	}

	/// <summary>
	/// Перечитать записку текущего дня снимком. Нужно на старте: смена могла
	/// начаться до того, как предмет попал в дерево, и сигнала уже не будет.
	/// </summary>
	public void OpenCurrentShiftNote()
	{
		Open(CurrentShiftNoteId());
	}

	protected override void Render()
	{
		var text = new System.Text.StringBuilder();
		foreach (ContentChunk chunk in Chunks)
		{
			if (text.Length > 0)
			{
				text.AppendLine().AppendLine();
			}

			text.Append(chunk.Text);
		}

		_body.Text = text.ToString();

		// Размеры узла на первом кадре ещё не посчитаны, да и записка меняется в
		// рантайме, поэтому кегль подбираем отложенно — по фактической странице.
		CallDeferred(nameof(FitBodyFontSize));
	}

	/// <summary>
	/// Подбирает кегль под длину записки: у каждого дня свой текст, а страница одна.
	/// Меряем самой меткой (сколько строк влезло из скольких) — так в счёт идут и
	/// перенос по словам, и межстрочный интервал. Ниже <see cref="MinFontSize"/> не
	/// опускаемся: лучше подрезать хвост, чем выдать нечитаемый мелкий почерк.
	/// </summary>
	private void FitBodyFontSize()
	{
		if (_body.Text.Length == 0 || _body.Size.Y <= 0.0f)
		{
			return;
		}

		for (int candidate = MaxFontSize; candidate >= MinFontSize; candidate--)
		{
			ApplyBodyFontSize(candidate);
			if (candidate == MinFontSize || _body.GetVisibleLineCount() >= _body.GetLineCount())
			{
				return;
			}
		}
	}

	/// <summary>
	/// У рукописного шрифта высокие выносные элементы, и строки по умолчанию стоят
	/// слишком редко. Поджимаем интервал пропорционально кеглю, иначе на мелком
	/// тексте абзацы разъезжаются так же, как на крупном.
	/// </summary>
	private void ApplyBodyFontSize(int size)
	{
		_body.AddThemeFontSizeOverride("font_size", size);
		_body.AddThemeConstantOverride("line_spacing", Mathf.RoundToInt(-0.35f * size));
	}

	private string CurrentShiftNoteId()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return FallbackShiftNoteId;
		}

		DayConfig day = runtime.Session.Config.GetDay(Math.Max(1, runtime.Session.Day));
		return day.ShiftNoteId.Length > 0 ? day.ShiftNoteId : FallbackShiftNoteId;
	}
}
