using System.Text;
using GSkillCue.Core;

namespace GSkillCue.Smbus;

/// <summary>
/// SMBus access via the PawnIO kernel driver + the AMD-FCH <c>SmbusPIIX4</c> module.
/// Ported from OpenRGB <c>i2c_smbus/Windows/i2c_smbus_pawnio.cpp</c> (GPL-2.0).
///
/// Requires: the process running elevated, <c>PawnIOLib.dll</c> + <c>SmbusPIIX4.bin</c> next to
/// the executable, and the PawnIO driver installed (https://pawnio.eu).
///
/// Writes are gated by <see cref="SmbusPolicy"/> — only 0x70-0x77 (the ENE DRAM RGB controller).
/// </summary>
public sealed class PawnIoSmbus : ISmbus
{
    private const string ModuleFile = "SmbusPIIX4.bin";
    private const string GlobalSmbusMutexName = @"Global\Access_SMBUS.HTP.Method";

    private const int SmbusRead = 1;
    private const int SmbusWrite = 0;
    // PawnIO SmbusPIIX4 sleep modes: 0 = busy-wait (fastest), 1 = short-busy (busy between
    // transfers, yield while waiting for completion), 2 = always yield (OpenRGB's default).
    // We drive many transfers per frame, so 1 keeps latency low without pinning a core.
    private const int SleepModeShortBusy = 1;

    private const int EAccessDenied = unchecked((int)0x80070005);
    private const int ENotSupported = unchecked((int)0x80070032);

    private readonly nint _handle;
    private readonly Mutex? _busMutex;
    private readonly object _xferLock = new();
    private bool _disposed;

    public string Name { get; }
    public (int Vendor, int Device) HostControllerPciId { get; }

    private PawnIoSmbus(nint handle, int port)
    {
        _handle = handle;

        // Best-effort, exactly like OpenRGB: it calls these and ignores the result. The module
        // defaults to port 0 / a sane sleep mode; only ioctl_smbus_xfer actually has to work.
        if (TryExecute("ioctl_piix4_port_sel", [(ulong)port], 1) is null)
            Log.Debug($"ioctl_piix4_port_sel({port}) not accepted by this module; using default port.");
        TryExecute("ioctl_set_sleep_mode", [SleepModeShortBusy], 0);

        var identity = TryExecute("ioctl_identity", [0UL], 3);
        if (identity is { Length: >= 3 })
        {
            Name = $"PawnIO SMBus {DecodePackedName(identity[0])} {port}";
            HostControllerPciId = ((int)(identity[2] & 0xFFFF), (int)((identity[2] >> 16) & 0xFFFF));
        }
        else
        {
            Name = $"PawnIO SMBus PIIX4 {port}";
        }

        try { _busMutex = new Mutex(false, GlobalSmbusMutexName); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Could not open the global SMBus mutex ({ex.Message}); proceeding without it.");
        }
    }

    /// <summary>
    /// Opens PawnIO, loads the AMD-FCH SMBus module and selects <paramref name="port"/>.
    /// Throws <see cref="SmbusException"/> with a precise reason on failure.
    /// </summary>
    public static PawnIoSmbus Open(int port = 0, string? moduleDirectory = null)
    {
        moduleDirectory ??= AppContext.BaseDirectory;
        string modulePath = Path.Combine(moduleDirectory, ModuleFile);

        if (!File.Exists(modulePath))
            throw new SmbusException($"'{ModuleFile}' not found in {moduleDirectory}.");

        byte[] blob;
        try { blob = File.ReadAllBytes(modulePath); }
        catch (IOException ex) { throw new SmbusException($"Could not read '{ModuleFile}': {ex.Message}", ex); }

        int hr;
        nint handle;
        try
        {
            hr = PawnIoNative.pawnio_open(out handle);
        }
        catch (DllNotFoundException ex)
        {
            throw new SmbusException("PawnIOLib.dll not found next to the executable.", ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new SmbusException("PawnIOLib.dll architecture mismatch — this tool must run as x64.", ex);
        }

        if (hr != 0)
        {
            throw new SmbusException(hr == EAccessDenied
                ? "PawnIO access denied — run this application as Administrator."
                : $"pawnio_open failed (HRESULT 0x{hr:X8}). Is the PawnIO driver installed? See https://pawnio.eu");
        }

        hr = PawnIoNative.pawnio_load(handle, blob, (nuint)blob.Length);
        if (hr != 0)
        {
            PawnIoNative.pawnio_close(handle);
            throw new SmbusException(hr == ENotSupported
                ? "The SmbusPIIX4 module reports this platform is unsupported (no AMD FCH SMBus found)."
                : $"pawnio_load(SmbusPIIX4.bin) failed (HRESULT 0x{hr:X8}).");
        }

        return new PawnIoSmbus(handle, port);
    }

    // ---- ISmbus -------------------------------------------------------------------

    public int ReadByte(byte address)
    {
        var raw = Xfer(address, SmbusRead, 0, SmbusXferSize.Byte, null);
        return raw is null ? -1 : raw[0];
    }

    public int ReadByteData(byte address, byte command)
    {
        var raw = Xfer(address, SmbusRead, command, SmbusXferSize.ByteData, null);
        return raw is null ? -1 : raw[0];
    }

    public int ReadWordData(byte address, byte command)
    {
        var raw = Xfer(address, SmbusRead, command, SmbusXferSize.WordData, null);
        return raw is null ? -1 : raw[0] | (raw[1] << 8);
    }

    public byte[]? ReadBlockData(byte address, byte command)
    {
        var raw = Xfer(address, SmbusRead, command, SmbusXferSize.BlockData, null);
        if (raw is null) return null;
        int len = Math.Min(raw[0], (byte)32);
        return raw.AsSpan(1, len).ToArray();
    }

    public void WriteByte(byte address, byte value)
    {
        SmbusPolicy.EnsureWriteAllowed(address);
        Xfer(address, SmbusWrite, value, SmbusXferSize.Byte, null);
    }

    public void WriteByteData(byte address, byte command, byte value)
    {
        SmbusPolicy.EnsureWriteAllowed(address);
        Xfer(address, SmbusWrite, command, SmbusXferSize.ByteData, [value]);
    }

    public void WriteWordData(byte address, byte command, ushort value)
    {
        SmbusPolicy.EnsureWriteAllowed(address);
        Xfer(address, SmbusWrite, command, SmbusXferSize.WordData, [(byte)(value & 0xFF), (byte)(value >> 8)]);
    }

    public void WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data)
    {
        SmbusPolicy.EnsureWriteAllowed(address);
        if (data.Length is 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(data), "SMBus block length must be 1..32.");
        var payload = new byte[data.Length + 1];
        payload[0] = (byte)data.Length;
        data.CopyTo(payload.AsSpan(1));
        Xfer(address, SmbusWrite, command, SmbusXferSize.BlockData, payload);
    }

    // ---- transport --------------------------------------------------------------

    /// <summary>
    /// One SMBus transaction. <paramref name="dataBytes"/> is the raw <c>i2c_smbus_data</c> union
    /// payload (byte 0 = value/len, following bytes = block data). Returns the 34-byte union on
    /// a successful read, an empty array on a successful write, or null on failure.
    /// </summary>
    private byte[]? Xfer(byte address, int readWrite, byte command, SmbusXferSize size, byte[]? dataBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_xferLock)
        {
            bool held = false;
            try
            {
                if (_busMutex is not null)
                {
                    try { held = _busMutex.WaitOne(TimeSpan.FromSeconds(2)); }
                    catch (AbandonedMutexException) { held = true; }
                }

                var input = new ulong[9];
                input[0] = address;
                input[1] = (ulong)readWrite;
                input[2] = command;
                input[3] = (ulong)size;

                // Pack up to 34 union bytes into input[4..8] (little-endian).
                Span<byte> union = stackalloc byte[40];
                union.Clear();
                dataBytes?.AsSpan(0, Math.Min(dataBytes.Length, 34)).CopyTo(union);
                for (int i = 0; i < 5; i++)
                    input[4 + i] = BitConverter.ToUInt64(union.Slice(i * 8, 8));

                var output = new ulong[5];
                int hr = PawnIoNative.pawnio_execute(_handle, "ioctl_smbus_xfer",
                    input, 9, output, 5, out _);

                if (hr != 0)
                    return null;

                if (readWrite == SmbusWrite)
                    return [];

                var result = new byte[34];
                Span<byte> outBytes = stackalloc byte[40];
                for (int i = 0; i < 5; i++)
                    BitConverter.TryWriteBytes(outBytes.Slice(i * 8, 8), output[i]);
                outBytes[..34].CopyTo(result);
                return result;
            }
            finally
            {
                if (held) _busMutex!.ReleaseMutex();
            }
        }
    }

    private ulong[]? TryExecute(string ioctl, ulong[] input, int outCount)
    {
        var output = new ulong[Math.Max(1, outCount)];
        int hr = PawnIoNative.pawnio_execute(_handle, ioctl, input, (nuint)input.Length,
            output, (nuint)outCount, out _);
        return hr == 0 ? output : null;
    }

    private static string DecodePackedName(ulong packed)
    {
        Span<byte> b = stackalloc byte[8];
        BitConverter.TryWriteBytes(b, packed);
        int len = b.IndexOf((byte)0);
        return Encoding.ASCII.GetString(b[..(len < 0 ? 8 : len)]).Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _busMutex?.Dispose();
        if (_handle != 0)
            PawnIoNative.pawnio_close(_handle);
    }
}
