using System.Collections.Generic;
using Godot;

/// <summary>
/// Находит кадр миссии по её id и виду сцены.
///
/// Раскладка на диске: illustrations/missions/day_&lt;день&gt;/&lt;id миссии&gt;/&lt;вид&gt;.jpg,
/// где вид — call, radio_problem, radio_A_success и так далее.
///
/// День в имени папки есть, а в игре его под рукой нет: инцидент знает только
/// свою миссию. Спрашивать день у смены нельзя — миссия третьего дня может
/// доигрываться и позже. Поэтому просто перебираем папки дней и берём ту, где
/// файл нашёлся. Перебор идёт через ResourceLoader, а не через чтение каталога:
/// в собранной игре ресурсы лежат в пакете, и обход папок там врёт.
///
/// Найденный путь запоминается: перебор случается один раз на миссию с видом.
/// </summary>
public static class MissionIllustrations
{
	private const string Root = "res://assets/textures/ui/illustrations/missions";

	/// <summary>Докуда перебирать папки дней. С запасом: смен пока четыре.</summary>
	private const int MaxDay = 12;

	private static readonly Dictionary<string, string> ResolvedPaths = new();

	/// <summary>Кадр экрана звонка.</summary>
	public static Texture2D LoadCall(string missionId)
	{
		return Load(missionId, "call");
	}

	/// <summary>Кадр доклада по рации — то, что группа видит на месте.</summary>
	public static Texture2D LoadRadioProblem(string missionId)
	{
		return Load(missionId, "radio_problem");
	}

	/// <summary>
	/// Кадр итога операции.
	///
	/// Вариант задаётся его порядковым номером на экране: 0 — A, 1 — B, 2 — C.
	/// Отрицательный номер значит, что решения по рации не было, и тогда итог
	/// берётся из пары noradio_*.
	/// </summary>
	public static Texture2D LoadOutcome(string missionId, int optionIndex, bool isSuccess)
	{
		string suffix = isSuccess ? "success" : "fail";
		string kind = optionIndex >= 0 && optionIndex < 3
			? $"radio_{(char)('A' + optionIndex)}_{suffix}"
			: $"noradio_{suffix}";
		return Load(missionId, kind);
	}

	public static Texture2D Load(string missionId, string kind)
	{
		if (string.IsNullOrWhiteSpace(missionId) || string.IsNullOrWhiteSpace(kind))
		{
			return null;
		}

		string key = missionId + "/" + kind;
		if (ResolvedPaths.TryGetValue(key, out string cached))
		{
			return cached == null ? null : GD.Load<Texture2D>(cached);
		}

		for (int day = 1; day <= MaxDay; day++)
		{
			string path = $"{Root}/day_{day}/{missionId}/{kind}.jpg";
			if (ResourceLoader.Exists(path))
			{
				ResolvedPaths[key] = path;
				return GD.Load<Texture2D>(path);
			}
		}

		// Кадра нет — не ошибка: часть видов дорисована не для всех миссий.
		// Экран просто останется без картинки, а не свалится.
		ResolvedPaths[key] = null;
		GD.PushWarning($"MissionIllustrations: не найден кадр {kind} для миссии {missionId}.");
		return null;
	}
}
