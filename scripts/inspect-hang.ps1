[CmdletBinding()]
param(
	[int]$ClipDiffProcessId = 0
)

$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT')
{
	throw 'Run this read-only inspection on the Windows desktop where ClipDiff is unresponsive.'
}

# Do not request clipboard data, window titles, command lines, selected paths,
# settings contents, or memory dumps. This script does not restart any process.
if (-not ('ClipDiffHangInspection' -as [type]))
{
	Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClipDiffHangInspection
{
	[DllImport("user32.dll", SetLastError = true)]
	public static extern uint GetGuiResources(IntPtr process, uint flags);
}
'@
}

if ($ClipDiffProcessId -gt 0)
{
	$targets = @(Get-Process -Id $ClipDiffProcessId)
}
else
{
	$targets = @(Get-Process -Name ClipDiff -ErrorAction SilentlyContinue)
}

if ($targets.Count -eq 0)
{
	Write-Host 'No ClipDiff process was found. For a dotnet-hosted launch, supply -ClipDiffProcessId.'
	return
}

Write-Host "Inspection UTC: $([DateTime]::UtcNow.ToString('O'))"

foreach ($target in $targets)
{
	try
	{
		$target.Refresh()
		[pscustomobject]@{
			ProcessId = $target.Id
			SessionId = $target.SessionId
			StartedUtc = $target.StartTime.ToUniversalTime().ToString('O')
			Responding = $target.Responding
			CpuSeconds = $target.TotalProcessorTime.TotalSeconds
			PrivateMiB = [Math]::Round($target.PrivateMemorySize64 / 1MB, 1)
			WorkingSetMiB = [Math]::Round($target.WorkingSet64 / 1MB, 1)
			Handles = $target.HandleCount
			Threads = $target.Threads.Count
			GdiObjects = [ClipDiffHangInspection]::GetGuiResources($target.Handle, 0)
			UserObjects = [ClipDiffHangInspection]::GetGuiResources($target.Handle, 1)
		} | Format-List

		$threadRows = foreach ($thread in $target.Threads)
		{
			try
			{
				[pscustomobject]@{
					ThreadId = $thread.Id
					State = $thread.ThreadState
					WaitReason = $(if ($thread.ThreadState -eq 'Wait') { $thread.WaitReason } else { $null })
					CpuSeconds = $thread.TotalProcessorTime.TotalSeconds
				}
			}
			finally
			{
				$thread.Dispose()
			}
		}

		$threadRows | Format-Table -AutoSize

		Write-Host "For a wait chain: Task Manager > Details > process $($target.Id) > Analyze wait chain."
		Write-Host "If dotnet-stack is installed: dotnet-stack report --process-id $($target.Id)"
		Write-Host 'Managed stacks contain method names, not clipboard values. Cancel collection if it stalls.'
		Write-Host 'Responding is only a hint for a tray application; a wait chain/stack is stronger evidence.'
	}
	catch
	{
		Write-Host "Could not inspect process $($target.Id): $($_.Exception.GetType().Name). It may have exited or denied access."
	}
	finally
	{
		$target.Dispose()
	}
}
