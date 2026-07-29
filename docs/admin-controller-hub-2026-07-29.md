# Admin → Controller Hub (T12 hal qilindi, T7 bajarildi) — 2026-07-29

Manba: `docs/_Clubpay - TЗ.docx` §4/9/11/23, `docs/v1-holati.md`, `docs/tuzatish-rejasi.md`
(T7, T12). Bu hujjat shu sessiyada `ClubPay.Agent.Admin` ustida qilingan ishning to'liq
xulosasi — keyingi sessiya/kompyuterda tez tushunib olish uchun.

## Kontekst — T12 qarori

ТЗ §11/23 rasman menejer panelini kontroller beradigan **web-PWA** deydi, WPF emas —
mavjud WPF `ClubPay.Agent.Admin` bilan to'g'ridan-to'g'ri ziddiyat. Bu qaror avvalgi
sessiyalarda ochiq qoldirilgan edi (`docs/tuzatish-rejasi.md` T12):

- **Variant A** — Admin faqat dev-vosita, haqiqiy panel alohida PWA bo'ladi.
- **Variant B** — WPF Admin ТЗ §9 zanjiridagi "menejer PK si" (T2-avtoritet) rolini
  bajaradi, to'liq kontroller-dvigatel bilan.

**Foydalanuvchi Variant B'ni tanladi.** Shu tanlov asosida quyidagi ish bajarildi.

## Milestone 1 — Controller Hub

Admin endi shunchaki UI emas — Agentlar bevosita ulanadigan **real kontroller**.

### Yangi fayllar (`src/ClubPay.Agent.Admin/Services/Controller/`)

| Fayl | Vazifasi |
|---|---|
| `PcRegistry.cs` | `appsettings.json`dagi statik PC ro'yxati (`ExternalPcId`, `PcId`, `Zone`, `AgentToken`) + token tekshiruvi |
| `PcLiveState.cs` | Har bir PC uchun jonli holat modeli (PcState, SessionState, CoreSessionId, RemainingSeconds, IsConnected) |
| `IPcStateStore` / `PcStateStore.cs` | Agent event'laridan (`session_started`, `pc_state_changed`, `heartbeat`, ...) jonli holatni yuritadi, `Changed` hodisasi bilan |
| `PcCardMapper.cs` | Wire `PcState` → UI `PcStatus`/matn (sof mapping, Core'dagi `PcStateMapper`ning teskarisi) |
| `ControllerHubService.cs` | **Asosiy qism** — ko'p-ulanishli WebSocket server (`HttpListener` asosida). Agentlar (`ControllerChannelService`, Client loyihasida) shu hub'ga to'g'ridan-to'g'ri ulanadi (aynan `tools/MockController`/`FakeControllerServer` protokoli, lekin ko'p-PC uchun). `SendCommandAsync(externalPcId, name, payload, timeout)` — sinxron ack, timeout'da `command_timeout`, ulanmagan PC uchun `agent_offline`. Har 15 soniyada ulangan PC lardan `get_status` so'raydi (chunki hech bir push-event mid-session `remaining_seconds`ni bermaydi, faqat `time_low` chegaralarda) |

### O'zgargan fayllar

- **`AdminViewModel.cs`** — 20 ta hardcoded demo PC o'chirildi, endi `IPcRegistry`+`IPcStateStore`dan
  yuklanadi va `Changed` hodisasi orqali jonli yangilanadi. `WakePcAsync`/`SleepPcAsync`/
  `EndSessionAsync` + yangi `LockPcAsync`/`UnlockPcAsync`/`SetRepairAsync` — barchasi
  `ControllerHubService.SendCommandAsync` orqali **real** komanda yuboradi (avval
  `Task.Delay(100)` stub edi). Zone filtri `CollectionViewSource`/`ICollectionView` bilan
  haqiqatda ishlaydi (avval TODO edi).
- **`Core/Models/PcCard.cs`** — `init`-only'dan `ObservableObject`ga o'tkazildi (jonli
  yangilanish uchun), property nomlari o'zgarmadi → XAML binding buzilmadi.
- **`Core/Models/PcStatus.cs`** — `Offline`, `Repair` qo'shildi (avval 5 ta holat bor edi,
  wire `PcState`ning 7 tasini to'liq qoplamas edi).
- **UI tugmalari birinchi marta ulandi**: `PcCardControl.xaml`dagi Wake/Sleep/End va
  `PcDetailPanel.xaml`dagi "Sessiyani tugatish"/"Yoqish-Uxlatish" — avval bu tugmalar
  hech qanday komandaga bog'lanmagan edi (faqat vizual). Endi yangi routed event'lar
  (`PcWakeRequested`, `PcSleepRequested`, `PcEndRequested`) orqali `AdminViewModel`ning
  mos komandalariga ulandi.
- **`App.xaml.cs`** — DI: `IPcRegistry`, `IPcStateStore`, `ControllerHubService` ro'yxatdan
  o'tkazildi, `ControllerHubService.StartAsync/StopAsync` ilova lifecycle'iga ulandi.
- **`appsettings.json`** — yangi `Controller.ListenPrefix` (default `http://localhost:8787/`,
  LAN uchun `http://+:PORT/` + `netsh http add urlacl` kerak bo'ladi) va `Controller.Pcs[]`
  statik ro'yxat.

### O'chirilgan fayllar (o'lik kod, pivotdan oldingi)

`Services/AgentEndpointServer.cs`, `Services/IPendingSessionStore.cs`,
`Services/PendingSessionStore.cs` (legacy HTTP `/api/agents/{pcId}/pending-session` yo'li,
Client allaqachon ishlatmas edi) va `Core/Constants.cs`dagi ishlatilmay qolgan `Controller`
static klassi.

## Milestone 2 — T7: Naqd to'lov audit logi (ТЗ §11)

- **Yangi**: `ICashAuditService`/`CashAuditService` (Core interfeys, Admin implementatsiya) —
  har naqd sessiya (ManagerId, PcId, DurationSeconds, `long` AmountTiyin, UTC vaqt,
  ReasonCode) append-only JSON-lines faylga (`%ProgramData%\ClubPay\Admin\cash-audit.jsonl`,
  `Admin:CashAuditLogPath` bilan sozlanadi) + `ILogger` structured log.
- **Yangi**: `IManagerPinService`/`ManagerPinService` — kiritilgan PIN'ning SHA-256 hash'ini
  `Admin:ManagerPinHash` (config) bilan solishtiradi; hash sozlanmagan bo'lsa — **hech qachon
  tasdiqlanmaydi** (fail-closed, fail-open emas). Dev-default: PIN `1234`
  (hash `03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4`) — production'da
  albatta almashtirilishi kerak.
- **`CashPaymentViewModel.cs`** — `AmountLabel` endi `decimal` o'rniga
  `Constants.Money.FormatSom` ishlatadi (CLAUDE.md qoidasi); `Confirm()` endi PIN uzunligi
  o'rniga haqiqiy hash tekshiradi va muvaffaqiyatda `ICashAuditService.RecordAsync`
  chaqiradi.
- **Muhim topilma**: `CashPaymentDialog.xaml`ning `DataContext`si aslida `AdminViewModel`ga
  bog'langan edi (`CashPaymentViewModel`ga emas!), va "Tasdiqlash" tugmasi shunchaki dialogni
  yopar edi (`// TODO: real confirm flow`) — audit-log yozilgan bo'lsa ham hech qachon
  chaqirilmas edi. Bu safar to'g'ri `CashPaymentViewModel`ga ulandi (`AdminViewModel.CashPayment`
  property orqali), shuning uchun audit-log endi haqiqatda ishlaydi.

## Testlar

Yangi `tests/ClubPay.Agent.Admin.Tests` (xUnit + Moq) — **43 test**, jumladan
`ControllerHubService`ni **haqiqiy** `ClientWebSocket` bilan end-to-end tekshiradigan
testlar (real HttpListener, real socket — mock emas).

Shu testlar orqali **haqiqiy production bug topildi va tuzatildi**: `ControllerHubService`
kiruvchi WebSocket Close frame'ga hech qachon javob bermas edi (`ReceiveLoopAsync`da
shunchaki `return`). Natijada har qanday to'g'ri xulq-atvorli klient (masalan real Agent
o'chganda) graceful `CloseAsync()` chaqirsa — javob kutib **abadiy osilib qolar edi**.
`CloseOutputAsync` bilan tuzatildi (`socket.State == WebSocketState.CloseReceived` bo'lsa).

**Butun solution**: 216 test, hammasi yashil (77 Core + 43 Admin + 96 Client),
`dotnet build` — 0 xato/ogohlantirish, `dotnet format --verify-no-changes` — yangi/
o'zgargan fayllarda 0 xato.

`Admin.exe` qo'lda ishga tushirilib tekshirildi: hub muvaffaqiyatli start bo'ladi, oyna
ochiladi, "Responding" holatda, muammosiz yopiladi. **Haqiqiy `ClubPay.Agent.Client.exe`
bilan to'liq qo'lda UI-orqali sinov qilinmadi** — Client kiosk-lock (global keyboard hook)
ishlatadi, buni ushbu ish stantsiyasida avtomatlashtirish xavfli deb topildi (memory'da
avvaldan qayd etilgan). Bu keyingi qo'lda tekshirish vazifasi.

## Ushbu ishdan tashqarida qoldirilgan (aniq belgilangan, T2-avtoritetning to'liq zanjiri uchun)

- **To'lov-provayder oprosi** T1/T2 (server+Pi ikkalasi ham o'chganda) uchun — merchant-sekret
  masalasi hali javobsiz tashqi savol (`docs/`dagi "tashqi savollar" bo'limi).
- **mDNS discovery + lokal HTTPS** (ТЗ §23) — production ko'p-klub joylashtirish uchun kerak;
  hozircha statik `ws://<manager-ip>:port` yetarli (V1/pilot).
- **Hub-tarafda outbox/durability** — sessiya haqiqati Agent tarafida (kesh) saqlanadi,
  Admin qayta ishga tushsa faqat qayta ulanish kerak.
- **Ommaviy operatsiyalar, superadmin PC-provisioning UI** — V2+.
- **T11 litsenziya/hardware-lock** — alohida trek, backend `apply_license`ga bog'liq.

## Qanday tekshirish / davom ettirish

```bash
dotnet build ClubPay.Agent.slnx      # 0 xato kutilmoqda
dotnet test ClubPay.Agent.slnx       # 216 test, hammasi yashil kutilmoqda
```

Qo'lda LAN sinovi uchun: `Admin/appsettings.json`dagi `Controller.Pcs[0].ExternalPcId`/
`AgentToken` — `Client/appsettings.json`dagi `Controller.ExternalPcId`/`AgentToken` bilan
mos bo'lishi kerak; `Client`ning `Controller:WebSocketUrl`i Admin hub manziliga
(`ws://<admin-ip>:8787/agent/ws`) ko'rsatilishi kerak (production `wss://api-clubpay...`
o'rniga, faqat qo'lda test uchun).

**Diqqat:** `dotnet test` paytida `ControllerHubServiceTests` real tarmoq-socketlaridan
foydalanadi — agar tasodifan hang bo'lib qolsa (masalan boshqa muhitda), avval
`Get-Process testhost | Stop-Process -Force` bilan qolib ketgan test-host'larni tozalang
(fayl-lock muammosi keyingi build'ni buzadi).

## Git holati

Barcha o'zgarishlar **hali commit qilinmagan** (shu sessiya oxirida). Branch:
`Integrate-backend`. Boshqa kompyuterda davom ettirish uchun: ushbu ishni commit+push
qilib, o'sha kompyuterda `git pull`/`git checkout Integrate-backend` qilish kerak.
