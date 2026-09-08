using System.Runtime.InteropServices;
using GSkillCue.Core;

namespace GSkillCue.ICue;

public sealed class ICueException(string message, CorsairError error = CorsairError.Success)
    : Exception(error == CorsairError.Success ? message : $"{message} ({error})")
{
    public CorsairError Error { get; } = error;
}

public sealed record ICueDevice(string Id, string Model, CorsairDeviceType Type, int LedCount, int ChannelCount);

/// <summary>
/// Thin wrapper over the iCUE SDK v4. Read-only: we connect as a shared client and only ever
/// call <c>CorsairGetLedColors</c> / <c>CorsairGetLedPositions</c>, so iCUE keeps full control of
/// its own devices and there is no layer-priority conflict.
/// </summary>
public sealed class ICueClient : IDisposable
{
    // Keep the delegate alive for the lifetime of the connection.
    private readonly CorsairSessionStateChangedHandler _handler;
    private readonly ManualResetEventSlim _connected = new(false);
    private volatile CorsairSessionState _state = CorsairSessionState.Closed;
    private bool _disposed;

    public CorsairSessionState State => _state;
    public string ServerHostVersion { get; private set; } = "unknown";

    public ICueClient()
    {
        _handler = OnStateChanged;
    }

    /// <summary>
    /// Connects to iCUE and waits until the session is established. Throws if iCUE is not running,
    /// the SDK is disabled in iCUE settings, or the handshake times out.
    /// </summary>
    public void Connect(TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var err = ICueNative.CorsairConnect(_handler, nint.Zero);
        if (err != CorsairError.Success)
            throw new ICueException("CorsairConnect failed", err);

        if (!_connected.Wait(timeout ?? TimeSpan.FromSeconds(10)))
        {
            throw new ICueException(
                $"iCUE SDK did not connect (state={_state}). Is iCUE running with " +
                "Settings → SDK → 'Enable SDK' turned on?");
        }

        if (ICueNative.CorsairGetSessionDetails(out var details) == CorsairError.Success)
        {
            var v = details.ServerHostVersion;
            ServerHostVersion = $"{v.Major}.{v.Minor}.{v.Patch}";
        }
        Log.Info($"iCUE SDK connected (iCUE {ServerHostVersion}).");
    }

    private void OnStateChanged(nint context, nint eventData)
    {
        if (eventData == nint.Zero)
            return;
        var evt = Marshal.PtrToStructure<CorsairSessionStateChanged>(eventData);
        _state = evt.State;
        if (evt.State == CorsairSessionState.Connected)
            _connected.Set();
        else if (evt.State is CorsairSessionState.ConnectionLost or CorsairSessionState.Closed
                 or CorsairSessionState.ConnectionRefused or CorsairSessionState.Timeout)
            _connected.Reset();
    }

    public IReadOnlyList<ICueDevice> ListDevices(CorsairDeviceType typeMask = CorsairDeviceType.All)
    {
        EnsureConnected();
        var filter = new CorsairDeviceFilter { DeviceTypeMask = (int)typeMask };
        var buffer = new CorsairDeviceInfo[ICueConstants.DeviceCountMax];
        var err = ICueNative.CorsairGetDevices(filter, buffer.Length, buffer, out int count);
        if (err != CorsairError.Success)
            throw new ICueException("CorsairGetDevices failed", err);

        var result = new List<ICueDevice>(count);
        for (int i = 0; i < count; i++)
        {
            var d = buffer[i];
            result.Add(new ICueDevice(d.Id, d.Model, d.Type, d.LedCount, d.ChannelCount));
        }
        return result;
    }

    /// <summary>Devices most useful as a mirror source: fans / coolers / LED strips / commanders.</summary>
    public IReadOnlyList<ICueDevice> ListLightingSources() => ListDevices(
        CorsairDeviceType.Cooler | CorsairDeviceType.FanLedController |
        CorsairDeviceType.LedController | CorsairDeviceType.MemoryModule);

    public CorsairLedPosition[] GetLedPositions(string deviceId)
    {
        EnsureConnected();
        var buffer = new CorsairLedPosition[ICueConstants.DeviceLedCountMax];
        var err = ICueNative.CorsairGetLedPositions(deviceId, buffer.Length, buffer, out int count);
        if (err != CorsairError.Success)
            throw new ICueException($"CorsairGetLedPositions failed for {deviceId}", err);
        return buffer[..count];
    }

    /// <summary>
    /// Reads the current colours iCUE is displaying for the given LED ids. Pass the array returned
    /// by a prior call to reuse it — only the RGBA fields are overwritten.
    /// </summary>
    public void ReadColors(string deviceId, CorsairLedColor[] leds)
    {
        EnsureConnected();
        var err = ICueNative.CorsairGetLedColors(deviceId, leds.Length, leds);
        if (err != CorsairError.Success)
            throw new ICueException($"CorsairGetLedColors failed for {deviceId}", err);
    }

    /// <summary>Builds a colour buffer seeded with the device's LED ids (from its positions).</summary>
    public CorsairLedColor[] CreateColorBuffer(CorsairLedPosition[] positions)
    {
        var buf = new CorsairLedColor[positions.Length];
        for (int i = 0; i < positions.Length; i++)
            buf[i].Id = positions[i].Id;
        return buf;
    }

    private void EnsureConnected()
    {
        if (_state != CorsairSessionState.Connected)
            throw new ICueException($"iCUE SDK not connected (state={_state}).", CorsairError.NotConnected);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ICueNative.CorsairDisconnect(); } catch { /* ignore */ }
        _connected.Dispose();
    }
}
