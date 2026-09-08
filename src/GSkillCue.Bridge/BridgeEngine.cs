using System.Diagnostics;
using GSkillCue.Core;
using GSkillCue.GSkillRam;
using GSkillCue.ICue;
using GSkillCue.Smbus;

namespace GSkillCue.Bridge;

public enum BridgeState { Stopped, Running, Paused, Faulted }

public sealed class BridgeStatusEventArgs(BridgeState state, string message) : EventArgs
{
    public BridgeState State { get; } = state;
    public string Message { get; } = message;
}

/// <summary>
/// The core loop: poll iCUE for the colours it is showing on a chosen Corsair device, map that
/// onto the DIMM LED chain, and push it to the RAM over SMBus — <c>Fps</c> times a second.
/// </summary>
public sealed class BridgeEngine : IDisposable
{
    private readonly BridgeConfig _config;
    private readonly Func<ISmbus> _smbusFactory;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile BridgeState _state = BridgeState.Stopped;

    private ISmbus? _smbus;
    private GSkillRamController? _ram;
    private ICueClient? _icue;

    private string _sourceId = "";
    private CorsairLedPosition[] _sourcePositions = [];
    private CorsairLedColor[] _sourceColors = [];
    private RgbColor[] _smoothed = [];

    public BridgeState State => _state;
    public string SourceModel { get; private set; } = "";
    public int RamLedCount => _ram?.TotalLedCount ?? 0;

    public event EventHandler<BridgeStatusEventArgs>? StatusChanged;

    public BridgeEngine(BridgeConfig config, Func<ISmbus>? smbusFactory = null)
    {
        _config = config;
        _smbusFactory = smbusFactory ?? (() => PawnIoSmbus.Open());
    }

    public void Start()
    {
        if (_state is BridgeState.Running or BridgeState.Paused)
            return;

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunLoop(_cts.Token));
    }

    public void Pause() => SetState(BridgeState.Paused, "Paused.");

    public void Resume()
    {
        if (_state == BridgeState.Paused)
            SetState(BridgeState.Running, "Resumed.");
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        ApplyExitBehavior();
        Teardown();
        SetState(BridgeState.Stopped, "Stopped.");
    }

    private async Task RunLoop(CancellationToken ct)
    {
        try
        {
            Connect(ct);
            SetState(BridgeState.Running, $"Mirroring '{SourceModel}' → {RamLedCount} DIMM LEDs.");

            var frame = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                int periodMs = Math.Max(16, 1000 / Math.Clamp(_config.Fps, 1, 60));
                frame.Restart();

                if (_state == BridgeState.Running)
                {
                    if (_config.PauseOnConflict && ConflictDetector.BlockingConflicts() is { Count: > 0 } conflicts)
                    {
                        SetState(BridgeState.Paused,
                            $"Paused — conflicting RGB software running: {string.Join(", ", conflicts.Select(c => c.Name))}.");
                    }
                    else
                    {
                        TickOnce();
                    }
                }

                int elapsed = (int)frame.ElapsedMilliseconds;
                if (elapsed < periodMs)
                    await Task.Delay(periodMs - elapsed, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Bridge loop faulted", ex);
            SetState(BridgeState.Faulted, ex.Message);
        }
    }

    private void Connect(CancellationToken ct)
    {
        _smbus = _smbusFactory();
        Log.Info($"SMBus: {_smbus.Name}");

        _ram = GSkillRamController.Detect(_smbus)
               ?? throw new InvalidOperationException(
                   "No G.Skill/ENE DRAM RGB controller found on the SMBus. Is Armoury Crate's DRAM " +
                   "lighting still enabled, or is another RGB app holding the bus?");
        Log.Info($"RAM: {_ram.Modules.Count} module(s), {_ram.TotalLedCount} LEDs total.");
        _ram.EnterDirectMode();

        _icue = new ICueClient();
        _icue.Connect(TimeSpan.FromSeconds(15));

        var sources = _icue.ListLightingSources();
        if (sources.Count == 0)
            throw new InvalidOperationException("iCUE reports no fans/coolers/LED-strips to mirror.");

        var chosen = sources.FirstOrDefault(s => s.Id == _config.SourceDeviceId) ?? sources[0];
        _sourceId = chosen.Id;
        SourceModel = chosen.Model;
        _config.SourceDeviceId = chosen.Id;
        _config.SourceDeviceModel = chosen.Model;

        _sourcePositions = _icue.GetLedPositions(_sourceId);
        _sourceColors = _icue.CreateColorBuffer(_sourcePositions);
        _smoothed = new RgbColor[_ram.TotalLedCount];
        ct.ThrowIfCancellationRequested();
    }

    private void TickOnce()
    {
        if (_icue is null || _ram is null)
            return;

        _icue.ReadColors(_sourceId, _sourceColors);

        var src = new SourceLed[_sourceColors.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var c = _sourceColors[i];
            src[i] = new SourceLed(new RgbColor(c.R, c.G, c.B), _sourcePositions[i].Cx);
        }

        var target = ColorMapping.Map(src, _ram.TotalLedCount, _config.MappingMode, _ram.Modules.Count);

        double keep = _config.Smoothing;
        for (int i = 0; i < target.Length; i++)
        {
            var shaped = target[i].Gamma(_config.Gamma).Scale(_config.Brightness);
            _smoothed[i] = keep <= 0.001 ? shaped : RgbColor.Lerp(_smoothed[i], shaped, 1.0 - keep);
        }

        _ram.SetColors(_smoothed);
    }

    private void ApplyExitBehavior()
    {
        if (_ram is null)
            return;
        try
        {
            switch (_config.ExitBehavior)
            {
                case ExitBehavior.HoldLastFrame:
                    _ram.Restore(1 /* ENE_MODE_STATIC, seeded with the last frame */);
                    break;
                case ExitBehavior.TurnOff:
                    _ram.SetAll(RgbColor.Black);
                    _ram.Restore(0 /* ENE_MODE_OFF */);
                    break;
                case ExitBehavior.StaticColor:
                    _ram.SetAll(_config.StaticColorRgb);
                    _ram.Restore(1 /* ENE_MODE_STATIC */);
                    break;
            }
        }
        catch (Exception ex) { Log.Warn($"Exit behavior failed: {ex.Message}"); }
    }

    private void Teardown()
    {
        _icue?.Dispose();
        _icue = null;
        (_smbus as IDisposable)?.Dispose();
        _smbus = null;
        _ram = null;
    }

    private void SetState(BridgeState state, string message)
    {
        _state = state;
        Log.Info($"[bridge] {message}");
        StatusChanged?.Invoke(this, new BridgeStatusEventArgs(state, message));
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
