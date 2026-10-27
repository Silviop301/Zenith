using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace Zenith.Core.Interop;

// kernel32 and iphlpapi, read side only.
internal static class ProcessInfo
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    /// <summary>The executable's full path, with the limited access Windows gives a normal user for most processes.</summary>
    public static string? ImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var size = 1024u;
            var name = new StringBuilder((int)size);
            return QueryFullProcessImageNameW(handle, 0, name, ref size) ? name.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Kernel plus user time, in 100 ns units; null when Windows does not allow it.</summary>
    public static long? CpuTime(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            return GetProcessTimes(handle, out _, out _, out var kernel, out var user) ? kernel + user : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidConnections = 4;
    private const uint MibTcpStateEstab = 5;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

    /// <summary>Distinct remote addresses each process has an open TCP connection to, leaving out this PC itself.</summary>
    public static Dictionary<int, HashSet<string>> RemoteHostsByProcess()
    {
        var result = new Dictionary<int, HashSet<string>>();
        Collect(AfInet, result);
        Collect(AfInet6, result);
        return result;
    }

    private static void Collect(int family, Dictionary<int, HashSet<string>> result)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidConnections, 0);
        for (var attempt = 0; attempt < 3 && size > 0; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var err = GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidConnections, 0);
                if (err == 122) continue; // ERROR_INSUFFICIENT_BUFFER: the table grew, try again with the new size.
                if (err != 0) return;
                Read(buffer, family, result);
                return;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static void Read(IntPtr buffer, int family, Dictionary<int, HashSet<string>> result)
    {
        var count = Marshal.ReadInt32(buffer);
        // MIB_TCPROW_OWNER_PID: state, local addr, local port, remote addr, remote port, pid (6 DWORDs).
        // MIB_TCP6ROW_OWNER_PID: local addr[16], local scope, local port, remote addr[16], remote scope, remote port, state, pid.
        var rowSize = family == AfInet ? 24 : 56;
        for (var i = 0; i < count; i++)
        {
            var row = buffer + 4 + i * rowSize;
            uint state;
            int pid;
            IPAddress remote;
            if (family == AfInet)
            {
                state = (uint)Marshal.ReadInt32(row);
                remote = new IPAddress((uint)Marshal.ReadInt32(row, 12));
                pid = Marshal.ReadInt32(row, 20);
            }
            else
            {
                var bytes = new byte[16];
                Marshal.Copy(row + 24, bytes, 0, 16);
                remote = new IPAddress(bytes);
                state = (uint)Marshal.ReadInt32(row, 48);
                pid = Marshal.ReadInt32(row, 52);
            }
            if (state != MibTcpStateEstab || IPAddress.IsLoopback(remote) || remote.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remote.MapToIPv4()))
                continue;
            if (!result.TryGetValue(pid, out var hosts)) result[pid] = hosts = [];
            hosts.Add(remote.ToString());
        }
    }
}
