using System.Diagnostics;
using System.Text;
using ClubPay.Agent.Core.Contracts.Payloads;

namespace ClubPay.Agent.Client.Services;

public interface IAgentUpdateService
{
    AgentUpdateResult Schedule(AgentUpdatePayload payload);
}

public sealed record AgentUpdateResult(string Status, string Version);

/// <summary>
/// Creates a detached, checksum-verifying Windows helper. The Controller gets
/// its command ACK before this helper stops the Agent process, so a restart
/// cannot be mistaken for a failed update command.
/// </summary>
public sealed class AgentUpdateService : IAgentUpdateService
{
    private static readonly object Gate = new();
    private static string? _scheduledVersion;

    public AgentUpdateResult Schedule(AgentUpdatePayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Version) ||
            !Uri.TryCreate(payload.DownloadUrl, UriKind.Absolute, out var archiveUri) || archiveUri.Scheme != Uri.UriSchemeHttps ||
            !Uri.TryCreate(payload.ChecksumUrl, UriKind.Absolute, out var checksumUri) || checksumUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("invalid Agent update artifact");

        lock (Gate)
        {
            if (string.Equals(NormalizeVersion(payload.Version), CurrentVersion(), StringComparison.Ordinal))
                return new AgentUpdateResult("current", payload.Version);

            if (string.Equals(_scheduledVersion, payload.Version, StringComparison.Ordinal))
                return new AgentUpdateResult("already_scheduled", payload.Version);

            var updatesDirectory = Path.Combine(Path.GetTempPath(), "ClubPay", "updates");
            Directory.CreateDirectory(updatesDirectory);
            var scriptPath = Path.Combine(updatesDirectory, "agent-" + SafeFilePart(payload.Version) + ".ps1");
            File.WriteAllText(scriptPath, Script(payload), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
            });
            _scheduledVersion = payload.Version;
            return new AgentUpdateResult("scheduled", payload.Version);
        }
    }

    private static string Script(AgentUpdatePayload payload) => $@"
$ErrorActionPreference = 'Stop'
Start-Sleep -Seconds 5
$stage = Join-Path $env:TEMP ('clubpay-agent-update-' + [guid]::NewGuid().ToString())
$zip = Join-Path $env:TEMP ('clubpay-agent-update-' + [guid]::NewGuid().ToString() + '.zip')
$checksum = Join-Path $env:TEMP ('clubpay-agent-update-' + [guid]::NewGuid().ToString() + '.sha256')
try {{
  New-Item -ItemType Directory -Path $stage -Force | Out-Null
  Invoke-WebRequest -Uri '{PowerShellLiteral(payload.DownloadUrl)}' -OutFile $zip -UseBasicParsing
  Invoke-WebRequest -Uri '{PowerShellLiteral(payload.ChecksumUrl)}' -OutFile $checksum -UseBasicParsing
  $expected = ((Get-Content -LiteralPath $checksum -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
  $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
  if ($expected -notmatch '^[a-f0-9]{{64}}$' -or $actual -ne $expected) {{ throw 'ClubPay Agent release checksum verification failed.' }}
  Expand-Archive -Path $zip -DestinationPath $stage -Force
  $updater = Get-ChildItem -Path $stage -Filter 'update-agent.ps1' -File -Recurse | Select-Object -First 1
  if ($null -eq $updater) {{ throw 'ClubPay Agent release does not contain its updater.' }}
  & $updater.FullName -NoPrompt
}}
finally {{
  Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $checksum -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}}";

    private static string PowerShellLiteral(string value) => value.Replace("'", string.Empty, StringComparison.Ordinal);

    private static string CurrentVersion()
    {
        var version = typeof(AgentUpdateService).Assembly.GetName().Version;
        if (version is null || version.Major < 0 || version.Minor < 0 || version.Build < 0)
            return string.Empty;
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V');

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '-' : ch));
    }
}
