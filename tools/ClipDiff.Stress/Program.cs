using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace ClipDiff.Stress;

internal static class Program
{
	private const string Usage = """
		ClipDiff.Stress --mode mixed --copies 1000 --size-kib 256 --delay-ms 200

		--mode text|files|paths|mixed   Default: mixed
		--copies <1..1000000>         Clipboard write attempts. Default: 1000
		--size-kib <1..16384>         ASCII bytes per generated value/file. Default: 256
		--delay-ms <10..60000>        Delay after each attempt. Default: 200
		--cooldown-seconds <0..600>   Watch counters after copying stops. Default: 30
		--single-line                Use one long line instead of many short lines
		--pid <id>                   Target a specific ClipDiff process

		Temporarily overwrites the clipboard with generated data. Ctrl+C stops the run.
		Does not read/restore the existing clipboard, change ClipDiff settings, or open a viewer.
		File modes create two synthetic temporary files and remove them on exit.
		Only process counters go to standard output (CSV); status goes to standard error.
		""";

	private static int Main(string[] arguments)
	{
		if (arguments.Length == 1 && arguments[0] is "--help" or "-h")
		{
			Console.WriteLine(Usage);
			return 0;
		}

		try
		{
			var options = StressOptions.Parse(arguments);

			if (!OperatingSystem.IsWindows())
			{
				Console.Error.WriteLine("Run this tool on the Windows desktop where ClipDiff is running.");
				return 1;
			}

			return Run(options);
		}
		catch (OperationCanceledException)
		{
			Console.Error.WriteLine("Stress run stopped.");
			return 0;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
			IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
		{
			Console.Error.WriteLine(exception.Message);
			return 1;
		}
	}

	[SupportedOSPlatform("windows")]
	private static int Run(StressOptions options)
	{
		using var sampler = new ProcessSampler(ProcessSampler.FindTarget(options.ProcessId));
		using var publisher = new NativeClipboardPublisher();
		using var cancellation = new CancellationTokenSource();
		ConsoleCancelEventHandler onCancel = (_, args) =>
		{
			args.Cancel = true;
			cancellation.Cancel();
		};
		Console.CancelKeyPress += onCancel;
		string? fixtureDirectory = null;

		try
		{
			var previous = StressWorkload.CreateText(options.SizeKiB, 'A', options.SingleLine);
			var current = StressWorkload.CreateText(options.SizeKiB, 'B', options.SingleLine);
			var previousPath = string.Empty;
			var currentPath = string.Empty;

			if (options.Mode != StressMode.Text)
			{
				fixtureDirectory = Directory.CreateTempSubdirectory("ClipDiff-stress-").FullName;
				previousPath = Path.Combine(fixtureDirectory, "Previous synthetic.txt");
				currentPath = Path.Combine(fixtureDirectory, "Current synthetic.txt");
				File.WriteAllText(previousPath, previous, new UTF8Encoding(false));
				File.WriteAllText(currentPath, current, new UTF8Encoding(false));
			}

			var workload = StressWorkload.Create(options.Mode, previous, current, previousPath, currentPath);
			Console.Error.WriteLine($"{options.Mode}: {options.Copies} attempts, {options.SizeKiB} KiB/value, {options.DelayMilliseconds} ms delay.");
			Console.Error.WriteLine("Clipboard will be overwritten. Ctrl+C stops. Invoke Show Diff manually to exercise the viewer.");
			Console.Error.WriteLine("Writes counts successful clipboard writes, not confirmed ClipDiff captures. Fast updates can supersede reads.");
			Console.WriteLine("TimeUtc,Phase,Attempts,Writes,Skipped,PrivateMiB,WorkingSetMiB,Handles,Threads,GdiObjects,UserObjects,CpuSeconds");
			var clock = Stopwatch.StartNew();
			var nextSample = TimeSpan.FromSeconds(5);
			var writes = 0;
			sampler.Sample("baseline", 0, 0);

			for (var index = 0; index < options.Copies; index++)
			{
				if (publisher.TryWrite(workload[index % workload.Length], cancellation.Token))
				{
					writes++;
				}

				cancellation.Token.WaitHandle.WaitOne(options.DelayMilliseconds);
				cancellation.Token.ThrowIfCancellationRequested();

				if (clock.Elapsed >= nextSample)
				{
					sampler.Sample("copying", index + 1, writes);
					nextSample = clock.Elapsed + TimeSpan.FromSeconds(5);
				}
			}

			sampler.Sample("copies-finished", options.Copies, writes);
			Console.Error.WriteLine($"Copying stopped; watching for {options.CooldownSeconds} seconds. Try Show Diff using the latest captures.");
			var cooldownEnd = clock.Elapsed + TimeSpan.FromSeconds(options.CooldownSeconds);

			while (clock.Elapsed < cooldownEnd)
			{
				var remaining = cooldownEnd - clock.Elapsed;

				if (remaining <= TimeSpan.Zero)
				{
					break;
				}

				cancellation.Token.WaitHandle.WaitOne(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5));
				cancellation.Token.ThrowIfCancellationRequested();
				sampler.Sample("cooldown", options.Copies, writes);
			}

			return 0;
		}
		finally
		{
			Console.CancelKeyPress -= onCancel;

			if (fixtureDirectory is not null)
			{
				try
				{
					Directory.Delete(fixtureDirectory, recursive: true);
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
				{
					Console.Error.WriteLine("Synthetic fixture cleanup failed; a ClipDiff-stress-* directory may remain in the temporary folder.");
				}
			}
		}
	}
}
