using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Services.Controller;

/// <summary>Pure presentation mapping from wire-level <see cref="PcLiveState"/> to the
/// UI-facing <see cref="PcCard"/> fields (PcStatus/StatusText/TimeRemaining) — the reverse of
/// <c>Core.Contracts.PcStateMapper</c>, but this direction is Admin-only display formatting
/// (Uzbek labels), not part of the wire contract, so it lives here rather than in Core.</summary>
public static class PcCardMapper
{
    public static void Apply(PcCard card, PcLiveState state)
    {
        card.Status = ToUiStatus(state.PcState);
        card.StatusText = BuildStatusText(state);
        card.TimeRemaining = card.StatusText;
    }

    private static PcStatus ToUiStatus(PcState wireState) => wireState switch
    {
        PcState.Offline => PcStatus.Offline,
        PcState.Free => PcStatus.Free,
        PcState.Occupied => PcStatus.Active,
        PcState.Frozen => PcStatus.Frozen,
        PcState.Sleeping => PcStatus.Sleeping,
        PcState.Repair => PcStatus.Repair,
        PcState.Attention => PcStatus.Attention,
        _ => PcStatus.Offline,
    };

    private static string BuildStatusText(PcLiveState state) => state.PcState switch
    {
        PcState.Offline => "Offline",
        PcState.Free => "Bo'sh",
        PcState.Sleeping => "Uxlayapti",
        PcState.Repair => "Remont",
        PcState.Attention => "Diqqat",
        PcState.Occupied when state.RemainingSeconds is > 0 => $"{FormatRemaining(state.RemainingSeconds.Value)} qoldi",
        PcState.Occupied => "Faol",
        PcState.Frozen when state.RemainingSeconds is > 0 => $"{FormatRemaining(state.RemainingSeconds.Value)} qoldi",
        PcState.Frozen => "To'lov kutilmoqda",
        _ => "Noma'lum",
    };

    private static string FormatRemaining(int seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm");
}
