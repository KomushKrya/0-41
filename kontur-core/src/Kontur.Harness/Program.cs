using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Kontur.Core.Api;
using Kontur.Core.Content;
using Kontur.Core.Events;
using Kontur.Core.Model;

namespace Kontur.Harness
{
	/// <summary>
	/// Headless-прогон смен без Godot.
	///
	///   dotnet run --project src/Kontur.Harness -- --days 4 --seed 41 --strategy best
	///   dotnet run --project src/Kontur.Harness -- --selftest
	/// </summary>
	public static class Program
	{
		public static int Main(string[] args)
		{
			Console.OutputEncoding = Encoding.UTF8;

			var options = HarnessOptions.Parse(args);

			string? contentPath = options.ContentPath ?? FindContentDirectory();
			if (contentPath == null)
			{
				Console.Error.WriteLine("Не найдена папка content. Укажите путь: --content <путь>");
				return 2;
			}

			// Запоминаем найденный путь, чтобы шапка прогона показала источник контента.
			options.ContentPath = contentPath;

			// Каталог текстов: тот же собранный JSON, что читает игра. Без него прогон
			// возможен, но опечатки в id останутся незамеченными до запуска в движке.
			ITextCatalog? textCatalog = null;
			string? localePath = FindLocaleDirectory(contentPath, options.Locale);
			if (localePath != null)
			{
				textCatalog = JsonTextCatalog.Load(new DirectoryContentSource(localePath));
			}
			else
			{
				Console.WriteLine($"Каталог текстов не найден (локаль {options.Locale}) — сверка id пропущена.");
			}

			ContentDatabase content;
			try
			{
				content = ContentLoader.Load(new DirectoryContentSource(contentPath), textCatalog);
			}
			catch (ContentException exception)
			{
				Console.Error.WriteLine(exception.Message);
				return 3;
			}

			if (options.SelfTest)
			{
				return SelfTest.Run(content) ? 0 : 1;
			}

			return RunShifts(content, options);
		}

		private static int RunShifts(ContentDatabase content, HarnessOptions options)
		{
			var simulation = new KonturSimulation(content, options.Seed);

			double clock = 0.0;
			var log = new ConsoleEventLog(() => clock, options.Verbose);
			log.Attach(simulation.Events);

			var oper = new AutoOperator(simulation, content, options.Strategy, options.Seed)
			{
				AnswerDelay = options.AnswerDelay,
				DispatchDelay = options.DispatchDelay
			};

			bool gameOver = false;
			simulation.Events.Subscribe<GameOverTriggered>(_ => gameOver = true);

			PrintHeader(options, content);

			for (int day = 1; day <= options.Days && !gameOver; day++)
			{
				clock = 0.0;

				CommandResult start = simulation.StartShift(day);
				if (!start.IsSuccess)
				{
					Console.Error.WriteLine(start.Error);
					return 4;
				}

				double guard = 0.0;
				while (simulation.IsShiftActive && !gameOver && guard < options.MaxShiftSeconds)
				{
					simulation.Tick(options.DeltaSeconds);
					clock += options.DeltaSeconds;
					guard += options.DeltaSeconds;

					oper.Update();
				}

				if (simulation.IsShiftActive)
				{
					Console.WriteLine("!! Смена не завершилась за отведённое время — принудительное закрытие.");
					simulation.ForceEndShift();
				}

				PrintRoster(simulation);
				PrintZones(simulation);

				if (!gameOver && day < options.Days)
				{
					oper.BetweenShifts(day + 1);
				}
			}

			PrintFinal(simulation);
			return gameOver ? 1 : 0;
		}

		private static void PrintHeader(HarnessOptions options, ContentDatabase content)
		{
			Console.WriteLine("К.О.Н.Т.У.Р. — headless-прогон ядра");
			Console.WriteLine(new string('=', 78));
			Console.WriteLine(
				"seed={0} дней={1} шаг={2}с стратегия радио={3}",
				options.Seed,
				options.Days,
				options.DeltaSeconds.ToString("0.##", CultureInfo.InvariantCulture),
				options.Strategy);
			Console.WriteLine(
				$"контент: зон {content.Zones.Count}, существ {content.Creatures.Count}, миссий {content.Missions.Count}, "
				+ $"вмешательств {content.MissionEvents.Count}, снаряжения {content.Equipment.Count}");

			// Путь печатается не для красоты: в дереве живёт вторая копия контента,
			// и прогон по ней однажды уже разошёлся с игрой. Пусть источник виден сразу.
			Console.WriteLine($"источник: {options.ContentPath}");
			Console.WriteLine(new string('=', 78));
		}

		private static void PrintRoster(KonturSimulation simulation)
		{
			Console.WriteLine();
			Console.WriteLine("ШТАТ:");

			IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
			for (int i = 0; i < roster.Count; i++)
			{
				EmployeeView employee = roster[i];
				string status = employee.Status == EmployeeStatus.Dead
					? "ПОГИБ"
					: employee.IsInjured ? "травмирован" : "в строю";

				Console.WriteLine(
					$"  {employee.Name,-20} ур.{employee.Level} [{employee.Stats}] опыт {employee.Experience}/{employee.ExperienceToNextLevel} — {status}");
			}
		}

		private static void PrintZones(KonturSimulation simulation)
		{
			Console.WriteLine("КАРТА:");
			IReadOnlyList<ZoneView> zones = simulation.GetZones();
			for (int i = 0; i < zones.Count; i++)
			{
				Console.WriteLine($"  {zones[i].Name,-22} {zones[i].State}");
			}

			Console.WriteLine();
		}

		private static void PrintFinal(KonturSimulation simulation)
		{
			ShiftStatusView status = simulation.GetStatus();

			Console.WriteLine(new string('=', 78));
			Console.WriteLine($"ИТОГ: {status.Scales}");

			if (status.IsGameOver && status.GameOverReason.HasValue)
			{
				Console.WriteLine($"Партия проиграна: {status.GameOverReason.Value}");
			}

			Console.WriteLine();
			Console.WriteLine("ЭНЦИКЛОПЕДИЯ:");
			IReadOnlyList<EncyclopediaEntryView> entries = simulation.GetEncyclopedia();
			if (entries.Count == 0)
			{
				Console.WriteLine("  (пусто)");
			}

			for (int i = 0; i < entries.Count; i++)
			{
				EncyclopediaEntryView entry = entries[i];
				Console.WriteLine($"  {entry.CreatureId}: свойств {entry.RevealedPropertyIds.Count} из {entry.TotalProperties}");
			}
		}

		/// <summary>
		/// content/localisation/&lt;локаль&gt; рядом с папкой данных: ищем вверх от неё же,
		/// чтобы прогон работал и из корня проекта, и из папки ядра.
		/// </summary>
		private static string? FindLocaleDirectory(string contentPath, string locale)
		{
			var directory = new DirectoryInfo(contentPath);

			for (int depth = 0; depth < 8 && directory != null; depth++)
			{
				string candidate = Path.Combine(directory.FullName, "content", "localisation", locale);
				if (Directory.Exists(candidate))
				{
					return candidate;
				}

				directory = directory.Parent;
			}

			return null;
		}

		/// <summary>
		/// Ищет контент игры: поднимается по дереву до корня — папки с project.godot —
		/// и берёт data/ оттуда. Другого источника нет.
		///
		/// Раньше был запасной вариант «ближайшая папка с config.json», и рядом с ядром
		/// лежала своя копия контента. Она оказывалась ближе к бинарнику, чем корневая
		/// data/: харнесс гонял баланс по копии, игра шла по оригиналу, и расхождение
		/// всплыло не сразу. Копия удалена, запасной поиск вместе с ней — лучше честно
		/// не найти контент и попросить --content, чем молча взять не тот.
		/// </summary>
		private static string? FindContentDirectory()
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);

			for (int depth = 0; depth < 8 && directory != null; depth++)
			{
				if (File.Exists(Path.Combine(directory.FullName, "project.godot")))
				{
					string data = Path.Combine(directory.FullName, "data");
					if (File.Exists(Path.Combine(data, ContentLoader.ConfigFile)))
					{
						return data;
					}
				}

				directory = directory.Parent;
			}

			return null;
		}
	}

	public sealed class HarnessOptions
	{
		public int Seed { get; set; } = 41;

		public int Days { get; set; } = 4;

		public double DeltaSeconds { get; set; } = 0.25;

		public double MaxShiftSeconds { get; set; } = 1800.0;

		public double AnswerDelay { get; set; } = 2.0;

		public double DispatchDelay { get; set; } = 4.0;

		public RadioStrategy Strategy { get; set; } = RadioStrategy.Best;

		public bool Verbose { get; set; }

		public bool SelfTest { get; set; }

		public string? ContentPath { get; set; }

		public string Locale { get; set; } = "ru";

		public static HarnessOptions Parse(string[] args)
		{
			var options = new HarnessOptions();

			for (int i = 0; i < args.Length; i++)
			{
				string key = args[i];
				string? value = i + 1 < args.Length ? args[i + 1] : null;

				switch (key)
				{
					case "--seed":
						options.Seed = ParseInt(value, options.Seed);
						i++;
						break;
					case "--days":
						options.Days = ParseInt(value, options.Days);
						i++;
						break;
					case "--dt":
						options.DeltaSeconds = ParseDouble(value, options.DeltaSeconds);
						i++;
						break;
					case "--answer-delay":
						options.AnswerDelay = ParseDouble(value, options.AnswerDelay);
						i++;
						break;
					case "--dispatch-delay":
						options.DispatchDelay = ParseDouble(value, options.DispatchDelay);
						i++;
						break;
					case "--strategy":
						options.Strategy = ParseStrategy(value);
						i++;
						break;
					case "--content":
						options.ContentPath = value;
						i++;
						break;
					case "--locale":
						options.Locale = value ?? options.Locale;
						i++;
						break;
					case "--verbose":
						options.Verbose = true;
						break;
					case "--selftest":
						options.SelfTest = true;
						break;
				}
			}

			return options;
		}

		private static int ParseInt(string? value, int fallback)
		{
			int parsed;
			return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
		}

		private static double ParseDouble(string? value, double fallback)
		{
			double parsed;
			return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
		}

		private static RadioStrategy ParseStrategy(string? value)
		{
			RadioStrategy parsed;
			return Enum.TryParse<RadioStrategy>(value, true, out parsed) ? parsed : RadioStrategy.Best;
		}
	}
}
