using System.Globalization;

namespace ClipDiff.Stress;

internal enum StressMode
{
	Text,
	Files,
	Paths,
	Mixed
}

internal sealed record StressOptions(
	StressMode Mode = StressMode.Mixed,
	int Copies = 1000,
	int SizeKiB = 256,
	int DelayMilliseconds = 200,
	int CooldownSeconds = 30,
	int ProcessId = 0,
	bool SingleLine = false)
{
	public static StressOptions Parse(string[] arguments)
	{
		var options = new StressOptions();

		for (var index = 0; index < arguments.Length; index++)
		{
			var name = arguments[index];

			if (name == "--single-line")
			{
				options = options with { SingleLine = true };
				continue;
			}

			if (++index == arguments.Length)
			{
				throw new ArgumentException($"Missing value for {name}.");
			}

			var value = arguments[index];
			options = name switch
			{
				"--mode" => options with
				{
					Mode = value.ToLowerInvariant() switch
					{
						"text" => StressMode.Text,
						"files" => StressMode.Files,
						"paths" => StressMode.Paths,
						"mixed" => StressMode.Mixed,
						_ => throw new ArgumentException("Mode must be text, files, paths or mixed.")
					}
				},
				"--copies" => options with { Copies = Number(name, value, 1, 1_000_000) },
				"--size-kib" => options with { SizeKiB = Number(name, value, 1, 16_384) },
				"--delay-ms" => options with { DelayMilliseconds = Number(name, value, 10, 60_000) },
				"--cooldown-seconds" => options with { CooldownSeconds = Number(name, value, 0, 600) },
				"--pid" => options with { ProcessId = Number(name, value, 1, int.MaxValue) },
				_ => throw new ArgumentException($"Unknown option: {name}.")
			};
		}

		return options;
	}

	private static int Number(string name, string value, int minimum, int maximum)
	{
		if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
			number < minimum || number > maximum)
		{
			throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
		}

		return number;
	}
}
