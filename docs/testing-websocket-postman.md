# WebSocket to'liq funksionallikni Postman orqali test qilish

Bu hujjat ClubPay Agent ↔ Controller WebSocket kontraktini (start/extend/end session, lock/unlock,
sleep/wake, ta'mirlash rejimi, status) Postman yordamida qanday sinash mumkinligini tushuntiradi.

## 1. Muhim arxitektura eslatmasi — avval shuni o'qing

Kontrakt (Billing/Core/Agent Contract v1.2, §1) talabiga ko'ra **Agent statik IP-manzilga ega emas**,
shuning uchun u hech qachon kiruvchi ulanish qabul qilmaydi — **har doim o'zi Controllerga chiqib
ulanadi** (outbound WebSocket). Bu shuni anglatadiki:

- Postman'ning "WebSocket Request" funksiyasi **faqat klient** — u serverga ulanadi, lekin o'zi server
  bo'lib boshqa birovning ulanishini kuta olmaydi.
- Demak, **Postman Agentga to'g'ridan-to'g'ri ulana olmaydi** (Agent hech qayerda "tinglamaydi").
- Haqiqiy Agent'ni sinash uchun **kimdir server bo'lishi kerak** — bu vazifani loyihadagi tayyor
  `tools/MockController` konsol vositasi bajaradi.

Shu sababli quyida ikkita alohida stsenariy berilgan:

| Stsenariy | Nima sinaladi | Postman ishtirok etadimi |
|---|---|---|
| **1-stsenariy** (tavsiya etiladi) | Haqiqiy `ClubPay.Agent.Client.exe`ning to'liq ishlashi (kiosk, sessiya, kilos-lock va h.k.) | Yo'q — `tools/MockController` server rolini o'ynaydi |
| **2-stsenariy** | Faqat Controller↔Agent **kontraktining** (JSON format, maydon nomlari, buyruq/hodisa oqimi) to'g'riligi | Ha — Postman "Agent" o'rnini bosadi (simulyatsiya qiladi) |

Ikkalasini **bir vaqtda** ishlatib bo'lmaydi: `tools/MockController` bir paytda faqat bitta ulanishni
"joriy agent" deb hisoblaydi. Postman ulanган bo'lsa, haqiqiy `Agent.exe` ulana olmaydi (va aksincha).

---

## 2. Tayyorgarlik

```bash
cd C:\Users\diyor\Pictures\Justix\clubpay-core-agent
dotnet build ClubPay.Agent.slnx
```

`tools/MockController`ni ishga tushiring:

```bash
dotnet run --project tools/MockController
```

Konsolda shunga o'xshash chiqish ko'rinadi:

```
ClubPay Mock Controller
WebSocket URL: ws://localhost:52984/agent/ws
Agent'ning appsettings.json faylida Controller:WebSocketUrl shu manzilga ko'rsatilishi kerak.
Agent ulanishini kutmoqda (Ctrl+C — chiqish)...
```

Port har safar tasodifiy tanlanadi — shuni nusxalab oling (masalan `ws://localhost:52984/agent/ws`).

---

## 3. 1-stsenariy: haqiqiy Agent.exe'ni MockController orqali sinash (tavsiya etiladi)

1. `src/ClubPay.Agent.Client/bin/Debug/net10.0-windows/appsettings.json` faylini oching (yoki manba
   `src/ClubPay.Agent.Client/appsettings.json`ni tahrirlab qayta build qiling) va `Controller` bo'limini
   yangilang:

   ```json
   "Controller": {
     "WebSocketUrl": "ws://localhost:52984/agent/ws",
     "AgentToken": "change-me-in-production",
     "ExternalPcId": "club12-pc07"
   }
   ```

2. `ClubPay.Agent.Client.exe`ni ishga tushiring.
   > ⚠️ Diqqat: bu kiosk-rejim — to'liq ekranli oyna ochiladi, Win-tugma va Task Manager vaqtincha
   > bloklanadi (`KioskLockService`). Sinov tugagach ilovani to'g'ri yopib chiqing.

3. MockController konsolida `Agent ulandi.` xabari va `[EVENT] agent_online — ...` ko'rinishi kerak.

4. Endi MockController'ning o'zida buyruqlarni yozing (u ichkarida `command` yuboradi va
   `command_result`/hodisalarni chop etadi):

   | Buyruq | Misol | Nima qiladi |
   |---|---|---|
   | `status` | `status` | `get_status` yuboradi |
   | `start [soniya]` | `start 3600` | `start_session` — 1 soatlik sessiya boshlaydi |
   | `extend <core_session_id> [soniya]` | `extend 8ce3e711... 1800` | `extend_session` — 30 daqiqa qo'shadi |
   | `lock [sabab]` | `lock manager-check` | `lock` — sessiyani o'zgartirmasdan PCni bloklaydi |
   | `unlock [sabab]` | `unlock` | `unlock` |
   | `sleep` | `sleep` | faqat `Locked` holatda ishlaydi (aks holda `pc_busy` xatosi) |
   | `wake` | `wake` | deyarli no-op (izoh: haqiqiy uyg'otish tarmoq darajasida WoL orqali bo'ladi) |
   | `repair on\|off` | `repair on` | `set_repair` — yoqilganda yangi sessiya boshlash rad etiladi |
   | `end <core_session_id> [sabab]` | `end 8ce3e711... MANAGER` | `end_session` (sabab: `TIME_UP\|MANAGER\|REFUND\|CLIENT_LEFT\|ERROR`) |

   `start`dan qaytgan `core_session_id`ni keyingi `extend`/`end` buyruqlarida ishlating.

5. Kutilgan natija namunasi (haqiqiy loyihada tekshirilgan):

   ```
   [COMMAND_RESULT] start_session -> status=ok, payload={"core_session_id":"8ce3e711d48942b98df7b58cb1a67dde","remaining_seconds":3600}
   [EVENT] session_started — {"core_session_id":"...","external_pc_id":"club12-pc07","grant_id":"grant_...","started_at":"..."}
   [EVENT] pc_state_changed — {"external_pc_id":"club12-pc07","pc_state":"OCCUPIED","online":true}
   ```

---

## 4. 2-stsenariy: Postman orqali kontraktni (wire-protokolni) qo'lda sinash

Bu yerda **Postman Agent o'rnini bosadi** — `tools/MockController`ga ulanib, haqiqiy Agent qanday
xabar yuborishi/qabul qilishi kerakligini qo'lda takrorlaysiz. Bu haqiqiy Agent kodini emas, balki
**JSON format va oqimni** tekshirish uchun.

### 4.1. Ulanish

Postman'da: **New → WebSocket Request**

- **URL**: `ws://localhost:52984/agent/ws?external_pc_id=club12-pc07` (portni MockController chiqargan
  qiymatga almashtiring)
- **Headers** bo'limida qo'shing: `Authorization: Bearer change-me-in-production`
- **Connect** tugmasini bosing.

Ulangach, MockController konsolida `Agent ulandi.` ko'rinadi.

### 4.2. Birinchi xabar — `agent_online` hodisasi

Real Agent ulangan zahoti shu hodisani yuboradi. Postman'ning message maydoniga yozib **Send** qiling:

```json
{
  "type": "event",
  "name": "agent_online",
  "event_id": "ev_0001",
  "ts": "2026-07-10T12:00:00Z",
  "payload": { "external_pc_id": "club12-pc07" }
}
```

### 4.3. Buyruqlarni qabul qilish va javob berish

MockController konsolida buyruq yuboring (masalan `start 3600`). Postman'ning "Messages" oynasida
shunga o'xshash **kiruvchi** xabar paydo bo'ladi:

```json
{
  "type": "command",
  "name": "start_session",
  "command_id": "cmd_a1b2c3d4",
  "ts": "2026-07-10T12:00:01Z",
  "payload": {
    "external_pc_id": "club12-pc07",
    "grant_id": "grant_1a2b3c4d",
    "granted_seconds": 3600,
    "grace_seconds": 180,
    "ends_at": "2026-07-10T13:00:01Z",
    "zone": "Standard",
    "start_at": "2026-07-10T12:00:01Z"
  }
}
```

Shunga javoban Postman'dan ("Agent" sifatida) quyidagini **Send** qiling — `command_id` kelgan
buyruqdagi bilan bir xil bo'lishi shart:

```json
{
  "type": "command_result",
  "command_id": "cmd_a1b2c3d4",
  "status": "ok",
  "payload": { "core_session_id": "8ce3e711d48942b98df7b58cb1a67dde", "remaining_seconds": 3600 }
}
```

Va sessiya boshlanganini bildiruvchi hodisalarni ham yuboring:

```json
{
  "type": "event",
  "name": "session_started",
  "event_id": "ev_0002",
  "ts": "2026-07-10T12:00:01Z",
  "payload": {
    "core_session_id": "8ce3e711d48942b98df7b58cb1a67dde",
    "external_pc_id": "club12-pc07",
    "grant_id": "grant_1a2b3c4d",
    "payment_order_id": null,
    "started_at": "2026-07-10T12:00:01Z"
  }
}
```

```json
{
  "type": "event",
  "name": "pc_state_changed",
  "event_id": "ev_0003",
  "ts": "2026-07-10T12:00:01Z",
  "payload": { "external_pc_id": "club12-pc07", "pc_state": "OCCUPIED", "online": true }
}
```

### 4.4. Har bir buyruq uchun to'liq namunalar

Har birida `payload` maydoni — Controller yuboradigan qism; undan keyingi bloklar — Agent (Postman)
qaytarishi kerak bo'lgan `command_result` va (agar bo'lsa) hodisalar.

#### `extend_session`

```json
{ "type": "command", "name": "extend_session", "command_id": "cmd_e1", "ts": "...Z",
  "payload": {
    "core_session_id": "8ce3e711d48942b98df7b58cb1a67dde",
    "grant_id": "grant_2b3c4d5e",
    "payment_order_id": null,
    "added_seconds": 1800
  } }
```

Javob:
```json
{ "type": "command_result", "command_id": "cmd_e1", "status": "ok",
  "payload": { "remaining_seconds": 5400 } }
```
Hodisa: `session_extended` — `{ "core_session_id", "external_pc_id", "grant_id", "added_seconds", "ends_at" }`
va `pc_state_changed`.

#### `end_session`

```json
{ "type": "command", "name": "end_session", "command_id": "cmd_e2", "ts": "...Z",
  "payload": { "core_session_id": "8ce3e711d48942b98df7b58cb1a67dde", "reason": "MANAGER" } }
```
(`reason`: `TIME_UP` | `MANAGER` | `REFUND` | `CLIENT_LEFT` | `ERROR`)

Javob:
```json
{ "type": "command_result", "command_id": "cmd_e2", "status": "ok",
  "payload": { "consumed_seconds": 5312, "remaining_seconds": 88 } }
```
Hodisalar: `session_ended` — `{ "core_session_id", "external_pc_id", "reason", "consumed_seconds", "remaining_seconds" }`
va `pc_state_changed` (endi `pc_state: "FREE"`).

#### `lock` / `unlock`

```json
{ "type": "command", "name": "lock", "command_id": "cmd_l1", "ts": "...Z",
  "payload": { "external_pc_id": "club12-pc07", "reason": "manager-check" } }
```
Javob: `{ "type": "command_result", "command_id": "cmd_l1", "status": "ok", "payload": {} }`
(`unlock` xuddi shunday, `payload`da `reason` ixtiyoriy). **Diqqat**: lock/unlock sessiya holatini
o'zgartirmaydi — agar sessiya `Active` bo'lsa, keyingi `pc_state_changed`da `pc_state` baribir
`OCCUPIED` bo'lib qoladi.

#### `wake` / `sleep`

```json
{ "type": "command", "name": "sleep", "command_id": "cmd_s1", "ts": "...Z",
  "payload": { "external_pc_id": "club12-pc07" } }
```
Javob (faqat `Locked` holatda `ok`; aks holda xato — pastdagi jadvalga qarang):
`{ "type": "command_result", "command_id": "cmd_s1", "status": "ok", "payload": {} }`

`wake` xuddi shu shaklda, lekin har doim darhol `ok` qaytaradi — real uyg'otish tarmoq darajasidagi
Wake-on-LAN orqali, Agent kodidan tashqarida amalga oshadi.

#### `set_repair`

```json
{ "type": "command", "name": "set_repair", "command_id": "cmd_r1", "ts": "...Z",
  "payload": { "external_pc_id": "club12-pc07", "on": true } }
```
Javob: `{ "type": "command_result", "command_id": "cmd_r1", "status": "ok", "payload": {} }`
Hodisa: `pc_state_changed` → `pc_state: "REPAIR"`. Shu holatda `start_session` yuborilsa,
`status: "error", error_code: "pc_in_repair"` qaytishi kerak.

#### `get_status`

```json
{ "type": "command", "name": "get_status", "command_id": "cmd_g1", "ts": "...Z",
  "payload": { "external_pc_id": "club12-pc07" } }
```
Javob (sessiyasiz holatda):
```json
{ "type": "command_result", "command_id": "cmd_g1", "status": "ok",
  "payload": { "pc_state": "FREE", "session_state": null, "core_session_id": null, "remaining_seconds": null } }
```
Faol sessiyada: `pc_state: "OCCUPIED"`, `session_state: "ACTIVE"`, `core_session_id`/`remaining_seconds` to'ldirilgan.

### 4.5. Idempotentlikni sinash (grant_id takrori)

Bir xil `grant_id` bilan `start_session`ni ikkinchi marta yuboring (masalan avvalgi 4.3-qismdagi
`grant_1a2b3c4d` bilan, boshqa `command_id` bilan). Kutilgan javob — vaqt qo'shilmaydi, faqat joriy
holat qaytadi:
```json
{ "type": "command_result", "command_id": "cmd_dup1", "status": "ok", "error_code": "duplicate",
  "payload": { "core_session_id": "8ce3e711d48942b98df7b58cb1a67dde", "remaining_seconds": 3600 } }
```
Bunda yangi `session_started` hodisasi **yuborilmasligi** kerak.

### 4.6. `error_code` jadvali

| `error_code` | Qachon qaytadi |
|---|---|
| `pc_busy` | `start_session` — PC allaqachon band (sessiya `Active`/`Frozen`); `sleep` — sessiya faol paytda |
| `pc_in_repair` | `start_session` — `set_repair(on:true)` yoqilgan bo'lsa |
| `invalid_state` | `extend_session`/`end_session` — mos sessiya yo'q; noma'lum buyruq nomi; noto'g'ri/yo'q payload |
| `duplicate` | `start_session`/`extend_session` — `grant_id` allaqachon qo'llanilgan (status baribir `ok`) |
| `internal_error` | kutilmagan ichki xato |
| `agent_offline`, `command_timeout`, `wol_failed` | Controller/tarmoq tomonidagi holatlar — Agent hech qachon bularni o'zi qaytarmaydi |

Xato javob namunasi:
```json
{ "type": "command_result", "command_id": "cmd_x1", "status": "error",
  "error_code": "pc_busy", "message": "pc already has an active session" }
```

Shu bilan birga `command_failed` hodisasi ham yuborilishi mumkin:
```json
{ "type": "event", "name": "command_failed", "event_id": "ev_00xx", "ts": "...Z",
  "payload": { "command_id": "cmd_x1", "external_pc_id": "club12-pc07", "error_code": "pc_busy" } }
```

### 4.7. `time_low` va `heartbeat` (Agent o'zi, buyruqsiz yuboradigan hodisalar)

Real Agentda bular ichki taymer orqali avtomatik yuboriladi — Postman orqali sinaganda ularni ham
qo'lda yuborib ko'rishingiz mumkin (shunchaki formatni tekshirish uchun):

```json
{ "type": "event", "name": "time_low", "event_id": "ev_00a1", "ts": "...Z",
  "payload": { "core_session_id": "8ce3e711d48942b98df7b58cb1a67dde", "remaining_seconds": 300, "threshold": 300 } }
```
(`threshold`: 600 / 300 / 60 — 10/5/1 daqiqa chegaralari, har biri sessiyada bir marta)

```json
{ "type": "event", "name": "heartbeat", "event_id": "ev_00a2", "ts": "...Z",
  "payload": { "external_pc_id": "club12-pc07", "pc_state": "OCCUPIED", "controllers_seen": 1, "server_reachable": true } }
```

---

## 5. Havolalar — kodni chuqurroq o'rganish uchun

| Nima | Fayl |
|---|---|
| Buyruq/hodisa nomlari, taymer/reconnect konstantalari | `src/ClubPay.Agent.Core/Constants.cs` (`ControllerChannel` klassi) |
| Buyruq payload'lari | `src/ClubPay.Agent.Core/Contracts/Payloads/*.cs` |
| Hodisa payload'lari | `src/ClubPay.Agent.Core/Contracts/Events/ControllerEvents.cs` |
| `pc_state`/`session_state`/`end_reason`/`error_code` qiymatlari | `src/ClubPay.Agent.Core/Contracts/Enums/*.cs` |
| Buyruqni qabul qilish/javob berish mantig'i | `src/ClubPay.Agent.Client/Services/CommandDispatcherService.cs` |
| Sessiya/holat biznes-mantig'i | `src/ClubPay.Agent.Client/Services/SessionCoordinatorService.cs` |
| WebSocket ulanish/reconnect | `src/ClubPay.Agent.Client/Services/ControllerChannelService.cs` |
| Mock-server (Controller o'rnini bosadi) | `tests/ClubPay.Agent.TestHarness/FakeControllerServer.cs` |
| Konsol vositasi | `tools/MockController/Program.cs` |
| Agent konfiguratsiyasi | `src/ClubPay.Agent.Client/appsettings.json` (`Controller:*`) |
