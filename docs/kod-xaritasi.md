# ClubPay Agent — kod xaritasi (qism-qism ko'rib chiqish uchun)

Bu hujjat kodni **11 ta mustaqil qismga** bo'ladi. Har biri: **nima vazifa bajaradi**,
**qaysi fayllarda**, **qanday ishlaydi** (mexanizm) va **diqqat qilish kerak bo'lgan joylar**
(men ko'rib chiqishda ko'zga tashlangan, lekin hali baholanmagan narsalar — bu ro'yxat
"xato" degani emas, "shu yerga qara" degani).

Siz backend dasturchisiz — shuning uchun tartib backendga yaqin qismlardan (POCO klasslar,
state-mashina, tarmoq qatlami) boshlab, WPF-ga xos qismlarga (ViewModel/XAML) oxirida boradi.
Har bir qismni alohida sessiyada/muhokamada ko'rib chiqamiz.

**Tavsiya etilgan tartib:** 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11

---

## 0-qism — Loyiha tuzilishi (orientatsiya)

3 ta .csproj, bitta yo'nalishli bog'liqlik:

```
ClubPay.Agent.Core     — umumiy: modellar, interfeyslar, kontraktlar, biznes-mantiq servislari
        ↑                (hech narsaga bog'liq emas — "domain layer")
ClubPay.Agent.Client   — o'yin PC'dagi WPF ilova (Core'ga bog'liq)
ClubPay.Agent.Admin    — admin qurilmadagi WPF ilova (Core'ga bog'liq, Client bilan bog'liq emas)
```

Backend analogiyasi: `Core` — sizning `Domain`/`Application` qatlamingiz (interfeys + POCO +
business rules, hech qanday infratuzilma yo'q). `Client` — bitta "worker" xizmat + uning UI'i:
DI konteyner (`App.xaml.cs`) xuddi `Program.cs`dagi `builder.Services.AddSingleton<>()`lar kabi,
faqat WPF `Application` klassida yig'ilgan.

---

## 1-qism — Kontraktlar va modellar (DTO qatlami)

**Vazifa:** Controller (backend/Go xizmati) bilan Agent orasidagi "wire format" — HTTP API'dagi
request/response DTO'laringizga to'g'ri keladi.

**Fayllar:** `Core/Contracts/**` (Envelopes.cs, Events/ControllerEvents.cs,
Payloads/*.cs, Enums/*.cs, Base64Url.cs, ControllerJsonOptions.cs, PcStateMapper.cs) +
`Core/Models/**` (Session, Tariff, AgentState, VoucherPayload, ManagerCodePayload, ...)

**Qanday ishlaydi:**
- Har bir WebSocket xabari uch turdan biri: `CommandEnvelope` (Controller → Agent buyruq),
  `CommandResultEnvelope` (Agent → Controller javob), `EventEnvelope` (Agent → Controller,
  so'ralmagan hodisa — heartbeat, session_started va h.k.).
- `Payload` maydoni `object`/`JsonElement` — kelayotgan buyruqning aniq turi command nomiga
  qarab keyinroq (`CommandDispatcherService`da) deserializatsiya qilinadi. REST'dagi
  "polymorphic body" muammosining oddiy yechimi.
- `PcStateMapper.ToWireState(...)` — ichki `AgentState` enum'ini (Locked/Active/Frozen) +
  qo'shimcha bool'larni (IsManagerLocked, IsRepairMode, isAsleep, isConnected) Controller
  kutayotgan bitta `PcState` enum qiymatiga siqadi. Bu — ichki holat bilan tashqi kontrakt
  orasidagi **anti-corruption layer**.
- `Session` (Models/Session.cs) — vaqtni **wall-clock (`DateTime.UtcNow`) diff** bilan hisoblaydi,
  `EndsAtUtc` esa persist qilinadigan tekshiruv nuqtasi. Izohda ataylab: monotonik soat (Stopwatch)
  ko'rib chiqilib, **rad etilgan** — chunki u process restart'da davom etmaydi, va baribir
  `EndsAtUtc` bilan sverka kerak bo'lardi.

**Diqqat qilish kerak bo'lgan joy:**
- ТЗ v5.3 review talabi so'zma-so'z monotonik soatni talab qiladi (`tuzatish-rejasi.md` T8).
  Kodda esa ataylab boshqacha yo'l tanlangan va sababi izohlangan. Bu — texnik jihatdan
  asosli qaror, lekin ТЗ talabiga rasman zid. O'zingiz screening qilib, rozi bo'lsangiz
  ТЗ'dagi eslatmani yopish kifoya; rozi bo'lmasangiz — qayta ko'rib chiqamiz.

---

## 2-qism — Kontroller bilan aloqa (WebSocket transport)

**Vazifa:** Agent'ning tashqi dunyo bilan yagona simi. Controller'ga **chiquvchi** ulanish
ochadi (Agent'da statik IP yo'q — kontrakt shart qiladi, ТЗ §9 "webhook kiruvchi tugunga
kerak emas" prinsipi).

**Fayl:** `Client/Services/ControllerChannelService.cs` (implements `IControllerChannel`)

**Qanday ishlaydi:**
- `RunConnectionLoopAsync` — cheksiz sikl: `ClientWebSocket` ochadi (Bearer token bilan) →
  ulansa `Connected` holatiga o'tadi → ikkita parallel sikl ishga tushadi:
  - `ReceiveLoopAsync` — kiruvchi JSON xabarlarni o'qiydi, `type=="command"` bo'lsa
    `IncomingCommandHandler`ga uzatadi (bu handler App.xaml.cs'da keyinroq bog'lanadi —
    quyida "Diqqat" bandiga qarang) va javobni darhol shu socket orqali qaytaradi.
  - `SendLoopAsync` — `IControllerOutbox.GetPendingAsync()`dan navbatdagi hodisalarni olib
    ketma-ket jo'natadi, har birini yuborgandan keyin `MarkSentAsync` bilan navbatdan
    o'chiradi. Navbat bo'sh bo'lsa `WaitForPendingAsync` bilan signalgacha uxlaydi (polling
    emas — semaphore-based signal, `AgentStateRepository`dagi `_sendSignal`).
  - Ikkalasidan biri tugasa (`Task.WhenAny`) — ulanish uzilgan deb hisoblanadi, ikkalasi ham
    to'xtaydi, `ComputeBackoffDelay` bilan **exponential backoff + jitter** kutib qayta
    urinadi (ТЗ §9 talabi — CLAUDE.md dagi "Retry policy: 3 urinish, backoff+jitter" ga mos,
    faqat bu yerda urinishlar soni cheklanmagan — doim qayta ulanishga harakat qiladi).
- **Muhim arxitektura qarori:** bu klass hech qachon eventlarni o'zida saqlamaydi — hammasi
  `IControllerOutbox`da (5-qismga qarang). Shuning uchun ulanish o'rtada uzilib qolsa,
  hech narsa "ikki marta yuborilib" yoki "yo'qolib" qolmaydi — outbox yagona haqiqat manbai.

**Diqqat qilish kerak bo'lgan joy:**
- `IncomingCommandHandler` konstruktor orqali emas, **property** sifatida keyinroq
  tashqaridan (`App.xaml.cs`) bog'lanadi — bu `ControllerChannelService ↔ CommandDispatcherService
  ↔ SessionCoordinatorService` orasidagi doiraviy bog'liqlikni (DI cycle) yo'q qilish uchun
  qilingan (T4 vazifasi shu). Backend tilida bu — konstruktor-injection o'rniga event-subscription
  bilan doiraviy dependency'ni kesish. Ishlaydi, lekin "kim buni sozlamasa nima bo'ladi" degan
  savolga `App.xaml.cs`da javob borligini o'zingiz tekshirib ko'ring.
- Bu klass **testlanmagan** (`tests/`da `ControllerChannelService` uchun unit test yo'q,
  faqat `ControllerChannelIntegrationTests.cs` bor — `FakeControllerServer` bilan integration).

---

## 3-qism — Buyruq dispatch (routing qatlami)

**Vazifa:** Kelgan `CommandEnvelope`ni to'g'ri handler'ga yo'naltirish va **har doim**
to'g'ri formatlangan javob qaytarish — hatto noma'lum buyruq yoki kutilmagan xato bo'lsa ham.

**Fayl:** `Client/Services/CommandDispatcherService.cs` (implements `ICommandDispatcher`)

**Qanday ishlaydi:**
- Backend analogiyasi: bu — sizning API controller'ingizdagi bitta `switch`/routing
  qatlamingiz, faqat HTTP marshrutlari o'rniga `command.Name` string'iga qarab yo'naltiradi.
- Har bir `Handle*Async` — payload'ni deserializatsiya qiladi (`Deserialize<T>`), `ISessionCoordinator`
  (4-qism)ga chaqiradi, natijani `Ok(...)`/`Error(...)` bilan o'raydi.
- Xato boshqaruvi ikki bosqichli: `SessionCommandException` (kutilgan biznes-xato, masalan
  "PC allaqachon band") — `ErrorCode` bilan to'g'ridan-to'g'ri qaytariladi; boshqa har qanday
  `Exception` — `ErrorCode.InternalError` sifatida yopiladi, **texnik tafsilot foydalanuvchiga
  chiqarilmaydi** (CLAUDE.md qoidasi: "Exception log qilinadi, foydalanuvchiga texnik xabar
  ko'rsatilmaydi" — garchi bu yerda "foydalanuvchi" Controller tomoni bo'lsa ham).
- Har ikkala xato holatida ham **`command_failed` eventi outbox'ga yoziladi** (T2 vazifasi) —
  shunda Controller buyruq muvaffaqiyatsiz bo'lganini `command_result`dan tashqari alohida
  audit-oqim orqali ham biladi.

**Diqqat qilish kerak bo'lgan joy:**
- `ApplyConfig` buyrug'i qasddan `"apply_config is not supported in this phase"` bilan rad
  etiladi — kontraktda bor, lekin V1'da ishlatilmaydi. Bu ataylab qilingan cheklov, xato emas.

---

## 4-qism — Sessiya state-mashinasi (tizimning yuragi)

**Vazifa:** Butun PC-nazorat holatini boshqaradi: qulflangan/aktiv/muzlagan holatlar,
start/extend/end, menejer-lock, remont-rejimi, "vaqt oz qoldi" ogohlantirishlari, greys-davri,
idle→uyqu va uyg'onishdan tiklanish. **MainViewModel (8-qism) hech qanday qaror qabul
qilmaydi — faqat shu klassning `StateChanged` hodisasini kuzatadi.**

**Fayl:** `Client/Services/SessionCoordinatorService.cs` (implements `ISessionCoordinator`) —
**eng katta va eng muhim fayl**, 555 qator.

**Qanday ishlaydi:**
- Backend analogiyasi: bu — sizning `OrderStateMachine` yoki `SagaOrchestrator`ingiz. Bitta
  `_stateLock` (SemaphoreSlim, 1-joy) orqali **barcha holat o'zgarishlari ketma-ket** (hech
  qachon parallel emas) — race condition'lardan himoya, xuddi backend'dagi
  `SELECT ... FOR UPDATE` yoki distributed lock kabi, faqat process-ichida.
- Uchta asosiy holat: `AgentState.Locked` (bo'sh/qulflangan) → `Active` (sessiya ketyapti) →
  `Frozen` (vaqt tugadi, greys-davrida to'lov kutilyapti) → yana `Locked` (greys tugadi yoki
  `EndSession` keldi).
- **Idempotentlik:** `StartSessionAsync`/`ExtendSessionAsync` avval `IGrantIdempotencyStore
  .HasAppliedAsync(grantId)` tekshiradi — Controller o'sha buyruqni ikki marta yuborsa
  (masalan tarmoq uzilib qayta urinish tufayli), ikkinchi marta hech narsa qilinmasdan
  **oldingi natija** qaytariladi (`IsDuplicate=true`). Bu — ТЗ §9 dagi
  "order_id bo'yicha idempotentlik" talabining agent tarafidagi ko'zgusi.
- **Ichki timer** (`OnTick`, har sekund): (1) heartbeat intervalini sanaydi va vaqti kelganda
  `PublishHeartbeatAsync` chaqiradi; (2) `Active` holatida qolgan vaqtni tekshirib
  10/5/1-daqiqa chegaralarini (`TimeLowThresholds`) bittadan otadi (`_firedThresholds`
  HashSet — har chegara faqat bir marta otiladi); (3) vaqt tugasa `Frozen`ga o'tkazadi va
  greys muddatini (`FrozenUntilUtc`) belgilaydi; (4) greys ham tugasa sessiyani tugatadi.
- **Uyqu/uyg'onish:** `SystemEvents.PowerModeChanged` (Windows OS hodisasi) ga obuna bo'ladi;
  `Resume` kelganda `ResumeFromSleepAsync` — agar uxlab yotgan vaqtda sessiya allaqachon
  tugagan bo'lsa (`EndsAtUtc` o'tib ketgan), darhol tugatadi; aks holda holatni qayta e'lon
  qiladi. Bu ТЗ §8dagi "uyqudan keyin to'g'ri tiklanish" talabi.
- **`ExtendSessionAsync`dagi hisob-kitob nozik joyi:** yangi qolgan vaqt eski
  `GrantedSeconds`ga emas, **`session.RemainingSeconds(now)`ga** (hozirgi haqiqiy qoldiqqa,
  `Frozen`da 0) qo'shiladi — izohda tushuntirilgan: aks holda greys-davrida kutib turilgan
  vaqt qo'shilgan sekundlarni "yeb qo'yardi".
- Har bir mutatsiya (`StartSession`/`ExtendSession`/`EndSession`/`Lock`/`Unlock`/`SetRepair`)
  bir xil naqsh bo'yicha ishlaydi: lock ol → holatni yangila → persist qil (5-qism orqali) →
  outbox'ga event yoz → `RaiseStateChanged()` (bu ham `_agent.KeepAwake(...)` chaqiradi —
  sessiya davomida PC uyquga ketmasligi uchun).

**Diqqat qilish kerak bo'lgan joy:**
- `atomicStore is not null` tekshiruvi (`_store as IAtomicAgentStateStore`) — runtime type-check
  orqali "agar repository atomik commit qo'llab-quvvatlasa, shuni ishlat, aks holda eski
  yo'l" degan shartli mantiq. Ishlaydi (chunki amalda `AgentStateRepository` hamma
  interfeyslarni implement qiladi), lekin backend ko'zi bilan qarasangiz bu — "interfeys
  orqasidan konkret turni taxmin qilish" degan hidli naqsh (CLAUDE.md: "Dependency Inversion:
  har bir servis faqat interfeys orqali inject qilinadi"). Nega ikkita yo'l saqlanganini
  (`atomicallyQueued ? ... : ...` shoxobchalari) birga muhokama qilamiz.
- `WakeAckAsync` — izohda halol yozilgan: haqiqiy Wake-on-LAN Controller/tarmoq darajasida
  bo'ladi, bu metod shunchaki "men uyg'oqman" deb javob qaytaradi. Tushunarli, lekin nomi
  (`WakeAckAsync`) birinchi qarashda chalkashtirishi mumkin.

---

## 5-qism — Lokal saqlash (SQLite outbox/idempotency/session store)

**Vazifa:** Agent o'chib-yonsa ham sessiya, "qaysi grantlar bajarilgan" va "hali yuborilmagan
hodisalar" yo'qolmasligi. Backend analogiyasi: bu sizning **local durable queue + local DB**
qatlamingiz — Controller'ga ulanmagan paytda ham voqealar yo'qolmaydi.

**Fayl:** `Client/Services/AgentStateRepository.cs` — bitta klass, **4 ta interfeysni birdan
implement qiladi**: `ISessionStore`, `IGrantIdempotencyStore`, `IControllerOutbox`,
`IAtomicAgentStateStore`.

**Qanday ishlaydi:**
- SQLite fayl (`agent-state.db`), 3 ta jadval: `current_session` (bitta qatr, id=1 CHECK),
  `applied_ids` (idempotentlik kalitlari, 30 kunlik retention bilan avtomatik tozalanadi —
  `PruneAppliedIdsAsync`), `outbox` (yuborilishi kerak hodisalar navbati, AUTOINCREMENT bilan
  tartib saqlanadi).
- `PRAGMA journal_mode=WAL` + `synchronous=FULL` — WAL: parallel o'qish/yozish uchun; FULL:
  har yozuv diskka jismonan tushgunicha qaytmaslik (pul/sessiya ma'lumoti uchun kerakli
  darajadagi durability — backend'dagi `fsync` talabiga ekvivalent).
- **Atomik commit:** `CommitStartAsync`/`CommitExtendAsync`/`CommitEndAsync` — bitta SQLite
  **transaction** ichida sessiya yozuvi + idempotency-kalit + outbox-hodisa birga yoziladi.
  Bu 4-qismdagi "eski yo'l" (`SaveAsync` + alohida `RecordAppliedAsync` + alohida
  `PublishEventAsync`) dan farqi — u yerda uchta operatsiya orasida jarayon qulasa,
  nomuvofiq holat qolishi mumkin edi (session yozilgan-u, idempotency-kalit yozilmagan).
- **Telemetriya vs pul-hodisalar ajratilgan** (T4 talabi, ТЗ §9 "деньги/сессии выше
  телеметрии"): `heartbeat`/`pc_state_changed` — diskka yozilmaydi, faqat xotirada
  (`_telemetryPending` dictionary, kalit = event nomi — demak har doim eng oxirgisi
  qoladi, eskisi ustidan yoziladi). `session_started`/`extended`/`ended` va boshqa hamma
  narsa — `outbox` jadvaliga yoziladi, oflaynda ham to'planaveradi.
- **Eski JSON'dan migratsiya:** birinchi ishga tushishda baza bo'sh bo'lsa va eski
  `agent-state.json` fayli topilsa, `ImportLegacyJsonAsync` uni o'qib bazaga ko'chiradi va
  faylni `.migrated-<timestamp>.bak` deb nomlab arxivlaydi — eski format buzilib qolgan
  bo'lsa xato faqat log'ga yoziladi, dastur qulamaydi.
- **Schema versiyalash:** `PRAGMA user_version` — kelajakda schema o'zgarsa, eski agent
  versiyasi yangi (kelajakdagi) formatdagi bazani ochib buzib qo'ymasligi uchun himoya
  (`version > 1` bo'lsa `InvalidOperationException`).

**Diqqat qilish kerak bo'lgan joy:**
- Bu — hozir **commit qilinmagan** ish (`git status`da `AgentStateRepository.cs` o'zgargan
  ko'rsatilgan). O'zi to'liq va puxta ko'rinadi (WAL, transaction, versiyalash, migratsiya —
  hammasi bor), lekin qo'lda sinovdan o'tkazib commit qilish kerak.
- Har bir metod `OpenConnectionAsync` bilan **yangi SQLite ulanish** ochadi va yopadi
  (connection pooling o'chirilgan: `Pooling = false`). Bitta fayl bazasi uchun bu normal
  (SQLite fayl darajasida qulflanadi), lekin agar keyinchalik yuk ko'paysa performance'ga
  ta'sirini birga baholaymiz.

---

## 6-qism — Kirish: vaucher va menejer master-kodi (kripto-tekshiruv)

**Vazifa:** LockScreen'dagi yagona kod-kiritish maydoniga kiritilgan matnni tekshirish va
mos bo'lsa sessiya boshlash yoki menejer-lock'ni ochish (ТЗ §7/§10/§13).

**Fayllar:** `Core/Services/VoucherService.cs`, `Core/Services/ManagerCodeService.cs`,
`Core/Services/LockCodeService.cs` (router)

**Qanday ishlaydi:**
- **Token format** (ikkalasida ham bir xil): `base64url(payload_json).base64url(signature)`
  — JWT'ga juda o'xshash, lekin JWT kutubxonasisiz qo'lda qilingan. Imzo **base64url
  qismining ASCII baytlari ustidan** olinadi (JSON'ning o'zi emas) — shu tufayli tekshirishda
  JSON'ni qayta serializatsiya qilish shart emas va property-tartibi/probel farqlari imzoni
  buzmaydi.
- **Ed25519** (`NSec.Cryptography`) — faqat **public key** agentda (config'dan,
  `Voucher:PublicKeyBase64` / `ManagerCode:PublicKeyBase64`); imzolovchi private key hech
  qachon agentga kelmaydi (Billing/backend tarafida turadi). Backend analogiyasi: bu xuddi
  JWT'ni faqat public key bilan tekshiradigan, private key bilan imzolamaydigan resource
  server kabi.
- **`LockCodeService`** — kelgan matnni **imzosini tekshirmasdan** (!) payload'ining birinchi
  qismini base64url-decode qilib, ichida `voucher_id` bormi yoki `code_id` bormi deb "ko'z
  bilan" turini aniqlaydi (`DetectKind`), so'ng mos servisga yo'naltiradi. Xavfsizlik qarori
  bu peek'da emas — har bir servis o'zi imzoni, muddatni, PC/klub bog'lanishini va
  takror-ishlatishni (replay) alohida tekshiradi.
- **Vaucher (`VoucherService`):** imzo → muddat (`ExpiresAtUtc`) → PC/klub bog'lanishi
  (`IsBoundToThisAgent`) → `SessionCoordinator.StartSessionAsync` chaqiriladi, `grant_id =
  voucher_id` — demak **takror-ishlatishdan himoya 4-qismdagi idempotency-do'kondan bepul
  keladi**, alohida "ishlatilgan vaucherlar ro'yxati" yuritilmaydi.
- **Menejer kodi (`ManagerCodeService`):** xuddi shunday tekshiruv, lekin ikki xil natija
  mumkin: agar `IsManagerLocked` bo'lsa — qulfni ochadi (`UnlockManagerLockAsync`); aks
  holda `Locked` holatida bo'lsa — sessiya boshlaydi (`StartSessionAsync`, xuddi vaucherdek).
  Har ikkala holatda ham **`manager_unlock` audit-hodisasi** outbox'ga yoziladi (ТЗ §11
  "kim/qachon/qaysi PC" antikraja talabi) — audit yozib bo'lmasa ham, ochish/boshlash
  **bekor qilinmaydi**, xato faqat log'ga yoziladi (izohda ataylab tushuntirilgan: audit
  muvaffaqiyatsizligi foydalanuvchi tajribasini buzmasligi kerak).

**Diqqat qilish kerak bo'lgan joy:**
- Ikkala servisda deyarli bir xil kod ikki marta yozilgan (imzo tekshirish, key yuklash,
  `IsBoundToThisAgent`). Umumiy bazaviy klass/helper chiqarish mumkin edi — bu klassik
  "ikki marta takrorlanish hali abstraksiya kerak emas" chegarasida turibdimi, yo'qmi — birga
  qaraymiz.
- `DetectKind` — signature tekshirmasdan JSON'ni ochib ko'radi. Xavfsiz (chunki faqat
  routing uchun, qaror qabul qilmaydi), lekin buzilgan/tasodifiy matn kelsa xato "yutilib"
  `Unknown` qaytariladi — buning loglanish darajasi yetarlimi, ko'ramiz.

---

## 7-qism — OS/Kiosk integratsiyasi (Win32 interop qatlami)

**Vazifa:** Windows darajasidagi "past darajali" ishlar — ekran qulflash, tugmalarni
bloklash, foydalanuvchi faolligini kuzatish, begona jarayonlarni tozalash, PC'ni uyquga
yuborish. CLAUDE.md: "past darajali (kiosk, S3, WoL, kiritish, anti-obход) Win32-interop
(P/Invoke) orqali".

**Fayllar:** `Client/Services/KioskLockService.cs`, `IdleDetectionService.cs`,
`ProcessCleanupService.cs`, `AgentService.cs` (qisman — `SleepAsync`/`KeepAwake`),
`InstallService.cs`, `SystemClock.cs`

**Qanday ishlaydi:**
- **`KioskLockService`** — `SetWindowsHookEx(WH_KEYBOARD_LL, ...)` bilan **low-level global
  klaviatura hook** o'rnatadi (butun tizim darajasida, faqat o'z oynasida emas). Har bosilgan
  tugma shu callback'dan o'tadi; Win+L, Ctrl+Shift+Esc (Task Manager), Ctrl+Alt+Del (UI
  darajasida), va `Full` rejimda Alt+F4/Alt+Tab/Alt+Esc — bloklanadi (`return -1` — OS'ga
  "bu tugmani hech kim ko'rmasin" deyish). `Session` rejimida esa Ctrl+Shift+F9 — shell'ni
  ochish/yopish signalini beradi (`ShellToggleRequested`), plain F9 esa o'yinlarga tegilmaydi
  (ular F9'ni quicksave uchun ishlatishi mumkin — izohda alohida tushuntirilgan).
  Qo'shimcha ravishda registry orqali `DisableTaskMgr`/`NoWinKeys` siyosatlarini yoqadi
  (best-effort — administrator huquqi bo'lmasa jim yutiladi).
  **Muhim halollik:** kod izohida ochiq yozilgan — Ctrl+Alt+Del bloklash yozuvi faqat
  "UI darajasida" ishlaydi, chunki haqiqiy SAS (Secure Attention Sequence) winlogon
  darajasida ishlaydi va hech qanday LL hook uni ko'rmaydi. Demak, real Ctrl+Alt+Del'ni bu
  kod **bloklay olmaydi** — bu T9 vazifasining tekshirilishi kerak bo'lgan qismi.
- **`IdleDetectionService`** — `GetLastInputInfo` (Win32) orqali 5 sekundda bir marta
  so'nggi klaviatura/sichqoncha faolligidan qancha vaqt o'tganini so'raydi; chegaraga
  yetsa bir marta `IdleThresholdReached` otadi (`_thresholdFired` bayrog'i takror-otishni
  oldini oladi). `Environment.TickCount` 32-bitli, ~49.7 kunda aylanib chiqadi — kod buni
  `unchecked` arifmetika bilan to'g'ri hisoblaydi (izohda tushuntirilgan).
- **`ProcessCleanupService`** — sessiya tugaganda ishga tushirilgan begona jarayonlarni
  o'ldiradi. "Baseline" naqshi: sessiya boshlanishidan oldin joriy jarayonlar ro'yxatini
  suratga oladi (`SnapshotBaseline`), tugaganda "baseline'da yo'q + tizim jarayoni emas +
  o'zi emas" bo'lgan hammasini o'ldiradi (`entireProcessTree: true` — bola-jarayonlar bilan
  birga). `_safeNames` — Explorer/dwm/svchost kabi tizim jarayonlarini himoya qiladigan
  qattiq ro'yxat.
- **`AgentService.SleepAsync`** — `SetSuspendState(false, false, false)` (Win32) — PC'ni S3
  uyquga yuboradi (gibernatsiya emas — birinchi parametr `false`).

**Diqqat qilish kerak bo'lgan joy:**
- **Watchdog yo'q** (T9): bu qismdagi hamma narsa — Agent WPF-protsessining o'zi ichida.
  Protsess ishdan chiqsa (crash yoki Task Manager orqali — garchi Task Manager ham
  bloklangan bo'lsa-da), hech kim uni qayta ko'tarmaydi. CLAUDE.md asl rejasida "Windows
  Service + WPF" deyilgan, lekin hozir alohida Windows Service loyihasi yo'q.
  `InstallService.cs` bor-lekin bu — nomidan farqli, sinov qilib ko'rish kerak nima
  qilishini.
- Safe Mode'da bu hook'larning hech biri ishlamaydi (Windows Safe Mode'da uchinchi-tomon
  driver/hook'lar yuklanmaydi) — bu ham hujjatlashtirilmagan cheklov.

---

## 8-qism — WPF UI qatlami (MVVM: ViewModel + View)

**Vazifa:** Foydalanuvchi (o'yinchi) ko'radigan ekranlar. Backend analogiyasi: **ViewModel —
sizning "presenter"/"controller" qatlamingiz** (holatni tashqi servisdan olib, UI'ga mos
formatga aylantiradi, buyruqlarni orqaga servisga uzatadi); **View (XAML) — shablon**
(Razor/JSX'ga o'xshab, faqat data-binding bilan, "template render" chaqirilmaydi — WPF o'zi
`PropertyChanged` hodisasini kuzatib qayta chizadi).

**Fayllar:** `ViewModels/*.cs` (MainViewModel, LockScreenViewModel, ActiveSessionViewModel,
FreezeViewModel, GameLauncherViewModel, LockCodeMessages) + `Views/*.xaml(.cs)` +
`Converters/Converters.cs` + `Resources/*.xaml`

**Qanday ishlaydi:**
- **`MainViewModel`** — eng yuqori darajadagi "router". `ISessionCoordinator.StateChanged`ga
  obuna bo'ladi (4-qism), holat o'zgarganda tegishli child-ViewModel'ni yangilaydi
  (`LockScreen.Reset()`, `ActiveSession.Sync(session)`, `Freeze.ShowGrace(...)`). **Hech
  qanday qaror qabul qilmaydi** — faqat aks ettiradi. `System.Windows.Application.Current
  .Dispatcher.InvokeAsync(...)` — chunki `StateChanged` fon-thread'dan (timer/WebSocket)
  kelishi mumkin, WPF esa UI-elementlarni faqat UI-thread'dan yangilashga ruxsat beradi
  (backend'dagi "faqat asosiy event-loop'da holat o'zgartirish mumkin" cheklovi kabi).
- **`LockScreenViewModel`** — QR kodlarni generatsiya qiladi (`QrCodeService`), Enter
  bosilganda kod-kiritish maydonini ochadi, kiritilgan kodni `ILockCodeService.SubmitAsync`
  (6-qism)ga uzatadi, natijaga qarab xabar ko'rsatadi yoki yashiradi. Biznes-mantiq yo'q —
  hammasi servisda (CLAUDE.md qoidasiga mos).
- **`ActiveSessionViewModel`** / **`FreezeViewModel`** — sessiya davomida qolgan vaqt,
  ogohlantirish rangi (`TimerToBrushConverter`), uzaytirish QR (`QrUrlBuilder.BuildSessionUrl`,
  1-qism).
- **`GameLauncherViewModel`** — shell/o'yin-tanlash oynasi mantig'i (7-qismdagi
  `ProcessCleanupService`ga tayanadi — "ishlab turgan begona jarayon bormi" ni tekshirib
  o'yin tugaganini aniqlaydi, chunki Steam kabi launcher'lar darhol chiqib ketadi-yu, haqiqiy
  o'yin boshqa PID'da davom etadi).
- **Views** — `KioskWindow` (asosiy oyna, holatlar orasida almashtiradi), `LockScreenView`,
  `ActiveSessionCornerView`/`ActiveSessionFullView` (kichik burchak vs to'liq ekran ko'rinishi),
  `FreezeView`, `GameLauncherWindow`, `SessionOverlayWindow`.

**Diqqat qilish kerak bo'lgan joy:**
- `MainViewModel.RefreshFromCoordinator`dagi izohda o'zi tan olingan "tech debt":
  `GameLauncherViewModel` oyna/UI masalalarini jarayon-o'ldirish biznes-mantig'i bilan
  aralashtirib yuboradi (`_launcher.KillRunningApp()` MainViewModel'dan to'g'ridan-to'g'ri
  chaqiriladi). Kichik, lekin CLAUDE.md'dagi "ViewModel'da biznes logika bo'lmaydi" qoidasiga
  chegaradosh joy — birga ko'rib chiqamiz.
- uz/ru mahalliylashtirish infra'si yo'q — barcha matnlar hozircha o'zbekcha hardcoded
  (`LockCodeMessages.cs` va boshqa joylarda). ТЗ §18 talabi, ataylab keyinga qoldirilgan.

---

## 9-qism — Yordamchi util'lar

**Vazifa:** Kichik, holat saqlamaydigan (stateless) yordamchi funksiyalar — bir nechta
qismda ishlatiladi, lekin har birining o'zi juda kichik.

**Fayllar:** `Core/QrUrlBuilder.cs`, `Client/Services/QrCodeService.cs`,
`Core/MoneyFormatter.cs`, `Core/SessionWarningCalculator.cs`, `Core/Constants.cs`

**Qanday ishlaydi:**
- `QrUrlBuilder` — LockScreen uchun statik URL (`club/pc` yo'li) va sessiya uchun dinamik
  URL (`?pc=...&session=<CoreSessionId:N>`) quradi. `CoreSessionId ?? localSessionId` —
  agar Controller hali sessiyani tasdiqlamagan bo'lsa (masalan menejer-kod bilan boshlangan
  lokal sessiya) lokal GUID'ga tushadi — bu T1 vazifasining yechimi.
- `QrCodeService` — `QrUrlBuilder`dan kelgan matnni haqiqiy QR-rasmga (`BitmapImage`)
  aylantiradi (QRCoder kutubxonasi, CLAUDE.md'da belgilangan stack).
- `MoneyFormatter` — `long` tiyinni `"15 000 so'm"` ko'rinishiga formatlaydi — CLAUDE.md
  "Pul: faqat long tiyinda ... MoneyFormatter.Format(long tiyin)" qoidasining implementatsiyasi.
- `Constants.cs` — barcha "magic string/number"lar shu yerda markazlashgan (timer chegaralari,
  event/command nomlari, outbox limiti va h.k.) — CLAUDE.md "Magic string yo'q — AppConstants
  klassida" qoidasi.

**Diqqat qilish kerak bo'lgan joy:** bu qism kichik va past-risk — tez ko'rib o'tish mumkin.

---

## 10-qism — Admin (WPF) — hozircha demo, real backend bilan bog'liq emas

**Vazifa (ТЗ bo'yicha bo'lishi kerak):** menejer paneli — PC setkasi, naqd to'lov, wake/sleep/
end-session buyruqlari. **Lekin ТЗ §4/11/23 aniq aytadi: bu web-PWA bo'lishi kerak (Controller
xizmati beradi), WPF emas.** Shu sabab bu repo'dagi WPF Admin — hal qilinmagan T12 savolining
markazida turibdi (avvalgi javobimda batafsil yozgandim).

**Fayllar:** `Admin/ViewModels/AdminViewModel.cs`, `CashPaymentViewModel.cs`,
`Admin/Services/AgentEndpointServer.cs`, `IPendingSessionStore.cs`, `PendingSessionStore.cs`

**Hozirgi holat:**
- `AdminViewModel` — 20 ta PC **hardcoded massiv** ichida (`LoadDemoPcs`), hech qanday
  tarmoq/kontroller ulanishi yo'q. `WakePcAsync`/`SleepPcAsync`/`EndSessionAsync` —
  `await Task.Delay(100)` — **hech narsa qilmaydi**, faqat UI'da tugma bosilgandek ko'rsatadi.
- `CashPaymentViewModel` — PIN faqat 4 ta raqamdan iboratligini tekshiradi
  (`PinInput.Length < 4`), haqiqiy tekshiruv yo'q (`// TODO: verify PIN via admin service`).
  Naqd sessiya hech qayerga (fayl/log/audit) yozilmaydi.
- `AgentEndpointServer`/`PendingSessionStore` — eski HTTP-polling yo'lining qoldig'i; Client
  bu endi ishlatmaydi (2-qismdagi WebSocket bilan almashtirilgan), lekin Admin loyihasidan
  hali olib tashlanmagan — **o'lik kod**.

**Diqqat qilish kerak bo'lgan joy (bu — kod emas, qaror masalasi):**
- Bu qismni kod darajasida "to'g'rilash"dan oldin T12'ni hal qilish kerak: WPF Admin
  dev-vosita bo'lib qoladimi (PWA asosiy bo'ladi), yoki WPF rasmiy T2-avtoritetga
  aylanadimi (unda katta qo'shimcha ish — kontroller-dvijoki kerak bo'ladi). Bu qismni
  ko'rib chiqishni **oxiriga qoldirish** tavsiya etiladi — chunki javob "qanday to'g'rilash
  kerak"ni tubdan o'zgartiradi.

---

## 11-qism — Testlar (har qismga mos)

Qism ko'rib chiqilganda tegishli testni ishga tushirib ko'rish mumkin:

| Qism | Test fayli |
|---|---|
| 1 (kontraktlar) | `Core.Tests/Contracts/PcStateMapperTests.cs`, `Core.Tests/Models/SessionTests.cs`, `Core.Tests/QrUrlBuilderTests.cs` |
| 2 (WebSocket transport) | `Client.Tests/Integration/ControllerChannelIntegrationTests.cs` (+ `TestHarness/FakeControllerServer.cs`) |
| 3 (buyruq dispatch) | `Client.Tests/Services/CommandDispatcherServiceTests.cs` |
| 4 (state-mashina) | `Client.Tests/Services/SessionCoordinatorServiceTests.cs`, `Core.Tests/SessionWarningCalculatorTests.cs` |
| 5 (SQLite) | `Client.Tests/Services/AgentStateRepositoryTests.cs` |
| 6 (vaucher/menejer-kod) | `Core.Tests/Services/VoucherServiceTests.cs` (+ `VoucherTestTokens.cs`), `Core.Tests/Services/ManagerCodeServiceTests.cs` (+ `ManagerCodeTestTokens.cs`), `Core.Tests/Services/LockCodeServiceTests.cs` |
| 7 (OS/kiosk) | `Client.Tests/Services/KioskLockServiceHotkeyTests.cs`, `Client.Tests/Services/ProcessCleanupServiceTests.cs` |
| 8 (UI/ViewModel) | `Client.Tests/ViewModels/GameLauncherViewModelTests.cs`, `Client.Tests/ViewModels/LockCodeMessagesTests.cs` |
| 9 (util'lar) | `Core.Tests/MoneyFormatterTests.cs` |
| 10 (Admin) | test yo'q (demo kod uchun test yozilmagan) |

Umumiy holat: **154 test yashil** (80 Core + 74 Client), build 0 xato — bu sessiyada
`dotnet test ClubPay.Agent.slnx` bilan tasdiqlangan.

---

## Keyingi qadam

Qaysi qismdan boshlaymiz? Tavsiyam: **1-qism (kontraktlar/modellar)** — eng "backend"ga
o'xshash, boshqa hamma qism shunga tayanadi, va eng arzon/xavfsiz o'rganish nuqtasi.
