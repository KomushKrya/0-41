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

			string contentPath = options.ContentPath ?? FindContentDirectory();
			if (contentPath == null)
			{
				Console.Error.WriteLine("Не найдена папка content. Укажите путь: --content <путь>");
				return 2;
			}

			ContentDatabase content;
			try
			{
				ITextCatalog? texts = Directory.Exists("content/localisation/ru")
					? JsonTextCatalog.Load(new DirectoryContentSource("content/localisation/ru"))
					: null;
				content = ContentLoader.Load(new DirectoryContentSource(contentPath), texts);
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
			var session = new KonturSimulation(content, options.Seed);

			double clock = 0.0;
			var log = new ConsoleEventLog(() => clock, options.Verbose);
			log.Attach(session.Events);

			var oper = new AutoOperator(session, content, options.Strategy, options.Seed)
			{
				AnswerDelay = options.AnswerDelay,
				DispatchDelay = options.DispatchDelay
			};

			bool gameOver = false;
			session.Events.Subscribe<GameOverTriggered>(_ => gameOver = true);

			PrintHeader(options, content);

			for (int day = 1; day <= options.Days && !gameOver; day++)
			{
				clock = 0.0;

				CommandResult start = session.StartShift(day);
				if (!start.IsSuccess)
				{
					Console.Error.WriteLine(start.Error);
					return 4;
				}

				double guard = 0.0;
				while (session.IsShiftActive && !gameOver && guard < options.MaxShiftSeconds)
				{
					session.Tick(options.DeltaSeconds);
					clock += options.DeltaSeconds;
					guard += options.DeltaSeconds;

					oper.Update();
				}

				if (session.IsShiftActive)
				{
					Console.WriteLine("!! Смена не завершилась за отведённое время — принудительное закрытие.");
					session.ForceEndShift();
				}

				PrintRoster(session);

				if (!gameOver && day < options.Days)
				{
					oper.BetweenShifts(day + 1);
				}
			}

			PrintFinal(session);
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
				$"контент: зон {content.Buildings.Count}, существ {content.Creatures.Count}, миссий {content.Missions.Count}, "
				+ $"событий по радио {content.MissionEvents.Count}, снаряжения {content.Equipment.Count}");
			Console.WriteLine(new string('=', 78));
		}

		private static void PrintRoster(KonturSimulation session)
		{
			Console.WriteLine();
			Console.WriteLine("ШТАТ:");

			IReadOnlyList<EmployeeView> roster = session.GetRoster();
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

		private static void PrintFinal(KonturSimulation session)
		{
			ShiftStatusView status = session.GetStatus();

			Console.WriteLine(new string('=', 78));
			Console.WriteLine($"ИТОГ: {status.Scales}");

			if (status.IsGameOver && status.GameOverReason.HasValue)
			{
				Console.WriteLine($"Партия проиграна: {status.GameOverReason.Value}");
			}

			Console.WriteLine();
			Console.WriteLine("ЭНЦИКЛОПЕДИЯ:");
			IReadOnlyList<EncyclopediaEntryView> entries = session.GetEncyclopedia();
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
		/// Ищет контент вверх по дереву. Сначала data/ в корне Godot-проекта — это
		/// единственный источник истины после интеграции; content/ внутри ядра остаётся
		/// запасным вариантом для автономного прогона.
		/// </summary>
		private static string? FindContentDirectory()
		{
			string[] candidates = { "data", "content" };
			var directory = new DirectoryInfo(AppContext.BaseDirectory);

			for (int depth = 0; depth < 8 && directory != null; depth++)
			{
				for (int i = 0; i < candidates.Length; i++)
				{
					string candidate = Path.Combine(directory.FullName, candidates[i]);
					if (File.Exists(Path.Combine(candidate, ContentLoader.ConfigFile)))
					{
						return candidate;
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
