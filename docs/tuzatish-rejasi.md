# ClubPay Agent — Tuzatish rejasi (2026-07-21 audit asosida)

Manba: `docs/_Clubpay - TЗ.docx` (v5.4) + kontrakt v1.2 bilan to'liq kod auditi.
Har bir vazifa **alohida Claude Code sessiyasida** bajariladi. Vazifa ichidagi
"Prompt" blokini to'g'ridan-to'g'ri copy-paste qilsangiz bo'ladi.

## Claude Code bilan ishlash tartibi (umumiy qoidalar)

1. **Bitta vazifa = bitta sessiya = bitta commit.** Vazifa tugagach `/clear` qiling —
   eski kontekst keyingi vazifani chalg'itadi.
2. **Katta vazifalarda (T4, T5, T9, T11) avval Plan mode** ishlating (Shift+Tab bilan
   plan rejimga o'ting yoki promptga "avval reja tuz, kodga tegma" deb yozing).
   Rejani o'qib tasdiqlang, keyin bajartiring.
3. **Har vazifadan keyin tekshiruv:** `dotnet build` (0 error/0 warning) va
   `dotnet test ClubPay.Agent.slnx`. Prompt oxirida buni talab qilish yozilgan.
4. **Commit xabari aniq bo'lsin** — Claude'dan so'rang: "o'zgarishlarni commit qil,
   xabarda qaysi ТЗ bo'limiga tegishliligini yoz".
5. **Tartibga rioya qiling:** P0 → P1 → P2. Ayniqsa **T4 (refactoring) T5 (vaucher)dan
   OLDIN** bajarilishi shart — ikkalasi ham SessionCoordinator'ga tegadi.
6. Vazifa tugagach shu faylda checkbox'ni belgilang — keyingi sessiyada Claude'ga
   "docs/tuzatish-rejasi.md dagi T<N> ni bajar" deyish kifoya.
7. Claude noto'g'ri yo'lga ketsa — to'xtatib (Esc), aniqlashtiruvchi kontekst bering;
   "davom et" demang, qabul mezonini eslatib qayta yo'naltiring.

---

## P0 — E2E testdan oldin (blocker'lar)

### [x] T1. Uzaytirish QR bug'i: lokal GUID o'rniga CoreSessionId + yagona QR domen

**Muammo:** `MainViewModel.cs:80` freeze QR'ga `CurrentSession.Id` (agentning ichki
GUID'i) beradi; `ActiveSessionViewModel.cs:60` ham `session.Id` ishlatadi. Billing esa
faqat `CoreSessionId` (start_session'da qaytarilgan) va `external_pc_id`ni biladi —
`?session=` parametrni hech narsaga bog'lay olmaydi, продление ishlamaydi.
Qo'shimcha: uch xil URL manba — `Constants.Qr.PaymentBaseUrl` (clubpay.justix.uz/qr),
`FreezeViewModel.cs:41-42` va `ActiveSessionViewModel.cs:60` da hardcoded
`pay.clubpay.uz` (magic string — CLAUDE.md buzilishi).

**Qabul mezonlari:**
- Uzaytirish/freeze QR URL'ida `CoreSessionId` ("N" format) + `external_pc_id` bo'lsin
- Barcha QR bazaviy URL'lari bitta joydan (config, `appsettings` orqali override
  qilinadigan, default `Constants.Qr`da) olinsin
- Hardcoded `pay.clubpay.uz` satrlari yo'qolsin
- Mavjud testlar o'tsin, QR URL yasashga unit test qo'shilsin

**Prompt:**
```text
docs/tuzatish-rejasi.md dagi T1 vazifani bajar. Muammo: uzaytirish/freeze QR kodlari
Billing'ga notanish bo'lgan lokal session GUID ishlatadi. MainViewModel.cs:80 da
CurrentSession.Id o'rniga CoreSessionId uzatilsin, ActiveSessionViewModel.Sync ham
CoreSessionId ishlatsin (Session modelida bor). QR URL'iga external_pc_id ham qo'shilsin
(masalan ?pc=...&session=...). FreezeViewModel va ActiveSessionViewModel'dagi hardcoded
"https://pay.clubpay.uz" magic stringlarni olib tashla — bazaviy URL Constants.Qr'da
tursin va IConfiguration orqali override qilinadigan bo'lsin. QR URL yasash mantiqini
alohida testlanadigan joyga chiqar va unit test yoz. Oxirida dotnet build (0 warning)
va dotnet test o'tishini tasdiqla.
```

### [x] T2. `command_failed` eventi + heartbeat'dagi real qiymatlar

**Muammo:** kontrakt §5.0 bo'yicha `command_failed` eventi bor, lekin agent uni hech
qachon yubormaydi (`CommandDispatcherService` xatoni faqat `command_result`da
qaytaradi). `SessionCoordinatorService.cs:490` da heartbeat `ControllersSeen: 1,
ServerReachable: true` — hardcoded. Bu ТЗ §9 "удержание включения" mexanizmini
imkonsiz qiladi (E2E-18).

**Qabul mezonlari:**
- Komanda xato bilan tugaganda `command_failed` eventi (command_id, external_pc_id,
  error_code bilan) outbox orqali yuborilsin
- `ServerReachable` hech bo'lmasa WebSocket ulanish holatidan
  (`IControllerChannel.ConnectionState`) olinsin
- `ControllersSeen` hozircha 1 qolsa ham, hardcode emas — kanal holatidan hisoblansin
  (ulanmagan bo'lsa 0)
- Unit testlar qo'shilsin

**Prompt:**
```text
docs/tuzatish-rejasi.md dagi T2 vazifani bajar. Kontrakt v1.2 §5.0 bo'yicha
command_failed eventini implement qil: CommandDispatcherService'da komanda error bilan
tugaganda (SessionCommandException yoki internal error) command_result'dan tashqari
command_failed eventi ham outbox'ga yozilsin (Constants.ControllerChannel.EventName.
CommandFailed allaqachon bor, ControllerEvents.cs'da payload record kerak bo'lishi
mumkin). SessionCoordinatorService.PublishHeartbeatAsync'dagi hardcoded
ControllersSeen:1/ServerReachable:true ni IControllerChannel.ConnectionState asosidagi
real qiymatlarga almashtir (Connected bo'lsa 1/true, aks holda 0/false). DI sikl
ehtiyot bo'l: coordinator kanalga allaqachon bog'langan, yangi bog'liqlik kiritma.
Ikkala o'zgarishga unit test yoz. dotnet build (0 warning) va dotnet test o'tsin.
```

### [x] T3. Integration testlarni tuzatish (URL-ACL muammosi)

**Muammo:** `tests/ClubPay.Agent.TestHarness/FakeControllerServer.cs:42` HttpListener'ni
`http://+:{port}/` prefiksi bilan ochadi — Windows'da bu admin huquq / `netsh urlacl`
talab qiladi. Natijada 4 ta integration test har doim "Access is denied" bilan yiqiladi.

**Qabul mezonlari:**
- `FakeControllerServer` `http://localhost:{port}/` prefiks ishlatsin (localhost
  prefiksi URL-ACL talab qilmaydi)
- 4 ta ControllerChannelIntegrationTests testi admin huquqisiz o'tsin
- Barcha 59 test yashil

**Prompt:**
```text
docs/tuzatish-rejasi.md dagi T3 vazifani bajar. tests/ClubPay.Agent.TestHarness/
FakeControllerServer.cs HttpListener prefiksini "http://+:{port}/" dan
"http://localhost:{port}/" ga o'zgartir — hozir 4 ta integration test admin huquq
yo'qligidan "Access is denied" bilan yiqilyapti. Agar WebSocket client URL yasashda ham
moslash kerak bo'lsa, moslashtir. Oxirida dotnet test ClubPay.Agent.slnx to'liq yashil
ekanini ko'rsat.
```

---

## P1 — V1 scope'ni yopish (asosiy ish)

### [x] T4. Arxitektura refactoring: DI siklini yo'q qilish + outbox telemetriya filtri

**Muammo (2 ta bog'liq):**
1. `ControllerChannelService.cs:37-41` — Channel→Dispatcher→Coordinator→Channel DI sikli
   `IServiceProvider` service-locator bilan yashirilgan. Coordinator'ning
   `PublishEventAsync` chaqiruvlari aslida faqat outbox'ga enqueue qiladi — demak
   Coordinator `IControllerChannel`ga emas, to'g'ridan-to'g'ri `IControllerOutbox`ga
   bog'lansa sikl arxitektura darajasida yo'qoladi.
2. `AgentStateRepository` har heartbeat/pc_state_changed'ni diskka yozadi va butun
   state faylini qayta yozadi. Bir kunlik oflayn = ~2900 eskirgan heartbeat persist
   bo'lib, reconnect'da hammasi yuboriladi. ТЗ §9: "приоритет: деньги/сессии выше
   телеметрии", limit + alert.

**Qabul mezonlari:**
- `SessionCoordinatorService` `IControllerChannel`ga bog'lanmasin (event yozish uchun) —
  event enqueue `IControllerOutbox` (yoki yangi tor interfeys) orqali
- `ControllerChannelService`dagi `IServiceProvider` service-locator olib tashlansin
- Heartbeat/pc_state_changed telemetriya eventlari diskka persist qilinmasin — faqat
  jonli ulanishda yuborilsin (yoki oflaynda faqat oxirgisi saqlanib eskisi o'chirilsin)
- Pul/sessiya eventlari (session_started/extended/ended) avvalgidek persist bo'lsin
- Outbox hajmiga limit + limitdan oshsa log-warning
- Mavjud testlar moslansin, yangi holatlarga test qo'shilsin

**Prompt (avval Plan mode!):**
```text
Avval reja tuz, kodga tegma. docs/tuzatish-rejasi.md dagi T4 vazifa: (1)
ControllerChannelService'dagi IServiceProvider service-locator workaround'ini yo'q
qilish — SessionCoordinatorService event yozish uchun IControllerChannel emas,
to'g'ridan-to'g'ri IControllerOutbox'ga bog'lansin (PublishEventAsync baribir faqat
outbox'ga enqueue qiladi + signal beradi; signal mexanizmini qanday saqlashni rejada
ko'rsat). (2) AgentStateRepository outbox'ida telemetriya (heartbeat, pc_state_changed)
diskka persist qilinmasin — ТЗ §9 bo'yicha pul/sessiya eventlari prioritet, telemetriya
oflaynda to'planmasligi kerak; outbox hajmiga limit + oshganda warning log. Rejang
tayyor bo'lgach ko'rsat, tasdiqlaganimdan keyin bajarasan. Oxirida barcha testlar
o'tishi va DI konteyner real qurilishini (App.xaml.cs kompozitsiyasi) tekshirishing
shart.
```

### [x] T5. VoucherService — Ed25519 oflayn vaucher (ТЗ §6/10/13)

**Muammo:** ТЗ V1 ning to'laqonli qismi bo'lgan vaucher kodda 0% — na servis, na NSec
paketi, na kiritish maydoni. `Constants.Voucher.MinRemainingSeconds = 300` ham ТЗ'ga
zid (ТЗ: "минимума нет", istalgan musbat qoldiq, sekund aniqlikda).

**Qabul mezonlari:**
- `ClubPay.Agent.Core`da `IVoucherService` + model: vaucher = imzolangan token
  (payload: voucher_id/nonce, external_pc_id yoki club_id, seconds, expires_at)
- `NSec.Cryptography` bilan Ed25519 imzo tekshirish — **faqat public key** agentda
  (config orqali), private key hech qachon agentga kelmaydi
- Lokal tekshirish tarmoqsiz ishlaydi; nonce replay-himoya (ishlatilgan voucher_id'lar
  `AgentStateRepository`da persist qilinadi)
- `MinRemainingSeconds` konstantasi o'chirilsin (ТЗ'ga zid)
- Vaucher qabul qilinsa lokal sessiya boshlanadi va `session_started` eventi outbox
  orqali Billing'ga yuboriladi (grant_id = voucher_id — idempotentlik saqlanadi)
- To'liq unit test: to'g'ri imzo, buzilgan imzo, muddati o'tgan, boshqa PC uchun
  berilgan, takror ishlatish

**Prompt (avval Plan mode!):**
```text
Avval reja tuz, kodga tegma. docs/tuzatish-rejasi.md dagi T5: ТЗ §6/10/13 bo'yicha
oflayn vaucher servisi. Talablar: IVoucherService Core'da; NSec.Cryptography (Ed25519)
bilan faqat public-key lokal tekshirish (public key config'dan); vaucher payload'ida
nonce/voucher_id + PC yoki club bog'lash + seconds + TTL; ishlatilgan voucher_id'lar
AgentStateRepository'da persist qilinib replay bloklanadi; muvaffaqiyatli vaucher
lokal sessiya boshlaydi (SessionCoordinator orqali, grant_id=voucher_id, shunda
mavjud idempotentlik ishlaydi) va session_started outbox'ga tushadi; Constants.Voucher.
MinRemainingSeconds o'chiriladi (ТЗ: minimum yo'q). Token formatini (masalan
base64url(payload).base64url(signature)) va test-vektor yaratish usulini rejada taklif
qil — testlarda haqiqiy Ed25519 juftlik bilan imzolangan namunalar kerak. MethodName_
Scenario_ExpectedResult uslubida to'liq test to'plami yoz. Reja tasdiqlangach bajar.
```

### [x] T6. LockScreen'ga yagona kod-kiritish maydoni + menejer master-kodi (ТЗ §7)

**Muammo:** ТЗ §7: "Видимая подсказка «Нажмите Enter, чтобы ввести код». Единое поле
для клиентского ваучера и мастер-кода менеджера (агент распознаёт тип)". Hozir
LockScreen faqat passiv QR — kod kiritishning umuman iloji yo'q. T2/oflayn holatda
PC ni ochib bo'lmaydi.

**Qabul mezonlari:**
- LockScreenView'da Enter bosilganda ochiladigan bitta input maydoni
- Kiritilgan kod turi avtomatik aniqlanadi: vaucher (T5 formati) yoki menejer
  master-kodi
- Master-kod: TOTP-uslub yoki imzolangan bir-martalik token (ТЗ §7 talabi
  "криптостойкий и одноразовый/сменяемый") — ishlatilgan kodlar persist qilinadi
- Xato kodda foydalanuvchiga texnik emas, oddiy xabar (CLAUDE.md qoidasi), xato logga
  yoziladi
- Master-kod bilan ochish audit-event sifatida outbox'ga yoziladi (kim/qachon/PC)
- ViewModel'da biznes mantiq yo'q — tekshirish servislarda; unit testlar

**Prompt:**
```text
docs/tuzatish-rejasi.md dagi T6 vazifani bajar (T5 tugagan bo'lishi shart). ТЗ §7
bo'yicha LockScreen'ga yagona kod-kiritish oqimi: Enter bosilganda input ochiladi
("Kod kiritish uchun Enter bosing" ko'rinishidagi hint doim ko'rinadi), kiritilgan kod
avval IVoucherService bilan vaucher sifatida, mos kelmasa menejer master-kodi sifatida
tekshiriladi. Master-kod uchun yangi IMasterCodeService yoz: TOTP-uslub (vaqt oynali,
club-secret'dan HMAC) yoki Ed25519-imzolangan bir-martalik token — qaysi biri joriy
arxitekturaga soddaroq mos kelishini tanla va sababini tushuntir. Ishlatilgan kodlar
replay'ga qarshi persist qilinsin, master-kod bilan ochish audit eventi sifatida
outbox'ga yozilsin. ViewModel faqat UI holati — tekshirish servislarda. Unit testlar
bilan. dotnet build/test yashil bo'lsin.
```

### [ ] T7. Naqd to'lov audit logi (Admin, ТЗ §11)

**Muammo:** `CashPaymentViewModel`da `ILogger` yo'q, PIN faqat uzunlik bo'yicha
tekshiriladi, naqd sessiya hech qayerda qayd etilmaydi. ТЗ §11: har naqd sessiya
"ID менеджера, ПК, длительность, сумма, время" + sabab kodi bilan yoziladi —
antikraja talabi, MVP'da majburiy.

**Qabul mezonlari:**
- Naqd sessiya yozuvi: menejer ID, PC, davomiylik, summa (long tiyin!), vaqt (UTC),
  sabab kodi — lokal append-only faylga persist + ILogger structured log
- Pul faqat `long` tiyin (hozirgi `decimal` ishlatilgan joylar tuzatiladi) va
  `Constants.Money.FormatSom` bilan formatlash (ad-hoc formatlagichlar yo'qoladi)
- PIN tekshirish hech bo'lmaganda config'dagi hash bilan solishtiriladi (keyin real
  servisga ulanadi)
- Unit testlar

**Prompt:**
```text
docs/tuzatish-rejasi.md dagi T7 vazifani bajar. ClubPay.Agent.Admin'dagi
CashPaymentViewModel'ga ТЗ §11 talab qilgan audit logni qo'sh: har naqd sessiya
(menejer ID, PC, davomiylik, summa long tiyinda, UTC vaqt, sabab kodi) append-only
lokal JSON-lines faylga yoziladi + ILogger structured log. Yangi ICashAuditService
Core'da, implementatsiya Admin'da. Pul hisob-kitobidagi decimal ishlatilgan joylarni
(Tariff.PriceSom, CashPaymentViewModel.AmountLabel atrofini tekshir) long tiyinga
o'tkaz va formatlashni Constants.Money.FormatSom'ga birlashtir — CLAUDE.md qoidasi.
PIN tekshirishni config'dagi hash bilan solishtirishga o'tkaz (TODO comment o'rniga).
Unit testlar yoz. dotnet build/test yashil.
```

---

## P2 — Pilot oldidan

### [ ] T8. Monotonik taymer (ТЗ v5.3 review talabi)

**Muammo:** ТЗ: "агент ведёт таймер по монотонному локальному времени от
granted_seconds, ends_at — сверка". Kod hamma joyda devor soati (`DateTime.UtcNow`) —
soat NTP/qo'lda o'zgarsa sessiya noto'g'ri tugaydi.

**Qabul mezonlari:**
- Qolgan vaqt hisoblash monotonik manbadan (`Environment.TickCount64` /
  `Stopwatch.GetTimestamp`), `EndsAtUtc` faqat sverka/persist uchun
- Uyqudan uyg'onish va restart holatlarida to'g'ri tiklanish (monotonik soat
  uyquda/restartda davom etmasligini hisobga olish — shunda `EndsAtUtc` bilan sverka)
- `ISystemClock` kengaytiriladi yoki yangi `IMonotonicClock` — testlarda soxtalash oson
  bo'lsin
- Soat sakrashi stsenariylariga testlar

**Prompt (avval Plan mode!):**
```text
Avval reja tuz. docs/tuzatish-rejasi.md dagi T8: sessiya taymerini monotonik vaqtga
o'tkazish (ТЗ talabi: "таймер по монотонному локальному времени от granted_seconds,
ends_at — сверка"). Session modelidagi RemainingSeconds/ElapsedSeconds hozir devor
soatiga tayanadi. Reja quyidagilarni hal qilsin: monotonik manba (Environment.
TickCount64) abstraktsiyasi; uyqu (S3) va protsess restartida monotonik hisob uzilishi
— bu holatlarda EndsAtUtc bilan sverka qilib davom etish; SessionCoordinator'dagi tick/
freeze/resume oqimlariga ta'siri; testlarda vaqtni boshqarish. Reja tasdiqlangach
bajar, soat oldinga/orqaga sakragan stsenariylarga test yoz.
```

### [ ] T9. Watchdog + anti-obход kuchaytirish (E2E-26)

**Muammo:** agent oddiy WPF ilova — o'ldirilsa hech kim qayta ko'tarmaydi.
`KioskLockService.cs:66` dagi Ctrl+Alt+Del "bloklash" ishlamaydi (SAS winlogon
darajasida, LL hook ko'rmaydi). Safe Mode / ikkinchi Windows user himoyasi yo'q.
CLAUDE.md asl rejasi: "Windows Service + WPF".

**Qabul mezonlari:**
- Windows Service (watchdog): UI protsessi o'lsa qayta ishga tushiradi; service'ni
  to'xtatish oddiy userga taqiqlangan
- Ctrl+Alt+Del haqidagi noto'g'ri comment/kod tuzatiladi (halol hujjatlashtiriladi:
  SAS bloklanmaydi, lekin Task Manager registry + hook bilan yopiq)
- Safe Mode himoyasi hujjatlashtiriladi (to'liq yechim OS-siyosat darajasida —
  BCDEdit/GPO onboarding skriptiga kiradi)
- `--install` service'ni ham o'rnatadi

**Prompt (avval Plan mode!):**
```text
Avval reja tuz. docs/tuzatish-rejasi.md dagi T9: agent uchun watchdog Windows Service.
Talablar: alohida kichik service-protsess (yangi loyiha src/ClubPay.Agent.Watchdog
yoki Client ichida --service rejim — taqqoslab taklif qil) UI protsessini kuzatadi,
o'lsa qayta ishga tushiradi, ketma-ket crash'larda backoff; InstallService --install
service'ni ham ro'yxatdan o'tkazadi (sc create, auto-start, recovery options).
KioskLockService.cs:66 dagi Ctrl+Alt+Del bloklash ishlamaydi (SAS LL hook'ka kelmaydi)
— kod/commentni halol holatga keltir: SAS bloklanmasligini hujjatlashtir, buning
o'rniga DisableTaskMgr/DisableLockWorkstation/DisableChangePassword registry
siyosatlarini qo'llash variantini baholab qo'sh. Safe Mode va ikkinchi user
himoyasining chegaralarini docs/ga qisqa yozib qo'y. Reja tasdiqlangach bajar.
```

### [ ] T10. O'lik kodni tozalash

**Muammo:** `Constants.Controller` (legacy HTTP polling) + Admin'dagi
`AgentEndpointServer`/`PendingSessionStore` — Client endi ishlatmaydi;
`SendTimeoutSeconds`/`MaxSendRetries` konstantalari e'lon qilingan-u ishlatilmaydi;
UI'da hardcoded "NEXUS ARENA"/"PC-12"/"Sardor" defaultlar.

**Prompt:**
```text
docs/tuzatish-rejasi.md dagi T10 vazifani bajar: (1) legacy HTTP polling yo'lini
o'chir — Constants.Controller, Admin'dagi AgentEndpointServer, IPendingSessionStore,
PendingSessionStore va ularga bog'liq DI ro'yxatlarini olib tashla (Client bu endpoint'ni
endi chaqirmaydi, Constants.cs:46 comment'i buni tasdiqlaydi; avval haqiqatan hech kim
ishlatmasligini grep bilan tekshir). (2) Constants.ControllerChannel'dagi ishlatilmagan
SendTimeoutSeconds/MaxSendRetries — yo kontrakt §1 bo'yicha haqiqiy 10s ack-timeout
sifatida implement qil, yo o'chir (qaysi biri to'g'riligini kontraktga qarab hal qil va
tushuntir). (3) ViewModel'lardagi hardcoded "NEXUS ARENA"/"PC-12"/"Sardor" defaultlarni
config'dan olinadigan qil. dotnet build/test yashil bo'lsin.
```

### [ ] T11. Litsenziya/hardware-lock — agent tomoni (docs/license-subscription-talablari.md)

**Muammo:** backend talablari yozilgan (2026-07-21), lekin agent tomonida hech narsa
yo'q: `apply_license` komandasi, hardware fingerprint hisoblash, heartbeat'ga 3 yangi
maydon, `license_expired`/`hardware_mismatch` error kodlari.

**Eslatma:** backend `apply_license` berishga tayyor bo'lmaguncha bu vazifani
boshlamang — kontrakt kengaytmasi ikki tomonlama kelishilishi kerak.

**Prompt (avval Plan mode!):**
```text
Avval reja tuz. docs/license-subscription-talablari.md ni to'liq o'qi va agent
tomonini implement qil: (1) hardware fingerprint hisoblash (motherboard serial + disk
serial + CPU ID + asosiy NIC MAC hash'i, hujjat §3); (2) apply_license komandasi —
Ed25519-imzolangan litsenziya obyektini lokal tekshirish (T5'dagi imzo-tekshirish
infratuzilmasini qayta ishlatish) va persist; (3) heartbeat payload'iga
hardware_fingerprint/license_valid_until/license_status maydonlari; (4) yangi
error_code'lar license_expired/hardware_mismatch — litsenziya yaroqsiz bo'lsa
start_session/extend_session rad etiladi, lekin MAVJUD sessiya to'xtatilmaydi (hujjat
§7 taklifi). Reja tasdiqlangach bajar, to'liq unit testlar bilan.
```

### [ ] T12. Admin taqdiri bo'yicha qaror (kod emas — qaror!)

ТЗ §11/23 bo'yicha menejer paneli — kontroller beradigan **web-PWA**, WPF emas.
Backend jamoasi bilan kelishing:

- **Variant A:** panel PWA bo'ladi (ТЗ'ga mos) → WPF Admin faqat dev-vosita,
  unga boshqa katta ish sarflanmaydi (T7 audit logi baribir kerak — u naqd oqim
  qayerda bo'lsa o'sha yerga ko'chadi).
- **Variant B:** WPF Admin rasmiy T2-avtoritet bo'ladi → unga kontroller-engine
  (agentlar bilan WS hub, to'lov opros, grant berish, outbox) kerak — bu katta
  alohida loyiha, backend bilan birgalikda rejalashtiriladi.

Qaror qabul qilinmaguncha Admin'ga T7'dan boshqa ish qilmaslik tavsiya etiladi.

---

## Bajarish tartibi (qisqa xulosa)

```
T1 → T2 → T3            (P0: 1-2 kun, E2E'ni ochadi)
T4 → T5 → T6 → T7       (P1: V1 scope, T4 T5'dan oldin SHART)
T8, T9, T10             (P2: pilot oldidan, tartib erkin)
T11                     (backend tayyor bo'lganda)
T12                     (qaror — istalgan vaqtda, lekin tezroq yaxshi)
```
