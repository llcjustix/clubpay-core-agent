using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Uses Windows' built-in SAPI through PowerShell. Failure to play a voice prompt must never affect
/// the session timer or payment flow, so unavailable voices are logged and ignored.
/// </summary>
public sealed class VoiceAnnouncementService(
    IConfiguration config,
    LocalizationService localizer,
    ILogger<VoiceAnnouncementService> logger)
    : IVoiceAnnouncementService
{
    private readonly bool _enabled = config.GetValue("Agent:VoiceAnnouncementsEnabled", true);

    public async Task AnnounceRemainingTimeAsync(int remainingSeconds, CancellationToken ct = default)
    {
        if (!_enabled || ct.IsCancellationRequested)
            return;

        var text = remainingSeconds switch
        {
            1800 => localizer["TimeLeft30"],
            600 => localizer["TimeLeft10"],
            300 => localizer["TimeLeft5"],
            _ => localizer.Format("TimeLeftGeneric", Math.Max(1, remainingSeconds / 60))
        };

        try
        {
            // Base64 avoids interpolating user-controlled text into a PowerShell command.
            var encodedText = Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
            var script = "Add-Type -AssemblyName System.Speech; " +
                         "$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                         "try { $voice.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::NotSet, " +
                         "[System.Speech.Synthesis.VoiceAge]::NotSet, 0, " +
                         "[System.Globalization.CultureInfo]'" + localizer.CultureName + "') } catch {}; " +
                         "$voice.Speak([System.Text.Encoding]::Unicode.GetString(" +
                         "[System.Convert]::FromBase64String('" + encodedText + "')));";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add(script);
            process.Start();
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var error = await errorTask;
            if (process.ExitCode != 0)
                logger.LogWarning("Session time announcement failed with exit code {ExitCode}: {Error}",
                    process.ExitCode, error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // App shutdown must not be held up by an in-flight voice prompt.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not play the session time announcement");
        }
    }
}
