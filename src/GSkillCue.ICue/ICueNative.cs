using System.Runtime.InteropServices;

namespace GSkillCue.ICue;

// ReSharper disable InconsistentNaming — names mirror the C SDK for grep-ability.

public enum CorsairError
{
    Success = 0,
    NotConnected = 1,
    NoControl = 2,
    IncompatibleProtocol = 3,
    InvalidArguments = 4,
    InvalidOperation = 5,
    DeviceNotFound = 6,
    NotAllowed = 7,
}

public enum CorsairSessionState
{
    Invalid = 0,
    Closed = 1,
    Connecting = 2,
    Timeout = 3,
    ConnectionRefused = 4,
    ConnectionLost = 5,
    Connected = 6,
}

[Flags]
public enum CorsairDeviceType : uint
{
    Unknown = 0x0000,
    Keyboard = 0x0001,
    Mouse = 0x0002,
    Mousemat = 0x0004,
    Headset = 0x0008,
    HeadsetStand = 0x0010,
    FanLedController = 0x0020,
    LedController = 0x0040,
    MemoryModule = 0x0080,
    Cooler = 0x0100,
    Motherboard = 0x0200,
    GraphicsCard = 0x0400,
    Touchbar = 0x0800,
    GameController = 0x1000,
    All = 0xFFFFFFFF,
}

internal static class ICueConstants
{
    public const int StringSizeM = 128;
    public const int DeviceCountMax = 64;
    public const int DeviceLedCountMax = 512;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CorsairVersion
{
    public int Major;
    public int Minor;
    public int Patch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CorsairSessionDetails
{
    public CorsairVersion ClientVersion;
    public CorsairVersion ServerVersion;
    public CorsairVersion ServerHostVersion;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CorsairSessionStateChanged
{
    public CorsairSessionState State;
    public CorsairSessionDetails Details;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CorsairDeviceFilter
{
    public int DeviceTypeMask;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct CorsairDeviceInfo
{
    public CorsairDeviceType Type;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ICueConstants.StringSizeM)]
    public string Id;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ICueConstants.StringSizeM)]
    public string Serial;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ICueConstants.StringSizeM)]
    public string Model;
    public int LedCount;
    public int ChannelCount;
}

[StructLayout(LayoutKind.Sequential)]
public struct CorsairLedPosition
{
    public uint Id;
    public double Cx;
    public double Cy;
}

[StructLayout(LayoutKind.Sequential)]
public struct CorsairLedColor
{
    public uint Id;
    public byte R;
    public byte G;
    public byte B;
    public byte A;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void CorsairSessionStateChangedHandler(nint context, nint eventData);

internal static class ICueNative
{
    private const string Dll = "iCUESDK.x64_2019.dll";
    private const CallingConvention Cc = CallingConvention.Cdecl;

    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern CorsairError CorsairConnect(CorsairSessionStateChangedHandler onStateChanged, nint context);

    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern CorsairError CorsairGetSessionDetails(out CorsairSessionDetails details);

    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern CorsairError CorsairDisconnect();

    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern CorsairError CorsairGetDevices(
        in CorsairDeviceFilter filter, int sizeMax,
        [Out] CorsairDeviceInfo[] devices, out int size);

    [DllImport(Dll, CallingConvention = Cc, CharSet = CharSet.Ansi)]
    internal static extern CorsairError CorsairGetDeviceInfo(string deviceId, out CorsairDeviceInfo deviceInfo);

    [DllImport(Dll, CallingConvention = Cc, CharSet = CharSet.Ansi)]
    internal static extern CorsairError CorsairGetLedPositions(
        string deviceId, int sizeMax, [Out] CorsairLedPosition[] ledPositions, out int size);

    [DllImport(Dll, CallingConvention = Cc, CharSet = CharSet.Ansi)]
    internal static extern CorsairError CorsairGetLedColors(
        string deviceId, int size, [In, Out] CorsairLedColor[] ledColors);

    [DllImport(Dll, CallingConvention = Cc)]
    internal static extern CorsairError CorsairSetLayerPriority(uint priority);
}
