using System.Collections.Generic;
using Godot;

/// <summary>
/// Единое владение курсором мыши.
///
/// До этого режим курсора выставляли восемь разных мест, и четыре из них ещё и
/// запоминали «предыдущее» значение, чтобы вернуть его при закрытии. Пока экраны
/// открывались по одному, схема работала. Стоило им наложиться — рация поверх
/// телефона, пауза поверх найма — и восстанавливалось уже несвежее значение.
/// А экраны найма и катсцен курсор не трогали вовсе, поэтому уносили с собой
/// захват, поставленный игроком в кабинете: мышь была жива, но невидима.
///
/// Здесь тот же приём, что у заморозки времени в ядре: не переключатель, а набор
/// держателей. Курсор виден, пока его просит хоть кто-то, и захватывается, когда
/// не просит никто. Кто именно отпустил последним — не важно, порядок закрытия
/// экранов перестаёт что-либо значить.
///
/// Держатели — узлы, и это не для красоты: узел, ушедший из дерева вместе со
/// сценой, отпускает курсор сам. Забытый вызов <see cref="Hide"/> перестаёт быть
/// ошибкой, из-за которой мышь пропадает до перезапуска.
/// </summary>
public static class CursorMode
{
	private static readonly List<Node> Holders = new();

	/// <summary>Просит показать курсор. Повторный вызов от того же узла безвреден.</summary>
	public static void Show(Node owner)
	{
		if (owner == null)
		{
			return;
		}

		if (!Holders.Contains(owner))
		{
			Holders.Add(owner);
		}

		Apply();
	}

	/// <summary>Снимает просьбу. Курсор вернётся в захват, только если больше никто не просит.</summary>
	public static void Hide(Node owner)
	{
		if (owner != null)
		{
			Holders.Remove(owner);
		}

		Apply();
	}

	/// <summary>
	/// Пересчитывает режим по живым держателям.
	///
	/// Зовётся и снаружи — теми, кто раньше выставлял захват напрямую: игроком
	/// при возврате в кресло и осмотром предмета. Им нужно не «захватить любой
	/// ценой», а «вернуть то, что положено сейчас».
	/// </summary>
	public static void Apply()
	{
		// Узлы, ушедшие вместе со сценой, держателями быть перестали.
		for (int i = Holders.Count - 1; i >= 0; i--)
		{
			Node holder = Holders[i];
			if (!GodotObject.IsInstanceValid(holder) || !holder.IsInsideTree())
			{
				Holders.RemoveAt(i);
			}
		}

		Input.MouseMode = Holders.Count > 0
			? Input.MouseModeEnum.Visible
			: Input.MouseModeEnum.Captured;
	}

	/// <summary>Кто сейчас держит курсор. Для отладочного оверлея.</summary>
	public static string DescribeHolders()
	{
		Apply();

		if (Holders.Count == 0)
		{
			return "курсор захвачен";
		}

		var names = new List<string>();
		for (int i = 0; i < Holders.Count; i++)
		{
			names.Add(Holders[i].Name);
		}

		return "курсор держат: " + string.Join(", ", names);
	}
}
