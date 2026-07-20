# ClubPay Agent — to'liq biznes flow va test etaplari

`docs/_Clubpay - ТЗ.docx` (v5.4 + Billing/Core kontrakt v1.2) va joriy koddan tasdiqlangan holat
asosida (2026-07-14 holatiga ko'ra).

## 1. Umumiy tsikl (mijoz nuqtai nazaridan)

```
Mijoz keladi → bo'sh PC tanlaydi → LockScreen'dagi QR ni skanerlaydi
    → Billing'da to'lov (Payme/Click) → to'lov tasdiqlanadi
    → Billing → Controller → Agent: start_session komandasi
    → Agent: KioskLock ochiladi (Full → Session), GameLauncher ko'rsatiladi
    → Mijoz o'ynaydi (Active holat, vaqt kamayadi)
    → [ixtiyoriy] uzaytirish QR → extend_session
    → Vaqt tugaydi (yoki menejer/mijoz tugatadi) → end_session
    → Agent: KillRunningApp → KillSessionProcesses → KioskLock.Full → idle timer → Sleep
```

## 2. Tizim zanjiri (kim kimga gapiradi)

```
Billing (server, Payme/Click, Telegram bot)
     ↕  (HTTP, sinxron ack yoki polling — T0/T1 rejimga qarab)
Controller (Raspberry Pi-1 → Pi-2 zaxira → T2 da menejer PC)
     ↕  (WebSocket, Agent TASHQARIGA chiquvchi ulanish ochadi — Reachability shu bilan yechilgan)
Agent = SHU REPO (ClubPay.Agent.Client, kiosk PC'da)
```

Agent hech qachon kiruvchi ulanish qabul qilmaydi (statik IP yo'q) — har doim o'zi Controller'ga
`ClientWebSocket` bilan ulanadi, backoff+jitter bilan qayta ulanadi.

## 3. Agentda (Client) bo'lishi shart bo'lgan modullar

| Modul | Vazifasi | Joriy holat |
|---|---|---|
| `ControllerChannelService` | WebSocket outbound ulanish, reconnect | ✅ bor |
| `ICommandDispatcher` | Kiruvchi `command` ni qabul qiladi → `command_result` qaytaradi | ✅ bor |
| `ISessionCoordinator` | start/extend/end/lock/unlock/wake/sleep/set_repair/get_status mantiq | ✅ bor (512 qator) |
| `IControllerOutbox` | Chiquvchi `event` navbat (agent_online, session_started, session_ended, time_low, heartbeat...) | ✅ bor, lekin `command_failed` hech qachon yuborilmaydi ⚠️ |
| `IKioskLockService` | OS darajasida Win/Alt+F4/Alt+Tab/Ctrl+Shift+Esc bloklash | ✅ bor |
| `AgentState` machine | Locked → Active → Frozen → Locked | ✅ bor |
| `GameLauncherViewModel` | Faqat ro'yxatdagi o'yinlarni ko'rsatish/ishga tushirish | ✅ bor |
| `IProcessCleanupService` | Sessiya oxirida ortiqcha process'larni o'ldirish | ✅ bor |
| `IControllerListener` (localhost:7474 HTTP) | Lokal kontroller/admin bilan aloqa, `X-Agent-Secret` | ✅ bor |
| **Voucher xizmati** (`IVoucherService`, Ed25519/NSec) | Server ishlamasa oflayn vaucher berish | ❌ **umuman yo'q**, faqat rejalashtirilgan |
| **Fallback orkestratori** (Kontroller→Server→Voucher→Admin) | CLAUDE.md talab qiladigan zanjirni birlashtiruvchi qatlam | ❌ kodda yo'q |
| **Naqd to'lov audit log** | `CashPaymentViewModel` | ⚠️ bor, lekin `ILogger` yo'q, PIN faqat uzunlik bo'yicha tekshiriladi |
| `MoneyFormatter.Format(long)` | Yagona pul formatlash | ⚠️ yo'q, 3 ta ad-hoc formatlagich bor, 2 joyda `decimal` ishlatilgan (CLAUDE.md buzilgan) |
| QR generatsiya (`external_pc_id`) | To'lov/uzaytirish QR kontrakt formatida | 🔴 **bug**: ichki `PcId`/session GUID ishlatiladi, `external_pc_id` emas |

## 4. Holat mashinasi

**PC (`pc_state`):** `OFFLINE → FREE → OCCUPIED → FROZEN → SLEEPING`, alohida `REPAIR`/`ATTENTION`

**Session (`session_state`):** `STARTING → ACTIVE → FROZEN → ENDED / FAILED`

**Agent UI (`AgentState`):** `Locked → Active → Frozen → Locked`

`end_reason`: `TIME_UP · MANAGER · REFUND · CLIENT_LEFT · ERROR`

## 5. Fallback (razblok tartibi, CLAUDE.md talabi)

```
1. Kontroller (LAN, T0) — asosiy yo'l, sinxron ack + 10s timeout
2. Server (Controller yo'q bo'lsa ham Billing polling orqali)
3. Voucher (server ham yo'q — Ed25519 imzolangan oflayn vaucher) ← kodda yo'q
4. Admin naqd (eng so'nggi chora, audit log MAJBURIY) ← audit log yo'q
```

Bu zanjir hozircha kodda **orkestrator sifatida yig'ilmagan** — har bir bosqich alohida-alohida
bor/yo'q, ularni birlashtiruvchi joy yo'q.

## 6. Tavsiya etilgan test bosqichlari (E2E A–I, docs Qism 3 asosida)

Tartib — oddiy tsikldan murakkab fault-tolerance'ga qarab, MVP (V1) doirasida:

1. **A — asosiy tsikl**: QR skan → to'lov → `start_session` → Locked→Active o'tishi, GameLauncher
   ochilishi. *Eng birinchi tekshiriladigan narsa, chunki QR bug'i shu yerda ta'sir qiladi — avval
   shuni tuzatish kerak.*
2. **B — uzaytirish/freeze**: `extend_session`, freeze grace period (10 daq), `time_low`
   ogohlantirishlari (10/5/1 daq).
3. **C — vaucher/Telegram**: hozircha implementatsiya yo'qligi sababli bu bosqich **bloklangan** —
   avval `IVoucherService` yozilishi kerak.
4. **D — naqd/menejer**: `CashPaymentViewModel` orqali, lekin audit log yo'qligi sababli bu
   bosqichda topilgan kamchilikni alohida bug sifatida yozib qo'yish kerak.
5. **E — fault-tolerance (T1/T2, wake-holding)**: MVP uchun to'liq zanjir V2'ga ko'chirilgan,
   hozircha "bitta kontroller + sovuq zaxira" bilan cheklab test qilish yetarli.
6. **F — xatolar/qaytarish/idempotentlik**: `grant_id`/`command_id` takrorlanganda `duplicate`
   javobi, retry backoff (3→5→10s).
7. **G — fiskalizatsiya**: ⛔ tashqi belgili — haqiqiy Soliq/Payme/Click credential kerak, bular
   hali kelmagan (qarang: tashqi ochiq savollar), shuning uchun bu bosqich hozircha **sinov
   muhitida to'liq bajarilmaydi**.
8. **H — xavfsizlik**: anti-tamper, QR/grant replay himoyasi.
9. **I — V2 ommaviy operatsiyalar**: MVP doirasidan tashqarida, hozircha o'tkazib yuborish mumkin.

### Test-stend talabi

1 kontroller (yoki `tools/MockController`) + 2-3 o'yin PC (Agent bilan) + Core/Billing + menejer
noutbuk + klient telefon + Telegram bot + Payme/Click sandbox, bir LAN'da. WoL faqat jismoniy
Windows PC'da tekshiriladi (virtual mashinada ishonchli emas).

### Amaliy boshlash tartibi (qo'lda, kiosk-lock tufayli avtomatlashtirilmaydi)

`tools/MockController` (yoki haqiqiy `Agent.exe`) server rolida ishga tushiriladi → real Agent
ulanadi → WPF oynada Locked→Active→Frozen o'tishlarini qo'lda kuzatish. Postman orqali faqat
wire-protokol (JSON shakl) tekshiriladi, Agent'ga bevosita ulanib bo'lmaydi.

## 7. Testdan oldin birinchi tuzatish kerak bo'lgan narsalar (blocker'lar)

1. **QR bug** — `LockScreenViewModel`/`ActiveSessionViewModel`/`FreezeViewModel` `external_pc_id`
   o'rniga ichki ID ishlatadi → A/B bo'limlar to'g'ri test qilinmaydi to'g'irlanmaguncha.
2. **`IVoucherService` yo'q** → C bo'limi bloklangan.
3. **Naqd audit log yo'q** → D bo'limida CLAUDE.md talabi buzilgan holda test qilinadi.
4. **`command_failed` event yuborilmaydi**, `heartbeat` payload'idagi
   `controllers_seen`/`server_reachable` hardcoded → F/E bo'limlarda noto'g'ri natija berishi
   mumkin.

**Xulosa:** to'liq E2E (A-I) ni ishga tushirishdan oldin yuqoridagi 4 ta blocker'ni (ayniqsa QR
bug'i) tuzatish tavsiya etiladi, aks holda test natijalari haqiqiy holatni ko'rsatmaydi.
