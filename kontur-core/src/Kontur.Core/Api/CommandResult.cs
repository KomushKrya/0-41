namespace Kontur.Core.Api
{
	/// <summary>
	/// Результат команды игрока. Ядро никогда не бросает исключения на действия игрока —
	/// UI получает явную причину отказа и сам решает, что показать оператору.
	/// </summary>
	public readonly struct CommandResult
	{
		private CommandResult(bool isSuccess, string error)
		{
			IsSuccess = isSuccess;
			Error = error;
		}

		public bool IsSuccess { get; }

		public string Error { get; }

		public static CommandResult Ok()
		{
			return new CommandResult(true, string.Empty);
		}

		public static CommandResult Fail(string error)
		{
			return new CommandResult(false, error);
		}

		public override string ToString()
		{
			return IsSuccess ? "OK" : "ОТКАЗ: " + Error;
		}
	}
}
