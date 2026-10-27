using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Zenith.Service;

/// <summary>The user behind a pipe client, read from the client process's token.</summary>
internal static class ClientProcess
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static string? UserSid(SafePipeHandle pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe, out var pid))
            throw new InvalidOperationException($"GetNamedPipeClientProcessId failed ({Marshal.GetLastWin32Error()}).");

        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero) throw new InvalidOperationException($"OpenProcess failed ({Marshal.GetLastWin32Error()}).");
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
                throw new InvalidOperationException($"OpenProcessToken failed ({Marshal.GetLastWin32Error()}).");
            try
            {
                using var identity = new WindowsIdentity(token);
                return identity.User?.Value;
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
}
