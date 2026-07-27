# V1 holati — Client va Admin (2026-07-25 audit)

Manba: `docs/_Clubpay - TЗ.docx` (v5.4) + `docs/tuzatish-rejasi.md` (T1–T12) + shu sessiyada
qilingan to'g'ridan-to'g'ri kod tekshiruvi (faqat checkbox'larga emas, haqiqiy fayllarga qarab).
`tuzatish-rejasi.md` dagi T1/T2/T3/T6 belgilari shu audit asosida yangilandi — avval
belgilanmagan edi, lekin kodda aslida bajarilgan ekan.

---

## CLIENT (PK Agent) — asosiy og'irlik shu yerda

### ✅ Bajarilgan

| # | Nima | ТЗ | Isbot |
|---|---|---|---|
| T1 | Uzaytirish/freeze QR endi `CoreSessionId`+`external_pc_id` ishlatadi, hardcoded `pay.clubpay.uz` yo'q, baza URL configdan | §5/13 | `QrUrlBuilder`, `ActiveSessionViewModel.cs:66`, `FreezeViewModel.cs:44` |
| T2 | `command_failed` eventi outbox'ga chiqadi, heartbeat'da `ControllersSeen`/`ServerReachable` real ulanish holatidan | §9 | `CommandDispatcherService.cs`, `SessionCoordinatorService.cs:522` |
| T3 | Integration testlar (localhost URL-ACL fix) | — | `FakeControllerServer.cs:37` |
| T4 | DI sikli yo'q qilingan, telemetriya/pul eventlari outbox'da ajratilgan | §9 | commit `daec127`/`f227fbf` |
| T5 | `VoucherService` — Ed25519 oflayn vaucher, minimum yo'q, replay-himoya | §6/10/13 | commit `3e8e061` |
| T6 | LockScreen'da Enter→yagona kod maydoni (vaucher yoki menejer master-kodi, `ILockCodeService` router), audit event | §7 | `LockScreenViewModel.cs`, `ManagerCodeService` (`821d80b`) |
| — | Ogohlantirish narvoni 10/5/1 daq + muzlatish ekrani + sessiyaga bog'langan uzaytirish QR | §7 | `FreezeView`, `TimerToBrushConverter` |
| — | Pul: `long` tiyin, `MoneyFormatter` — ad-hoc `decimal` olib tashlangan | Kod sifati qoidasi | oldingi sessiya |
| — | Idle-uyqu (S3) + uyg'onishdan tiklanish (`IIdleDetectionService`, `SetSuspendState`) | §8 | `SessionCoordinatorService.cs:299-430` |
| — | GameLauncher shell — V1 minimum (ishga tushirish/qaytish, band-toast, xato-toast) | §22 | oldingi sessiya, 74 Client test |
| — | Legacy HTTP-polling yo'li Client'dan tozalangan (`Constants.Controller` endi hech qayerda chaqirilmaydi) | T10 (qisman) | grep tasdiqladi |
| — | Build 0 xato, testlar **154 yashil** (80 Core + 74 Client) | — | shu sessiyada `dotnet test` bilan tekshirildi |

### ❌ Bajarilmagan / ochiq

| # | Nima | ТЗ | Nega muhim |
|---|---|---|---|
| T9 | **Watchdog Windows Service yo'q** — agent oddiy WPF, UI protsess o'chirilsa/qulasa hech kim qayta ko'tarmaydi | §9/13, CLAUDE.md asl rejasi ("Windows Service + WPF") | Pilotda eng katta operatsion risk |
| T9 | Anti-bypass yolg'on: `KioskLockService.cs`dagi Ctrl+Alt+Del "bloklash" haqiqatda ishlamaydi (SAS winlogon darajasida, LL hook ko'rmaydi) — comment/kod hali soxta holatda; Safe Mode / 2-chi Windows user himoyasi yo'q | §13 | Kioskni aylanib o'tish mumkin |
| T11 | Litsenziya/hardware-lock — agent tarafida umuman yo'q (backend `apply_license` tayyor bo'lmagani uchun ataylab kutilmoqda) | `license-subscription-talablari.md` | Blocker emas, lekin ochiq |
| — | uz/ru mahalliylashtirish — butun ilova bo'ylab infra yo'q, ataylab keyinga qoldirilgan | §18 ("interfeyslar uz/ru") | V1 talab qiladi, hali qilinmagan |
| T8 | Monotonik taymer — **qaror qabul qilingan, lekin ТЗ so'zidan farqli**: `Session.cs`dagi izohda ataylab wall-clock+`EndsAtUtc` tanlangan ("monotonik ko'rib chiqilgan va rad etilgan — restart'da davom etmaydi"). ТЗ v5.3 esa aynan monotonikni talab qiladi | §9 v5.3 review | Texnik qaror to'g'ri bo'lishi mumkin, lekin ТЗ bilan ziddiyat — tasdiqlash kerak |
| — | **Commit qilinmagan ish davom etmoqda**: `AgentStateRepository`ni JSON fayldan SQLite'ga o'tkazish (yangi `IAtomicAgentStateStore`, `App.xaml.cs`/`SessionCoordinatorService.cs` o'zgargan) — tugallanmagan, testdan o'tkazilib commit qilinishi kerak | T4 davomi | Hozirgi ishchi holat |

---

## ADMIN — eng muhim ogohlantirish: T12 hal qilinmagan

**ТЗ §4/11/23 aniq aytadi: menejer paneli — kontroller (Go xizmati) beradigan web-PWA, WPF
emas.** Bu repo'dagi WPF Admin bilan to'g'ridan-to'g'ri ziddiyat. Shuning uchun oldingi
sessiyada qaror chiqmaguncha Admin'ga qo'l tegizilmagan (faqat T7 qilinishi tavsiya etilgan
edi — u ham hali qilinmagan).

### Hozirgi holat — 100% demo, real backend bilan bog'liq emas

- `AdminViewModel`: 20 ta PC — **hardcoded** ro'yxat, hech qanday kontroller/serverdan kelmaydi
- `"Sardor"` — hardcoded admin ismi
- `WakePcAsync`/`SleepPcAsync`/`EndSessionAsync` — hammasi `Task.Delay(100)` placeholder,
  **hech qanday real komanda yubormaydi**
- Zona filtri — `// TODO: filter Pcs collection`, ishlamaydi
- `AgentEndpointServer`/`PendingSessionStore` — legacy HTTP yo'l, Client endi ishlatmaydi,
  lekin Admin'da hali turibdi (o'lik kod)
- **T7 — naqd to'lov audit logi hali qilinmagan** (ТЗ §11 majburiy talabi):
  - `CashPaymentViewModel`da `ILogger` yo'q
  - PIN faqat uzunlik (`PinInput.Length < 4`) bo'yicha tekshiriladi —
    `// TODO: verify PIN via admin service`
  - Naqd sessiya hech qayerga yozilmaydi (menejer ID/PC/summa/vaqt/sabab — yo'q)
  - Pul `decimal` bilan formatlanadi (`AmountTiyin / 100m:N0`) — CLAUDE.md qoidasi
    buzilgan, `MoneyFormatter.Format` ishlatilishi kerak
  - `PcId = "PC-12"` hardcoded default

### Sizga kerak bo'lgan qaror (kod emas!)

1. **Variant A** — Admin faqat dev-vosita bo'lib qoladi (ТЗ'ga mos, PWA asosiy panel
   bo'ladi). Bu holda Admin'ga faqat **T7** qilinadi, boshqa hech narsaga vaqt ketmaydi.
2. **Variant B** — WPF Admin rasmiy T2-avtoritet bo'ladi (ТЗ §9dagi "menejer PK si"
   zanjiri). Bu holda unga butun kontroller-dvijoki (agentlar bilan WS hub, to'lov
   so'rovi, grant berish, outbox) kerak — bu alohida katta loyiha, backend jamoasi bilan
   birga rejalashtiriladi.

Bu qaror qabul qilinmaguncha Admin'ga T7'dan boshqa ish qilish tavsiya etilmaydi — chunki
noto'g'ri variant tanlansa (A bo'lib chiqsa), Variant B yo'nalishidagi ish bekor ketadi.

---

## Tavsiya etilgan tartib

```
1. T12 qarorini chiqaring (kod emas — arxitektura qarori)
2. T7 — Admin cash audit (qaysi variant bo'lsa ham kerak)
3. T9 — Watchdog + anti-bypass haqiqiy holat (pilot uchun eng katta operatsion risk)
4. T8 ni qayta ko'rib chiqing — wall-clock qarorini ataylab qilganmisiz, yoki eslab
   qolinmagan izoh edimi?
5. Uncommitted SQLite migratsiyasini tugatib commit qiling
6. uz/ru mahalliylashtirish (V1 talabi, hali umuman yo'q)
7. T11 — backend `apply_license` tayyor bo'lganda
```
