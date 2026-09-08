using System.Runtime.InteropServices;

namespace GSkillCue.Smbus;

/// <summary>
/// Raw P/Invoke bindings for <c>PawnIOLib.dll</c> (namazso/PawnIO). Every function returns an
/// <c>HRESULT</c> (0 == S_OK). See <c>reference/OpenRGB/dependencies/PawnIO/PawnIOLib.h</c>.
/// The DLL and the <c>*.bin</c> modules must sit next to the executable.
/// </summary>
internal static class PawnIoNative
{
    private const string Dll = "PawnIOLib";

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    internal static extern int pawnio_version(out uint version);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    internal static extern int pawnio_open(out nint handle);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    internal static extern int pawnio_load(nint handle, byte[] blob, nuint size);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal static extern int pawnio_execute(
        nint handle,
        string name,
        ulong[] input,
        nuint inSize,
        ulong[] output,
        nuint outSize,
        out nuint returnSize);

    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    internal static extern int pawnio_close(nint handle);
}
