using System.Runtime.InteropServices;
using System.Text;

namespace Zenith.Core.Interop;

// The shell's .lnk reader (IShellLinkW), to see what a shortcut in the Startup folder runs.
internal static class ShellLink
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkObject;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relPath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    private const uint SlgpRawPath = 0x4;

    /// <summary>The shortcut's command line ("target" arguments), or null when it cannot be read.</summary>
    public static string? CommandLine(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new ShellLinkObject();
            ((IPersistFile)link).Load(lnkPath, 0);
            var shell = (IShellLinkW)link;
            var target = new StringBuilder(1024);
            shell.GetPath(target, target.Capacity, IntPtr.Zero, SlgpRawPath);
            if (target.Length == 0) return null;
            var args = new StringBuilder(4096);
            shell.GetArguments(args, args.Capacity);
            return args.Length == 0 ? $"\"{target}\"" : $"\"{target}\" {args}";
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or FileNotFoundException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }
}
