/// <summary>
/// Пути ко всем звукам проекта. Один список на игру: файл переименовали — правка здесь,
/// а не в нескольких сценах. Имена констант совпадают с именами файлов в <c>sound/</c>.
///
/// Константы с пометкой «ждёт механики» уже лежат в проекте, но вызывать их пока неоткуда:
/// у предмета нет своего взаимодействия. Когда оно появится — хватит одной строки.
/// </summary>
public static class Sfx
{
	/// <summary>Фон смены. Порядок перемешивается, см. AudioManager.</summary>
	public static readonly string[] ShiftAmbient =
	{
		"res://sound/ambient/ambient1.mp3",
		"res://sound/ambient/ambient_2.mp3",
		"res://sound/ambient/ambient_3.mp3",
		"res://sound/ambient/ambient_4.mp3"
	};

	/// <summary>
	/// Между сменами: у каждого перехода своя мелодия. Когда она доиграла, а игрок
	/// всё ещё на экране найма — включается <see cref="BetweenShiftsFull"/>.
	/// </summary>
	public static string BetweenShiftsFor(int nextDay)
	{
		switch (nextDay)
		{
			case 2: return "res://sound/ambient/between_shifts_12.mp3";
			case 3: return "res://sound/ambient/between_shifts_23.mp3";
			case 4: return "res://sound/ambient/between_shifts_34.mp3";
			default: return BetweenShiftsFull;
		}
	}

	/// <summary>Длинная версия: играет по кругу, пока игрок сидит на экране найма.</summary>
	public const string BetweenShiftsFull = "res://sound/ambient/between_shifts_full.mp3";

	/// <summary>
	/// Фон главного меню. Пока это тот же длинный трек, что и между сменами: он
	/// единственный написан как петля и не привязан к конкретной смене, а фон
	/// смены в меню звучал бы так, будто игра уже началась. Появится своя тема —
	/// правится эта строка, и больше ничего.
	/// </summary>
	public const string MainMenu = BetweenShiftsFull;

	public const string PhoneRing = "res://sound/events/phone_ring.mp3";
	public const string PhoneTake = "res://sound/events/phone_take.mp3";
	public const string PhonePut = "res://sound/events/phone_put.mp3";

	/// <summary>Шум рации в момент, когда она стала доступна.</summary>
	public const string Radio = "res://sound/events/radio.mp3";

	/// <summary>
	/// Заход в меню выбора по рации. Сейчас не используется: эту роль занял
	/// <see cref="RadioAnswer"/>, а вместе они звучали одновременно и глушили друг друга.
	/// </summary>
	public const string ChoiceAmbient = "res://sound/events/choice_ambient.mp3";

	/// <summary>Ответ по рации: звучит, когда игрок берёт её в руки. Берётся случайный из трёх.</summary>
	public static readonly string[] RadioAnswer =
	{
		"res://sound/events/radio_answer_1.mp3",
		"res://sound/events/radio_answer_2.mp3",
		"res://sound/events/radio_answer_3.mp3"
	};

	/// <summary>Нажатие на вариант ответа по рации.</summary>
	public const string ChoicePress = "res://sound/events/choice_press.mp3";

	/// <summary>Гудение машины, пока игрок за экраном компьютера. Зациклено вручную.</summary>
	public const string ComputerWorking = "res://sound/events/computer_working.mp3";

	/// <summary>Нажатие любой кнопки на экране компьютера.</summary>
	public const string KeyboardEnter = "res://sound/events/keyboard_enter.mp3";

	/// <summary>
	/// Набор «Фамилия И.О.»: звучит, когда игрок жмёт на фотографию сотрудника в папке —
	/// как будто фамилию печатают на экране. Берётся случайный из трёх.
	/// </summary>
	public static readonly string[] KeyboardTyping =
	{
		"res://sound/events/keyboard_sound_1.mp3",
		"res://sound/events/keyboard_sound_2.mp3",
		"res://sound/events/keyboard_sound_3.mp3"
	};

	/// <summary>Папку открыли.</summary>
	public const string DocumentOpen = "res://sound/events/document_open.mp3";

	/// <summary>Папку положили на стол.</summary>
	public const string DocumentDrop = "res://sound/events/document_drop.mp3";

	/// <summary>Перелистнули страницу в папке.</summary>
	public const string DocumentTurnPage = "res://sound/events/document_turn_page.mp3";

	/// <summary>Щелчок лампы. Ждёт механики: лампа сейчас в группе NotInteractable.</summary>
	public const string Lamp = "res://sound/events/lamp.mp3";

	public const string NoteTake = "res://sound/events/note_take.mp3";
	public const string NotePut = "res://sound/events/note_put.mp3";
	public const string NotepadTake = "res://sound/events/notepad_take.mp3";

	/// <summary>Скрип карандаша: поверх взятия блокнота, если шкалы менялись с прошлого раза.</summary>
	public const string PencilWrite = "res://sound/events/pencil_write.mp3";
}
