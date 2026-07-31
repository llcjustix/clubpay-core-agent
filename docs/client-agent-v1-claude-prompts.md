# ClubPay Client Agent V1 — Claude uchun implementatsiya promptlari

Ushbu promptlarni Claude'ga **bittadan**, quyida tavsiya etilgan tartibda yuborish kerak.

Har bir vazifada:

- `AGENTS.md`, `CLAUDE.md` va `docs` ichidagi V1 talablariga amal qilish;
- mavjud, vazifaga aloqasi bo'lmagan o'zgarishlarni saqlash;
- V1 doirasidan chiqmaslik;
- o'zgarishlarni testlar bilan qoplash;
- yakunda o'zgargan fayllar va test natijalarini ko'rsatish talab qilinadi.

## 1. Windows Service va watchdog

```text
ClubPay Agent repository'sini o'rganib chiq. AGENTS.md, CLAUDE.md va docs ichidagi V1 talablariga amal qil.

Client Agent hozir oddiy WPF WinExe va Startup/Shell orqali ishga tushadi. Uni production uchun Windows Service + UI arxitekturasiga o'tkaz:

- Windows Service kompyuter yoqilganda avtomatik ishga tushsin.
- Service Client UI process'ini nazorat qilsin.
- UI crash bo'lsa yoki o'ldirilsa, service uni qayta ishga tushirsin.
- Bir vaqtning o'zida faqat bitta UI instance ishlasin.
- Service va UI o'rtasida xavfsiz IPC ishlat.
- Install/uninstall jarayoni service'ni to'g'ri ro'yxatdan o'tkazsin va o'chirsin.
- Mavjud `--install`, `--uninstall`, `--status` imkoniyatlarini buzma.
- Windows session/login holatini hisobga ol: service to'g'ridan-to'g'ri Session 0 ichida WPF ochmasin.
- Barcha Windows-specific operatsiyalar abstraction orqali testlanadigan bo'lsin.
- Unit testlar yoz.

Avval mavjud arxitekturani tekshir, keyin minimal va xavfsiz yechimni implement qil. Yakunda o'zgargan fayllar, arxitektura, install komandalar va test natijalarini yoz.
```

## 2. Anti-bypass himoyasi

```text
ClubPay Agent Client'dagi anti-bypass himoyasini V1 talablari darajasiga yetkaz.

Hozir KioskLockService keyboard hook va ayrim HKCU policy'lardan foydalanadi. Quyidagilarni xavfsiz va qaytariladigan tarzda implement qil:

- Task Manager orqali agentni o'chirishni cheklash.
- Agent service'ini oddiy foydalanuvchi to'xtata olmasligi.
- Ikkinchi Windows user/session orqali unlocked desktop olishning oldini olish.
- Agent UI yopilsa watchdog orqali tiklanishi.
- Windows Explorer/shell bypass'ini nazorat qilish.
- Safe Mode bo'yicha Windows imkoniyatlari chegarasini aniqlab, amalga oshirish mumkin bo'lgan himoyani qo'shish.
- Install vaqtida qo'yilgan barcha policy va registry o'zgarishlari uninstall vaqtida avvalgi qiymatiga qaytarilsin.
- Kompyuterni ishlatib bo'lmaydigan holatga keltiradigan agressiv o'zgarishlar qilma.
- Registry va service operatsiyalarini abstraction orqali unit test qil.
- Manual Windows acceptance checklist yarat.

Faqat V1 Client Agent xavfsizligiga tegishli qismlarni o'zgartir. Yakunda qaysi bypass'lar yopilgani va qaysilari OS sabab to'liq yopilmasligini aniq yoz.
```

## 3. Bootstrap va provisioning

```text
ClubPay Agent Client uchun V1 bootstrap/provisioning mexanizmini implement qil.

Hozir controller URL, agent token, PC ID, public key va branding `appsettings.json` ichida qo'lda saqlanadi. Docs va contract'ni o'rganib, quyidagilarni bajar:

- Birinchi ishga tushishda provisioning ma'lumotlarini xavfsiz qabul qilish.
- Controller bootstrap endpoint'dan agent konfiguratsiyasini olish.
- external_pc_id, club_id, zone, branding, controller URL, agent token, voucher public key va manager-code public key'ni qo'llab-quvvatlash.
- Secret/token'larni oddiy plaintext appsettings'da saqlamaslik; Windows DPAPI yoki mos xavfsiz storage ishlatish.
- Bootstrap muvaffaqiyatli bo'lsa konfiguratsiyani atomik saqlash.
- Controller vaqtincha ishlamasa oxirgi valid konfiguratsiya bilan ishlash.
- Invalid yoki incomplete config'da fail-closed ishlash.
- Token rotation va revoked token holatini hisobga olish.
- Bootstrap uchun timeout, retry/backoff va structured logging qo'shish.
- Wire format va validation'ni aniq model bilan yozish.
- Unit va integration testlar qo'shish.

Agar controller tomonda endpoint yo'q bo'lsa, repository ichidagi controller implementatsiyasiga mos endpoint qo'sh. Default `REPLACE_WITH_CORE_TOKEN` bilan production rejimida ishga tushishga yo'l qo'yma.
```

## 4. `apply_config` va dinamik konfiguratsiya

```text
Client Agent'da V1 uchun `apply_config` command'ini to'liq implement qil.

Hozir CommandDispatcherService `apply_config is not supported` qaytaradi. Quyidagilar kerak:

- Contract'ga mos typed `ApplyConfigPayload` yarat.
- `config_version` majburiy bo'lsin.
- Eski yoki takroriy version idempotent no-op bo'lsin.
- Yangi config controller/bootstrap endpoint'dan olinsin yoki command payload'da keladigan contract asosida qo'llansin.
- Branding, club name, PC ID display, zone, payment URL, controller settings, 10/5/1 warning threshold, grace va idle timeout yangilanishini qo'llab-quvvatla.
- Config validatsiyadan o'tmaguncha joriy config'ni almashtirma.
- Config atomik saqlansin va restart'dan keyin tiklansin.
- Qaysi sozlamalar restart talab qilishi aniq boshqarilsin.
- UI ViewModel'lari config o'zgarganda yangilansin.
- Xato holatida to'g'ri `command_result` va `command_failed` qaytar.
- Unit testlar yoz: new version, duplicate, older version, invalid config, persistence va rollback.

Mavjud session'ni config yangilanishi buzmasin.
```

## 5. Monotonic timer va `ends_at`

```text
Client Agent session timer'ini V1 contract'ga to'liq moslashtir.

Hozir Session `DateTime.UtcNow - StartedAtUtc` orqali vaqt hisoblaydi va `EndsAtUtc` oddiy tick'da qat'iy limit sifatida ishlatilmaydi.

Quyidagilarni implement qil:

- Process ishlayotgan vaqtda countdown uchun monotonic clock ishlat.
- `granted_seconds` asosiy lokal budget bo'lsin.
- `ends_at` session hech qachon keragidan ortiq davom etmasligi uchun yuqori chegara/cross-check bo'lsin.
- System clock oldinga yoki orqaga o'zgarsa session bepul uzayib ketmasin.
- Restart'dan keyin persisted UTC qiymatlar orqali xavfsiz recovery qilinsin.
- Sleep/resume va crash/restart holatlari to'g'ri ishlasin.
- Extend paytida yangi remaining va ends_at to'g'ri hisoblanishi kerak.
- Consumed/remaining qiymatlari manfiy yoki granted'dan katta bo'lmasin.
- ISystemClock bilan birga testlanadigan monotonic abstraction qo'sh.
- Clock rollback, clock jump, restart, sleep/resume, extend va expiry uchun unit testlar yoz.

Public contract'ni zaruratsiz buzma. Timer bo'yicha qabul qilingan formulani kod kommentida aniq tushuntir.
```

## 6. `command_id` idempotentligi

```text
Client Agent'da contract talab qiladigan `command_id` idempotentligini implement qil.

Hozir faqat start/extend uchun `grant_id` saqlanadi. Kerakli o'zgarishlar:

- Har bir bajarilgan command uchun `command_id`, command name va serialized `command_result` SQLite'da saqlansin.
- Xuddi shu `command_id` qayta kelsa command qayta bajarilmasin, avvalgi natija qaytarilsin.
- Bir xil `command_id`, lekin boshqa command name/payload kelsa conflict/invalid_state qaytarilsin.
- Command natijasi state mutation bilan imkon qadar bitta atomik transaction'da saqlansin.
- Crash command bajarilgandan keyin, javob yuborilishidan oldin yuz bersa, restart'dan keyin duplicate qayta bajarilmasin.
- Retention va database cleanup siyosati bo'lsin.
- `grant_id` idempotentligini saqla.
- Start, extend, end, lock, unlock, sleep va set_repair duplicate holatlarini test qil.
- Concurrent bir xil command'lar uchun race-condition testi yoz.

Mavjud contract JSON formatini buzma va duplicate natijaning qanday qaytarilishini testlarda aniq ko'rsat.
```

## 7. Command payload validation

```text
Client Agent'dagi barcha controller command payload'larini qat'iy validatsiya qil.

Quyidagi xavflarni yop:

- `external_pc_id` aynan joriy agent ExternalPcId'iga teng bo'lishi kerak.
- `extend_session` va `end_session` dagi `core_session_id` joriy session ID'siga teng bo'lishi kerak.
- `grant_id`, `command_id` va kerakli string maydonlar bo'sh bo'lmasin.
- `granted_seconds` va `added_seconds` musbat va xavfsiz limit ichida bo'lsin.
- `start_at` va `ends_at` mantiqan mos bo'lsin.
- Noto'g'ri enum yoki zone qiymatlari indamay fallback qilinmasin.
- Boshqa PC uchun kelgan lock/unlock/sleep/repair command bajarilmasin.
- Invalid payload'da hech qanday state mutation bo'lmasin.
- Contract'ga mos error code va `command_failed` event qaytarilsin.
- Log'da token yoki maxfiy payload chiqmasin.
- Har bir command uchun positive va negative unit testlar yoz.

Validation'ni bitta markaziy, qayta ishlatiladigan qatlamga joylashtir. ViewModel ichiga biznes validation qo'shma.
```

## 8. Reliable outbox va event ACK

```text
ClubPay Agent event outbox'ini reliable delivery darajasiga olib chiq.

Hozir event WebSocket SendAsync muvaffaqiyatli tugashi bilan outbox'dan o'chiriladi. Controller event'ni persist qilganini tasdiqlamasa event yo'qolishi mumkin.

Quyidagilarni implement qil:

- Agent event yuborgandan keyin controller application-level ACK qaytarsin.
- ACK `event_id` bilan bog'langan bo'lsin.
- Agent faqat valid ACK kelgandan keyin event'ni SQLite outbox'dan o'chirsin.
- ACK kelmasa timeout'dan so'ng event pending holatda qolsin va reconnect'da qayta yuborilsin.
- Controller `event_id` bo'yicha dedup qilsin.
- Duplicate event uchun ham ACK qaytarsin.
- Bir vaqtning o'zida bir nechta pending event bo'lsa ordering saqlansin.
- `session_started`, `session_extended`, `session_ended` kabi muhim event'lar hech qachon avtomatik drop qilinmasin.
- Telemetry coalescing saqlanishi mumkin.
- Agent, controller, test harness va contract modellari birgalikda yangilansin.
- Disconnect-before-ACK, duplicate delivery, reconnect va delayed ACK integration testlarini yoz.

Wire protocol o'zgarishini qisqa hujjatlashtir va backward compatibility bo'yicha qarorni aniq yoz.
```

## 9. Uz/Ru lokalizatsiya

```text
ClubPay Agent Client UI'da V1 uchun haqiqiy uz/ru lokalizatsiyani implement qil.

Hozir matnlar XAML ichida uzbek, rus va ingliz tillarida aralash hard-coded yozilgan.

Talablar:

- Barcha user-facing matnlarni ResourceDictionary yoki .resx localization tizimiga ko'chir.
- Uzbek va Russian alohida to'liq tarjima bo'lsin.
- Default til provisioning/config orqali belgilansin.
- Agent shell ichida yoki manager config orqali tilni almashtirish mumkin bo'lsin.
- Til almashtirilganda restart talab qilinmasin.
- Timer, zone, error, lock screen, freeze screen, launcher va notification matnlari lokalizatsiya qilinsin.
- Texnik error foydalanuvchiga ko'rsatilmasin.
- Fallback tili Uzbek bo'lsin.
- UI layout uzun ruscha matnlarda buzilmasin.
- Hard-coded user-facing matn qolmaganini test yoki static check bilan tekshir.
- Localization selection va fallback uchun unit testlar yoz.

Brand nomi, PC ID va dynamic qiymatlarni tarjima qilma.
```

## 10. Session'dan keyingi clean state

```text
Client Agent'da V1 session tugagandan keyingi clean-state mexanizmini kuchaytir.

Hozir asosan launcher orqali kuzatilgan process'lar yopiladi. Quyidagilarni xavfsiz implement qil:

- Agent orqali ishga tushirilgan barcha game/launcher process'larini ishonchli kuzatish.
- Child process tree va launcher handoff holatlarini hisobga olish.
- Session end, timeout, manager end, crash recovery va restart'da cleanup bajarish.
- Faqat allowlist/config'da ko'rsatilgan process va kataloglarga tegish.
- System process yoki sessiondan oldin ishlayotgan foreign process'larni o'ldirma.
- Configurable cleanup actions yarat: process close, process kill fallback va ruxsat berilgan temporary profile directory cleanup.
- Fayl o'chirish faqat aniq konfiguratsiyalangan xavfsiz kataloglarda bajarilsin.
- Cleanup xatosi PC'ni unlocked holatda qoldirmasin.
- Cleanup audit log'ga yozilsin.
- Steam/game account auto-logout V3 ekanini hisobga olib, V1 scope'dan chiqma.
- Process tree, pre-existing process, crash recovery va cleanup failure testlarini yoz.

Destructive cleanup'dan oldin path safety validation bo'lishi shart.
```

## 11. Production logging va diagnostika

```text
Client Agent uchun V1 production logging va diagnostika tizimini qo'sh.

Hozir logging asosan Console/Debug provider bilan cheklangan.

Talablar:

- Local rolling file log qo'sh.
- Log fayllari ProgramData ichidagi ClubPay Agent katalogida saqlansin.
- Size/day rotation va retention limiti bo'lsin.
- Token, voucher, manager code, password va boshqa secret'lar log qilinmasin.
- Startup, provisioning, controller connection, command, session transition, sleep/resume, cleanup va crash hodisalari structured log qilinsin.
- Global unhandled exception va unobserved task exception handler qo'sh.
- Crash diagnostikasi saqlansin, lekin agent fail-open bo'lmasin.
- `--status` komandasi service holati, UI holati, config version, controller connection va oxirgi xatoni ko'rsatsin.
- Disk to'lib qolsa logging agent ishini to'xtatmasin.
- Logger konfiguratsiyasi test qilinsin.
- Secret redaction uchun testlar yoz.

Minimal va qo'llab-quvvatlanadigan logging yechimini tanla; keraksiz katta dependency qo'shma.
```

## 12. Windows acceptance testlari

```text
ClubPay Agent V1 uchun real Windows acceptance test to'plamini tayyorla.

Bu vazifada bajarilmagan hardware testlarni "passed" deb belgilama. Quyidagilarni yarat:

- `docs/client-agent-v1-acceptance.md` checklist.
- Imkon qadar xavfsiz PowerShell test/helper scriptlari.
- Install/uninstall va service auto-start testi.
- UI process kill → watchdog recovery testi.
- Windows restart → locked screen recovery testi.
- Active session crash/restart recovery testi.
- Task Manager bypass testi.
- Ikkinchi Windows user/session testi.
- Safe Mode bo'yicha manual test.
- S3 sleep/resume va expired session testi.
- Controller disconnect/reconnect va outbox sync testi.
- Fizik PC'da WoL testi.
- Payment-to-unlock va command ACK 10-second timeout o'lchovi.
- Har test uchun prerequisite, steps, expected result, evidence/log location va Pass/Fail maydoni.
- Test scriptlari production state'ni buzmasin va cleanup komandalariga ega bo'lsin.

Mavjud E2E hujjatiga reference ber, lekin faqat Client Agent V1 acceptance scope'ini yoz.
```

## 13. `HttpListener` integratsion testlarini tuzatish

```text
ClubPay Agent Client va Admin integratsion testlaridagi `HttpListenerException: Invalid handle` muammosini tuzat.

Hozir FakeControllerServer va ControllerHubService testlari HttpListener sabab ayrim Windows/CI muhitlarida ishlamaydi.

Talablar:

- Muammoning aniq sababini aniqlash.
- Test server'ni barqaror, cross-environment WebSocket server bilan almashtirish; imkon bo'lsa Kestrel asosida qil.
- Dynamic free port allocation race-condition'siz ishlasin.
- Testlar parallel ishlaganda port collision bo'lmasin.
- Server start/stop va disposal deterministic bo'lsin.
- Cancellation va timeout'lar testni osilib qolishdan saqlasin.
- Mavjud WebSocket protocol behavior o'zgarmasin.
- Client'dagi barcha integration testlar ishlasin.
- Admin ControllerHub integration testlari ham ishlasin.
- Disconnect/reconnect va outbox testlarini saqla.
- `dotnet test ClubPay.Agent.slnx` to'liq muvaffaqiyatli o'tishini tekshir.

Testni shunchaki skip qilma va environment-specific workaround bilan cheklanma. Yakunda root cause va test natijalarini yoz.
```

## Tavsiya etilgan bajarish tartibi

1. `HttpListener` integratsion testlarini tuzatish - done
2. Command payload validation - done
3. `command_id` idempotentligi - done
4. Reliable outbox va event ACK
5. Monotonic timer va `ends_at`
6. Bootstrap va provisioning
7. `apply_config` va dinamik konfiguratsiya
8. Windows Service va watchdog
9. Anti-bypass himoyasi
10. Uz/Ru lokalizatsiya
11. Session'dan keyingi clean state
12. Production logging va diagnostika
13. Windows acceptance testlari
