using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Uses Windows' built-in SAPI through PowerShell. Failure to play a voice prompt must never affect
/// the session timer or payment flow, so unavailable voices are logged and ignored.
/// </summary>
public sealed class VoiceAnnouncementService(IConfiguration config, ILogger<VoiceAnnouncementService> logger)
    : IVoiceAnnouncementService
{
    private readonly bool _enabled = config.GetValue("Agent:VoiceAnnouncementsEnabled", true);

    public Task AnnounceRemainingTimeAsync(int remainingSeconds, CancellationToken ct = default)
    {
        if (!_enabled || ct.IsCancellationRequested)
            return Task.CompletedTask;

        var text = remainingSeconds switch
        {
            1800 => "До окончания сессии осталось тридцать минут.",
            600 => "До окончания сессии осталось десять минут.",
            300 => "До окончания сессии осталось пять минут.",
            _ => $"До окончания сессии осталось {Math.Max(1, remainingSeconds / 60)} минут."
        };

        try
        {
            // Base64 avoids interpolating user-controlled text into a PowerShell command.
            var encodedText = Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
            var script = "Add-Type -AssemblyName System.Speech; " +
                         "$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                         "try { $voice.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::NotSet, " +
                         "[System.Speech.Synthesis.VoiceAge]::NotSet, 0, " +
                         "[System.Globalization.CultureInfo]'ru-RU') } catch {}; " +
                         "$voice.Speak([System.Text.Encoding]::Unicode.GetString(" +
                         "[System.Convert]::FromBase64String('" + encodedText + "')));";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add(script);
            process.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not play the session time announcement");
        }

        return Task.CompletedTask;
    }
}
