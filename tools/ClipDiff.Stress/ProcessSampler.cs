using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClipDiff.Stress;

[SupportedOSPlatform("windows")]
internal sealed class ProcessSampler(Process target) : IDisposable
{
	private readonly Process process = target;

	public void Sample(string phase, int attempts, int writes)
	{
		process.Refresh();

		if (process.HasExited)
		{
			throw new InvalidOperationException("ClipDiff exited. Stopping the stress run.");
		}

		Console.WriteLine(FormattableString.Invariant(
			$"{DateTimeOffset.UtcNow:O},{phase},{attempts},{writes},{attempts - writes},{process.PrivateMemorySize64 / 1048576d:F1},{process.WorkingSet64 / 1048576d:F1},{process.HandleCount},{process.Threads.Count},{GuiObjects(0)},{GuiObjects(1)},{process.TotalProcessorTime.TotalSeconds:F2}"));
	}

	public void Dispose() => process.Dispose();

	private uint? GuiObjects(uint kind)
	{
		var count = GetGuiResources(process.Handle, kind);
		return count == 0 && Marshal.GetLastPInvokeError() != 0 ? null : count;
	}

	public static Process FindTarget(int processId)
	{
		if (processId != 0)
		{
			return Process.GetProcessById(processId);
		}

		using var current = Process.GetCurrentProcess();
		var candidates = Process.GetProcessesByName("ClipDiff");
		Process? selected = null;

		try
		{
			var matches = candidates.Where(candidate => candidate.SessionId == current.SessionId).ToArray();

			if (matches.Length != 1)
			{
				throw new InvalidOperationException("Start one ClipDiff instance in this session, or use --pid <process id>.");
			}

			selected = matches[0];
			Console.Error.WriteLine($"Monitoring ClipDiff PID {selected.Id}.");
			return selected;
		}
		finally
		{
			foreach (var candidate in candidates)
			{
				if (!ReferenceEquals(candidate, selected))
				{
					candidate.Dispose();
				}
			}
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint GetGuiResources(nint processHandle, uint flags);
}
