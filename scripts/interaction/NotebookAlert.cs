using Godot;

/// <summary>
/// Подъём блокнота к лицу, когда по шкалам прилетел штраф.
///
/// Живёт отдельным файлом намеренно: это единственное место во всём проекте,
/// где игра сама берёт управление камерой. Если приём не приживётся, удаляется
/// вызов <see cref="RaiseNotebook"/> из NotebookScalesUI и этот файл — больше
/// ничего трогать не придётся.
///
/// Своего состояния нет и узлом это не является: класс только находит нужные
/// объекты в дереве и дёргает уже существующий механизм осмотра предметов.
/// </summary>
public static class NotebookAlert
{
	/// <summary>
	/// Пытается поднести блокнот к камере. Молча ничего не делает, если момент
	/// неподходящий, — навязчивость здесь хуже пропущенного уведомления.
	/// </summary>
	/// <param name="notebookUi">Экран блокнота: от него ищем и предмет, и игрока.</param>
	public static void RaiseNotebook(Node notebookUi)
	{
		if (notebookUi == null || !notebookUi.IsInsideTree())
		{
			return;
		}

		// Пауза — состояние, в которое игрок вошёл сам. Вырывать его оттуда
		// нельзя: меню осталось бы открытым поверх поднятого блокнота.
		if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen)
		{
			return;
		}

		InspectableItemController item = FindItemAbove(notebookUi);
		if (item == null || item.IsViewActive)
		{
			return;
		}

		FlyPlayer player = FindPlayer(notebookUi.GetTree()?.Root);
		if (player == null)
		{
			return;
		}

		// Игрок уже во что-то смотрит — в телефон, в досье, в компьютер. Второй
		// предмет перед лицом не поместится, а прервать чтение звонка штрафом
		// значило бы наказать дважды за одно.
		if (player.IsViewFocused || player.IsCameraTransitioning)
		{
			return;
		}

		// Отложенно: сигнал приходит из тика ядра, а OpenView ставит симуляцию
		// на паузу и запускает твин. Менять и то и другое посреди тика — способ
		// получить ошибку, которую потом ищут неделю.
		//
		// Через Callable.From, а не по имени метода: имя резолвится в рантайме и
		// молча промахнётся, если метод не попал в генерируемые привязки.
		Callable.From(() => item.OpenView(player)).CallDeferred();
	}

	/// <summary>Ищет предмет-носитель вверх по дереву: экран лежит внутри SubViewport'а блокнота.</summary>
	private static InspectableItemController FindItemAbove(Node from)
	{
		for (Node node = from; node != null; node = node.GetParent())
		{
			if (node is InspectableItemController item)
			{
				return item;
			}
		}

		return null;
	}

	/// <summary>
	/// Ищет игрока обходом дерева.
	///
	/// Группа или автозагрузка были бы дешевле, но требуют правки чужого
	/// FlyPlayer или списка автозагрузок. Обход стоит доли миллисекунды и
	/// случается только в момент штрафа, поэтому цена приемлемая.
	/// </summary>
	private static FlyPlayer FindPlayer(Node root)
	{
		if (root == null)
		{
			return null;
		}

		if (root is FlyPlayer player)
		{
			return player;
		}

		foreach (Node child in root.GetChildren())
		{
			FlyPlayer found = FindPlayer(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}
}
