using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Zenith.Service;

/// <summary>
/// The user behind a pipe client, read straight from the client process's token. This avoids impersonation,
/// which needs the client to grant a higher level, and WindowsIdentity, which needs rights to duplicate the token.
/// </summary>
internal static class ClientProcess
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUserClass = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    public static string UserSid(SafePipeHandle pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe, out var pid)) throw Win32("GetNamedPipeClientProcessId");

        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) throw Win32("OpenProcess");
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token)) throw Win32("OpenProcessToken");
            try
            {
                return TokenUserSid(token);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string TokenUserSid(IntPtr token)
    {
        GetTokenInformation(token, TokenUserClass, IntPtr.Zero, 0, out var length);
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, TokenUserClass, buffer, length, out _)) throw Win32("GetTokenInformation");
            // TOKEN_USER starts with SID_AND_ATTRIBUTES, whose first field is the SID pointer.
            var sid = Marshal.ReadIntPtr(buffer);
            if (!ConvertSidToStringSid(sid, out var text)) throw Win32("ConvertSidToStringSid");
            try
            {
                return Marshal.PtrToStringUni(text)!;
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static InvalidOperationException Win32(string api) => new($"{api} failed ({Marshal.GetLastWin32Error()}).");
}
