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

For a staging VM, use `--install` only. Do **not** enable `--shell` or `--autologin` while testing.

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
6. End the session: its dynamic extension QR is no longer accepted by Core.
7. Restart the Agent: it reconnects and recovers persisted active session state. A real sleeping PC
   is awakened by Controller/DevOps via Wake-on-LAN; once Windows starts, the Agent reconnects.

Core is the only source of the FROZEN grace duration: it sends `grace_seconds` with `start_session`.
The Agent persists and applies that value for the whole session. The current default is 180 seconds.
The Agent also announces 30, 10 and 5 minutes remaining through the Windows voice service; this can
be disabled per PC with `Agent:VoiceAnnouncementsEnabled`.

## Required environment, outside the Agent repository

- Windows VM or physical Windows PC with outbound HTTPS/WSS port 443 to Core.
- A matching PC record, `CORE_TOKEN`, database migrations, WSS reverse proxy, and payment-provider
  production credentials are Core/DevOps responsibilities.
- Wake-on-LAN needs a controller on the club LAN and the PC MAC address. A VM can verify reconnect
  behavior but cannot prove physical Wake-on-LAN.
