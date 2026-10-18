using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ClipDiff.Windows.Clipboard;

internal static class ShellShortcutTargetReader
{
	internal static readonly Guid ClassId = new("00021401-0000-0000-C000-000000000046");

	public static string? Read(string filePath)
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		var shellLink = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(ClassId, throwOnError: true)!)!;

		try
		{
			((IPersistFile)shellLink).Load(filePath, 0);

			// GetPath reads the stored filesystem target. Do not call Resolve:
			// that can search for moved files, prompt, or update link information.
			// https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-getpath
			const int maximumPathCharacters = 260;
			var target = new StringBuilder(maximumPathCharacters);
			var result = shellLink.GetPath(target, target.Capacity, nint.Zero, 0);

			// Reject a potentially truncated target instead of reading a different file.
			return result == 0 && target.Length is > 0 and < maximumPathCharacters - 1
				? target.ToString()
				: null;
		}
		finally
		{
			Marshal.FinalReleaseComObject(shellLink);
		}
	}

	[ComImport]
	[Guid("000214F9-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IShellLinkW
	{
		[PreserveSig]
		int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int characterCount, nint findData, uint flags);
		void GetIDList(out nint itemIdList);
		void SetIDList(nint itemIdList);
		void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int characterCount);
		void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
		void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int characterCount);
		void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
		void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int characterCount);
		void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
		void GetHotkey(out ushort hotkey);
		void SetHotkey(ushort hotkey);
		void GetShowCmd(out int showCommand);
		void SetShowCmd(int showCommand);
		void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int characterCount, out int iconIndex);
		void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
		void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
		void Resolve(nint window, uint flags);
		void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
	}
}
