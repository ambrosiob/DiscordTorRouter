using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using DiscordTorRouter.Infrastructure;

namespace DiscordTorRouter.Network;

internal enum WinDivertLayer { Network = 0, NetworkForward = 1, Flow = 2, Socket = 3, Reflect = 4 }
internal enum WinDivertShutdown { Receive = 1, Send = 2, Both = 3 }

[StructLayout(LayoutKind.Sequential, Size = 80)]
internal struct WinDivertAddress
{
    public long Timestamp;
    public ulong Flags;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Data;

    public readonly bool Outbound => (Flags & (1UL << 17)) != 0;
    public readonly bool IPv6 => (Flags & (1UL << 20)) != 0;
    public void SetOutbound(bool value) => Flags = value ? Flags | (1UL << 17) : Flags & ~(1UL << 17);
    public readonly byte Event => (byte)((Flags >> 8) & 0xff);
    public static WinDivertAddress Create() => new() { Data = new byte[64] };
}

internal sealed class WinDivertHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private WinDivertHandle() : base(true) { }
    protected override bool ReleaseHandle() => WinDivertNative.WinDivertClose(handle);
}

internal static class WinDivertNative
{
    private const string Library = "WinDivert.dll";
    public const ulong FlagSniff = 0x0001;
    public const ulong FlagReceiveOnly = 0x0004;

    static WinDivertNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(WinDivertNative).Assembly, (name, assembly, path) =>
            name.Equals(Library, StringComparison.OrdinalIgnoreCase)
                ? NativeLibrary.Load(Path.Combine(AppPaths.WinDivertDirectory, Library))
                : IntPtr.Zero);
    }

    [DllImport(Library, CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern WinDivertHandle WinDivertOpen(string filter, WinDivertLayer layer, short priority, ulong flags);
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(WinDivertHandle handle, IntPtr packet, uint packetLength, out uint receiveLength, ref WinDivertAddress address);
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSend(WinDivertHandle handle, IntPtr packet, uint packetLength, out uint sendLength, ref WinDivertAddress address);
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertShutdown(WinDivertHandle handle, WinDivertShutdown how);
    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(IntPtr handle);
    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperParsePacket(IntPtr packet, uint packetLength, out IntPtr ipHeader, out IntPtr ipv6Header, out byte protocol, out IntPtr icmpHeader, out IntPtr icmpv6Header, out IntPtr tcpHeader, out IntPtr udpHeader, out IntPtr data, out uint dataLength, out IntPtr next, out uint nextLength);
    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCalcChecksums(IntPtr packet, uint packetLength, ref WinDivertAddress address, ulong flags);
    [DllImport(Library, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperFormatIPv6Address(IntPtr address, StringBuilder buffer, uint bufferLength);
    [DllImport(Library, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCompileFilter(string filter, WinDivertLayer layer, IntPtr filterObject, uint objectLength, out IntPtr errorText, out uint errorPosition);

    public static void ValidateFilter(string filter, WinDivertLayer layer)
    {
        const int maximumFilterItems = 256;
        var buffer = Marshal.AllocHGlobal(maximumFilterItems * 24);
        try
        {
            if (WinDivertHelperCompileFilter(filter, layer, buffer, maximumFilterItems, out var error, out var position)) return;
            var message = error == IntPtr.Zero ? "unknown error" : Marshal.PtrToStringAnsi(error);
            throw new InvalidOperationException($"Filtro WinDivert inválido na posição {position}: {message}");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static WinDivertHandle OpenChecked(string filter, WinDivertLayer layer, short priority = 0, ulong flags = 0)
    {
        var handle = WinDivertOpen(filter, layer, priority, flags);
        if (handle.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            var systemMessage = new Win32Exception(errorCode).Message;
            throw new Win32Exception(errorCode, $"WinDivertOpen falhou ({layer}), código {errorCode}: {systemMessage}");
        }
        return handle;
    }
}
