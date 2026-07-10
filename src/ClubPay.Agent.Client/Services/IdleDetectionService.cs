using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

/// <summary>Polls GetLastInputInfo periodically so idle→sleep reflects genuine keyboard/mouse
/// inactivity (ТЗ §8), replacing the previous flat "always sleep after N minutes locked" timer.</summary>
public sealed class IdleDetectionService : IIdleDetectionService
{
    private const int PollIntervalMs = 5000;

    private readonly ILogger<IdleDetectionService> _logger;
    private Timer? _timer;
    private bool _thresholdFired;

    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromSeconds(Constants.Timer.IdleSleep);

    public event Action? IdleThresholdReached;

    public IdleDetectionService(ILogger<IdleDetectionService> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        _thresholdFired = false;
        _timer = new Timer(_ => Poll(), null, PollIntervalMs, PollIntervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    private void Poll()
    {
        try
        {
            var idleFor = GetIdleDuration();
            if (idleFor < IdleThreshold)
            {
                _thresholdFired = false;
                return;
            }

            if (_thresholdFired)
                return;

            _thresholdFired = true;
            IdleThresholdReached?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idle-holatni aniqlab bo'lmadi");
        }
    }

    private static TimeSpan GetIdleDuration()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;

        // Both sides are 32-bit tick counts (ms since boot); unchecked subtraction wraps correctly
        // modulo 2^32 even across Environment.TickCount's ~49.7-day rollover.
        uint idleMs = unchecked((uint)Environment.TickCount - info.DwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);
}
