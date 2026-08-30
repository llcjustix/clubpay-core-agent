# ClubPay Agent — MVP setup and acceptance check

This application is the Windows-side Agent. It connects **outbound** to ClubPay Core; it does not
run a local Controller or expose an inbound port.

## One-time configuration per PC

1. Publish the Agent build on a Windows PC/VM.
2. Copy `appsettings.Local.example.json` to `appsettings.Local.json` beside the executable.
3. Set a unique `Controller:ExternalPcId` and the matching `Controller:AgentToken` (`CORE_TOKEN`).
   Do not commit this file or send its value in chat.
4. Keep the production endpoints from `appsettings.json` unless DevOps supplies staging endpoints:
   - WSS: `wss://api-clubpay.justix.uz/api/core/ws`
   - bootstrap: `https://api-clubpay.justix.uz/api/core/bootstrap`
5. Start the Agent. It first opens WSS and asks Core bootstrap for the static public QR URL. The
   static QR must never be assembled from `external_pc_id` locally.

## Shared diskless Windows image

For a diskless club, install the executable once in the master Windows image. Do **not** bake one
fixed `ExternalPcId` or one shared state file into that image: all clients would otherwise connect
as the same ClubPay PC.

Give every Windows client a unique computer name of at most 15 characters, for example `CP001`,
`CP002`. In ClubPay admin create the matching PCs with external IDs `cp001`, `cp002`. In the
shared `appsettings.Local.json` use the supported placeholders:

```json
{
  "Agent": {
    "PcId": "PC {MACHINE_NAME}",
    "DataDirectory": "C:\\ProgramData\\ClubPay\\Agent\\state\\{MACHINE_NAME_LOWER}"
  },
  "Controller": {
    "ExternalPcId": "{MACHINE_NAME_LOWER}",
    "AgentToken": "PUT_CORE_TOKEN_HERE"
  }
}
```

The Agent expands `{MACHINE_NAME}`, `{MACHINE_NAME_LOWER}` and `{MACHINE_NAME_UPPER}` when it
starts. The diskless system must keep these per-client state folders separate and writable; they
contain active-session recovery and undelivered event data.

## Automatic startup on a club PC

The Agent must **not** be started manually on a club PC. The one-time installation below registers
it in Windows Startup and configures automatic login for the dedicated kiosk Windows account:

```bat
ClubPay.Agent.Client.exe --setup-kiosk=kiosk:YOUR_WINDOWS_PASSWORD
```

Run this command once from an **Administrator** Command Prompt. It copies the build to
`C:\ClubPay\Agent`, registers the installed copy in Windows Startup, and configures auto-login.
Then reboot the PC. After reboot, the kiosk account should log in and Agent Core should appear
without any manual action.

`--setup-kiosk` deliberately does not replace `explorer.exe`. Agent Core hides Windows while it is
running and the maintenance workflow can safely restore it. Do not use `--shell` for the pilot.

For a staging VM, use `--install` only if you explicitly want to keep manual Windows login. Do
**not** enable `--shell` or `--autologin` while testing without a dedicated kiosk account.

## Maintenance mode for a test VM

The production default is a locked kiosk: Task Manager and common Windows escape shortcuts are
blocked. Do not weaken that setting on real club PCs.

For a development VM only, add the following to `appsettings.Local.json` next to the executable:

```json
"Agent": {
  "KioskLockdownEnabled": false,
  "MaintenanceExitEnabled": true
}
```

Restart the Agent after changing the file. In this mode, **Ctrl+Shift+F12** closes the Agent
cleanly and returns to Windows, including removal of any kiosk policy left by a previous run.
`appsettings.Local.json` must never be copied to a real club PC with those values.

## MVP acceptance flow

1. Agent connects: Core shows the PC as online/free; its lock screen displays the static QR.
2. Scan the static QR and pay/apply a voucher: Core sends `start_session`; Agent unlocks and starts
   the timer.
3. On the active session screen, scan the **dynamic QR from `extend_url`** carried in
   `start_session`. Payment/voucher must result in `extend_session` with a new `grant_id`; the timer
   increases only once.
4. Scan the static QR while the PC is busy: Core must reject new payment/session creation.
5. Let the timer expire: the Agent enters FROZEN for the configured grace period and shows the same
   dynamic extension QR. A successful extension restores the active session.
6. During an active session, open the `⋮` menu in the overlay and choose **Завершить сеанс**. Enter
   the phone number and confirm Telegram consent. Core instructs the Agent to end the session,
   locks the PC and creates a voucher for the unused seconds — exactly as when a manager ends a
   session in the admin panel. If this number is already linked to the Telegram bot, the voucher is
   sent immediately. Otherwise the Agent displays a Telegram QR: the player opens the bot and taps
   Start, after which Core automatically sends the pending voucher.
7. After the session has ended, its dynamic extension QR is no longer accepted by Core.
8. Restart the Agent: it reconnects and recovers persisted active session state. A real sleeping PC
   is awakened by Controller/DevOps via Wake-on-LAN; once Windows starts, the Agent reconnects.

Core is the only source of the FROZEN grace duration: it sends `grace_seconds` with `start_session`.
The Agent persists and applies that value for the whole session. The current default is 180 seconds.
The Agent also announces 30, 10 and 5 minutes remaining through the Windows voice service; this can
be disabled per PC with `Agent:VoiceAnnouncementsEnabled`.

The real club-PC kiosk hides Windows controls. Do not expose Windows settings or Task Manager to a
player. The overlay menu is deliberately limited to player actions; club staff can manage Windows
through their separate maintenance workflow.

## Required environment, outside the Agent repository

- Windows VM or physical Windows PC with outbound HTTPS/WSS port 443 to Core.
- A matching PC record, `CORE_TOKEN`, database migrations, WSS reverse proxy, and payment-provider
  production credentials are Core/DevOps responsibilities.
- Wake-on-LAN needs a controller on the club LAN and the PC MAC address. A VM can verify reconnect
  behavior but cannot prove physical Wake-on-LAN.
