namespace Kontur.Core.Model
{
	/// <summary>
	/// Конечный автомат одного вызова — прямое отражение раздела 3 ДД.
	/// Вызовы могут накладываться, поэтому фаза хранится на каждом инциденте отдельно.
	/// </summary>
	public enum IncidentPhase
	{
		/// <summary>Запланирован планировщиком, но ещё не создан.</summary>
		Scheduled = 0,

		/// <summary>Телефон звонит, 15 секунд на ответ.</summary>
		Ringing = 1,

		/// <summary>Трубка снята, на экране описание задания, ждём кнопку ОК.</summary>
		Briefing = 2,

		/// <summary>Метка на карте, 30 секунд на отправку группы.</summary>
		MarkerActive = 3,

		/// <summary>Группа в пути — пунктирная линия на карте.</summary>
		Travelling = 4,

		/// <summary>Группа на объекте, работает.</summary>
		OnSite = 5,

		/// <summary>Радио трещит, 20 секунд на реакцию.</summary>
		RadioPending = 6,

		/// <summary>Итог посчитан, группа возвращается.</summary>
		Returning = 7,

		/// <summary>Отчёт доступен на компьютере, вызов закрыт.</summary>
		Closed = 8
	}
}
