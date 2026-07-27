# Clubpay — Texnik Topshiriq (TЗ) — to'liq o'zbekcha tarjima

> Manba: `docs/_Clubpay - TЗ.docx`, 1-qism (ТЗ v5.4, iyun 2026).
> Master-hujjat 5 qismdan iborat: 1) TЗ · 2) Billing ↔ Core/Agent kontrakti · 3) E2E test-plan · 4) Dev-stend · 5) Tashqi tomonlarga savollar.
> Bu fayl — **1-qism (TЗ)** ning to'liq tarjimasi.

---

**O'yin klublarini boshqarish tizimi — TЗ (qoralama)**

Holat: ishlab chiqishga qoralama · Versiya: 5.4 (MVP-moliya modeli: splitsiz, har klubga o'z merchant/kassasi, pul to'g'ridan-to'g'ri klubga, monetizatsiya obuna orqali, fiskalizatsiya provayder tomonidan klub kassasi orqali) · Sana: iyun 2026.
Bozor: O'zbekiston · Mohiyati: billing / money-management platformasi, kaputi ostida — kompyuter klublarini boshqarish · Mo'ljal: iCafe8 (Shunwang), SENET, ggLeap, SmartLaunch.

Terminologiya Glossariyga chiqarilgan (24-bo'lim). Huquq/soliq bo'yicha disklamer — 14-bo'lim.

---

## 1. Umumiy ko'rinish

Platforma kompyuter klubini avtomatlashtiradi va klublar, o'yinchilar hamda pullarni bitta tarmoqqa bog'laydi. Klub uchun — qo'lda bajariladigan yumushlarsiz billing va PK nazorati: mijoz o'z telefonidan to'laydi, PK o'zi blokdan chiqadi, egasi har bir sessiya va kassani ko'rib turadi. O'yinchi uchun — butun klublar tarmog'i bo'ylab bitta akkaunt, bitta balans va bitta ilova — u o'yinchini istalgan joyda taniydi. Keyinchalik esa platforma alohida klubdan o'sib chiqadi: klublar marketpleysi, masofaviy o'yin, turnirlar va butun mamlakat bo'ylab o'yinchilar reytingiga ega kibersport hamjamiyati.

Falsafa — kompyuter klubini avtomatlashtirish. Kaput ostida uch qatlam: billing dvigateli, marketpleys va mijoz qulayligi (bitta akkaunt, bitta ilova, ko'p klublar, bron qilish).

Mohiyatan bu billing / money-management platformasi: yadro — pul dvigateli (to'lovlar, depozitlar, holdlar, qaytarishlar, bron depozitlari, klublarga to'lovlar, komissiya, solishtirma/sverka), kompyuterlarni boshqarish esa — kaput ostidagi vertikal.

Tarmoqning killer-fichasi (V2): yagona akkaunt, bitta ilova va bitta balans barcha ulangan klublarda ishlaydi — o'yinchi hisobini bir marta to'ldiradi va tarmoqning istalgan klubida o'ynaydi. Tarmoq effekti (klublar ko'proq → o'yinchiga qimmatliroq → o'yinchilar ko'proq → klublar ulanishni xohlaydi). Bu o'tuvchi (open-loop) hamyonga va shu sababli e-money/hamkor-bank yo'liga bog'liq (6.2, 14-bo'limlar).

**Asosiy arxitektura tamoyillari:**

- **Bulut o'yinni to'xtatmasligi kerak.** «To'ladim → o'ynayapman» bazaviy stsenariysi bizning bulut serverimiz ishlamay qolganda ham ishlaydi. Zanjir: mijoz → bizning platforma (bir sahifali sayt, shakli Rahmat PAY kabi) → to'lov tizimi (Click / Payme / Uzum / kartalar) — har biri bilan to'g'ridan-to'g'ri integratsiya qilamiz; Rahmat PAY faqat UX-flou namunasi bo'lib xizmat qildi, Rahmat servisining o'zini ishlatmaymiz. Tasdiq qaytib keladi: to'lov tizimining webhook'i → bizning server (yoki kontroller o'zi statusni so'rab turadi) → bizning server/kontroller PK ni blokdan chiqaradi. To'lov tizimi kontrollerga to'g'ridan-to'g'ri bormaydi — bizning platformaga javob beradi.
- **To'lov sahifasini bulutimizsiz ham ochish mumkin.** Odatda bir sahifali sayt bulutdan yuklanadi, lekin uni klubning lokal kontrolleri ham bera oladi: klub WiFi'siga ulanishda o'sha to'lov sahifasi avtomatik ochiladi — xuddi ommaviy WiFi'dagi «kirish sahifasi» kabi (texnik jihatdan — captive-portal). Shunday qilib bulut ishlamayotganda ham mijoz QR ni skanerlaydi va darhol to'lovga tushadi.
- **PK avtorizatsiyasi — agentning o'zida, lokal-birlamchi.** Agent avtorizatsiyani o'zi tekshiradi (aktiv sessiya keshi, lokal imzolangan vaucherlar); tashqi manbalar — prioritet bo'yicha va talab bo'yicha: odatda lokal kontroller orqali, bulut — faqat fallback (backoff+jitter bilan). Hech bir nosozlik (kontroller, bulut, internet) klubni to'xtatmaydi va serverga «to'lqin» yaratmaydi.
- **Server — haqiqat manbai (source of truth); kontroller — sarflanadigan operatsion tugun.**
- **Pulni ushlamaymiz va pul oqimida emasmiz (MVP).** Har bir klubning Payme/Click'da o'z merchant/kassasi bor, to'lov to'g'ridan-to'g'ri klubga boradi; chek bo'yicha sotuvchi — klub. MVP da split va to'lovdan-komissiya yo'q. Bu bizdan og'ir to'lov litsenziyalashni olib tashlaydi (14-bo'lim).

## 2. Rollar va ma'lumot ob'ektlari (entity)

**Rollar:**

- **Mijoz / o'yinchi** — o'z qurilmasidan to'laydi, o'ynaydi (yuzma-yuz; masofadan — keyinroq, V5–V6).
- **Menejer (smena)** — oxirgi chegara sifatida qo'lda boshqarish, naqd pul qabul qilish. Smena rollari: egasi / menejer / kassir (avvalgi «admin» = menejer).
- **Superadmin (Clubpay jamoasi)** — klub yaratadi, egasini tayinlaydi, zonalar/paketlar/PK larni, Payme/Click, Soliq/OFD ni sozlaydi, klubni aktivlashtiradi; texnik sirlarni ko'radi (to'lov tizimlari kalitlari, OFD, platform fee). Egasi faqat o'z klublarini ko'radi va texnik sirlarga kirisha olmaydi; egasini faqat superadmin tayinlaydi.
- **Klub egasi** — tariflar, zonalar, hisobotlar, to'lovlar (payout), xodimlar (o'zining egasi-paneli).
- **Platforma (biz)** — soft, orkestratsiya va to'lovlarni tasdiqlash (pulga tegmaymiz), marketpleys (V2+). Monetizatsiya — obuna/shartnoma, to'lovdan komissiya emas.
- **Turnir tashkilotchisi (kompaniya, V4)** — akkaunt ochadi, xodimlar/filiallar orasida turnirlar o'tkazadi (rol + ochiq API).

**Asosiy ob'ektlar:**

- Klub (soliq shaxsi — chek bo'yicha sotuvchi), Zona, PK, PK Agenti, Kontroller.
- Tarif / blok, Sessiya, To'lov (statuslari bilan), Bron (V2).
- Vaucher (imzolangan oflayn vaqt-tokeni), Akkaunt/a'zo (V2), To'lov (payout)/hisob-kitob.
- Mijoz balansi/hamyoni (V2), Bog'langan karta (token) (V2).

## 3. Versiyalar va hajm

### V1 (birinchi iteratsiya)

**MVP maqsadi:** pilot klubda iCafe ni almashtirish, ma'lumotlar — O'zbekistonda; QR → to'lov → fiskal chek → PK avtomatik blokdan chiqishi → egasiga hisobot siklini tashqi (Xitoy) serverlariga bog'liqliksiz, lokal infratuzilmada berish.

**MVP-kesim (birinchi tayyor versiya):** yadro-halqa QR → to'lov → blokdan chiqarish → sessiya → egasi ko'radi. Adminka/naqd/qoldiq vaucheri — ham V1, lekin eng ingichka birinchi slays — pay-and-play. V1 ichida bosqichma-bosqich: avval cloud-first, yadro/agentga adapter bilan (halqani tez ishga tushirish uchun), keyin kontroller + real pilotgacha oflayn-bardoshlilik. Lending/sayt — alohida trek, billing bilan aralashtirmaymiz.

**Murakkab nosozlikka bardoshlilik — bu V2, MVP blokeri emas.** MVP da: cloud-first + bitta kontroller + B varianti (tugunda merchant-sirsiz; oflaynda yangi karta-to'lovlar → naqd/vaucher, server tiklanganda solishtiradi). To'liq avtoritet zanjiri Pi-1 → Pi-2 → menejer PK si, avtodiskaveri, lease, keep-awake va tugunda sir bilan oflayn-so'rov (A varianti) — V2 ga ko'chiriladi (9-bo'lim). Shunday qilib pilot tezroq chiqadi, murakkabi esa tirik klublar va ma'lumotlar paydo bo'lganda qo'shiladi.

- Har bir PK da statik QR → bizning bir sahifali saytni ochadi (bizning platforma; u to'lovni provayder orqali orkestratsiya qiladi).
- Tarif bloki uchun to'lov (hamyonsiz). Bir sahifali sahifamiz UX bo'yicha Rahmat PAY namunasida; to'lov tizimlari (Click/Payme/Uzum/kartalar) bilan va chek uchun Soliq bilan to'g'ridan-to'g'ri integratsiya (6-bo'lim).
- Uzaytirish — ekrandagi yirik QR orqali (dinamik, sessiyaga bog'langan), kichkina nakleyka orqali emas (7-bo'lim).
- Majburiy ro'yxatdan o'tishsiz (scan-pay-play). Vaqt qoldig'i — 6-bo'lim siyosati bo'yicha (kuyadi yoki qoldiq vaucheri). To'laqonli akkaunt/balans qoldiqni saqlash bilan — V2.
- Vaqt tugashi haqidagi bildirishnomalar — asosan PK ekranida.
- Telegram-bot push/yetkazish kanali sifatida (chek, vaucher, «vaqt oz qoldi») mijoz telefoniga (7-bo'lim).
- Kontroller Raspberry Pi da + sovuq zaxira.
- Oflayn-rejim va oflayn to'lov yo'llari (9–10 bo'limlar): kontrollerda vaucherlar keshi, kontroller server o'rnini bosuvchi sifatida, lokal xotirali veb + sink.
- Menejer paneli oxirgi chegara sifatida (onlayn-prioritet, naqd — eng oxirgi chora).
- Har bir to'lovga fiskal chek (O'zbekiston talabi, 14-bo'lim).

### V2 (keyingi iteratsiya)

- Foydalanuvchilarni ro'yxatga olish, klublar marketpleysi.
- Marketpleys (ilovada): bandlik rang ko'rsatkichli klublar xaritasi (bo'sh/band), yaqin klublar (geolokatsiya), klub va PK qidiruvi, statuslar (ochiq/bandlik/bo'sh PK lar/soatlar), sevimlilar, masofadan yoqish, joyida QR skan.
- PK bron qilish — ixtiyoriy (alohida opsiya; bronsiz yuzma-yuz kelish qoladi), pul modeli bilan (depozit, greys, bekor qilish, no-show) — 6.1-bo'lim.
- To'laqonli money-management (depozitlar, holdlar, qaytarishlar, kuyishlar, to'lovlar, komissiya, sverka).
- Mijoz hamyoni/balansi karta bog'lash va avto-to'ldirish bilan (6.2-bo'lim).
- Killer-ficha: yagona akkaunt + ilova + balans tarmoqning barcha klublarida — bir marta to'ldirding, istalgan ulangan klubda o'ynaysan (o'tuvchi hamyon, 6.2-bo'lim).
- Mobil ilova (Flutter, iOS/Android) asosiy mijoz interfeysi sifatida (Telegram Mini App o'rniga; bir marta qilamiz, alohida 3-versiyasiz). Joyida QR skan → veb-sahifa tasodifiy mehmonlar uchun no-install fallback bo'lib qoladi.
- Tokenlashtirilgan zero-tap avto-uzaytirish (Click/Payme).
- Avtoritet zanjiri Pi-1 → Pi-2 → menejer PK si (agentda avtodiskaveri + lease), keep-awake — to'xtab qolish huquqi yo'q klublar uchun.
- Menejer ommaviy operatsiyalari (yoqish/o'chirish/remont) tasdiqlash va «ahmoqdan himoya» bilan — 11-bo'lim.
- Klubning self-service onbordingi va klublarni tarmoqqa bog'lash modeli — 17-bo'lim.

Bozorga qarshi to'liq ficha-xarita va roadmap — 16-bo'limda.

## 4. Arxitektura (komponentlar)

| Komponent | Qayerda | Roli |
|---|---|---|
| PK Agenti | har bir o'yin PK sida | bloklash/kiosk, sessiya taymeri, kod kiritish ekrani, bir nechta manbadan avtorizatsiya, avto-uyqu, lokal/tarmoq orqali uyg'otish |
| Kontroller | klubdagi Raspberry Pi (yoki mini-PK N100) | WoL, sessiyalar koordinatsiyasi, agentlar va provayder bilan aloqa, lokal kesh, bulutga yukni agregatlash, menejer PWA sini beradi |
| Server (bulut) | bulut | haqiqat manbai, uzoq muddatli saqlash, marketpleys, analitika, sinxronizatsiya, hisob-kitoblar |
| Bizning bir sahifali sayt | bulutdan beriladi, kontroller keshlaydi | to'lovning «old eshigi»: PK holatini ko'rsatadi, to'lovni provayderda orkestratsiya qiladi |
| QR-nakleyka | har bir PK da | PK ning barqaror manzili (URL = PK ID si); saytimizni ochadi. Retroreflektiv (qorong'i klubda fonar ostida yaltiraydi); asosiy QR — uyg'onganda PK ekranida |
| Menejer paneli | boshqaruv tarmog'idagi qurilmada veb-PWA | PK ni qo'lda boshqarish (yoqish/uyqu/o'chirish/remont), to'lov qabul qilish (onlayn-prioritet), audit. Lokal kontroller beradi → internetsiz ishlaydi; kontroller ishdan chiqsa PWA keshi + agentlarga to'g'ridan-to'g'ri kirish |
| Egasi paneli | veb (bulut) | tariflar, zonalar, qo'shimcha xizmatlar katalogi, hisobotlar, to'lovlar/komissiya, xodimlar, kassa sverkasi; multiklub |
| Lending / sayt | veb (bulut) | marketing, mahsulot haqida info, klublar onbordingi; ochiq API portali (hamkorlar/kompaniyalar: akkauntlar, turnirlar, integratsiyalar — V4). Boshidanoq kerak |
| Telegram-bot | bulut (bizning servis) | push-bildirishnomalar va mijoz raqamiga yetkazish uchun alohida ob'ekt: vaucher, fiskal chek, «vaqt oz». Mijoz/menejer Telegram raqamini kiritadi → bot yuboradi; raqam bazada saqlanadi (mijozlar bazasi, V2 akkauntiga ko'prik). Yagona push kanali (SMS ishlatmaymiz) |
| Mehmon WiFi | alohida VLAN | to'lov walled-garden'i + captive-portal (10-bo'lim) |

### Texnik stek (taklif)

Agent C# / .NET da (Microsoft-stek) — sizning tanlovingiz bo'yicha, o'zining boy UI si uchun; qolgani — afzal ko'rilgan tanlov, o'zgartirsa bo'ladi.

| Komponent | Til/stek | Nega |
|---|---|---|
| PK Agenti | C# / .NET (WPF yoki WinUI) — Microsoft-stek: agentning o'z nativ UI si (shell/blok ekrani, 22-bo'lim); nazorat/billing uchun fon Windows-xizmati; past darajali ishlar (kiosk, S3, WoL, kiritish, anti-aylanib-o'tish) Win32-interop (P/Invoke) orqali; vaucher imzolash — .NET-kripto / libsodium | boy nativ Windows-UI + OS bilan yaxshi integratsiya; PK lar kuchli, futprint muhim emas |
| Kontroller | Go + SQLite (o'rnatilgan kesh/outbox) | bitta statik binar, ARM (Pi)/x86 (N100) ga kross-kompilyatsiya, past futprint, o'rnatilgan HTTP/HTTPS |
| Server (bulut) | Go + PostgreSQL (pul/ledjer uchun ACID) + hodisalar navbati (NATS/Redis) | kontroller bilan bitta til → umumiy modellar; billing uchun ishonchlilik |
| Veb-bir-sahifalik (V1) | TypeScript + yengil front (React/Svelte), statika | bulutdan beriladi, kontroller keshlaydi |
| Admin-PWA | TypeScript + React/Svelte, service worker | PWA oflayn, kontroller lokal HTTPS orqali beradi |
| Egasi paneli | TypeScript + React (o'sha front-stek) | menejer paneli bilan qayta ishlatish |
| Mobil ilova (V2) | Flutter / Dart | iOS/Android uchun bitta kod |

Infra: bulutda konteynerlar; kontroller — Pi da systemd-xizmat/konteyner; push — FCM/APNs; provayder bilan integratsiyalar ularning API si bo'yicha.

## 5. V1 oqimi: «to'ladim → o'ynayapman»

1. Mijoz PK ning QR-nakleykasini skanerlaydi → bizning bir sahifali sayt ochiladi (manzil = PK ID si). Login yo'q, sahifa bir martalik.
2. Sayt PK holatini ko'rsatadi (sessiya serverda, PK ga bog'langan). Bo'sh PK — tarifni tanlaysiz va boshlaysiz. Band PK (statik nakleyka) «HH:MM gacha band, bu yerda to'lov mavjud emas» deb ko'rsatadi — begona odam birovning sessiyasiga aralashmasligi uchun (13-bo'lim).
3. Mijoz tarif blokini (hajmiy chegirmali vaqt paketi — 6-bo'lim) va summani tanlaydi → sahifamiz to'lov tizimiga olib boradi (Click/Payme/Uzum/kartalar), u mijoz telefonida ilovani ochadi; to'ladi — sahifamizga qaytdi. Sayt bulutdan beriladi, u ishlamasa — lokal kontrollerdan.
4. Tasdiq qaytadi: to'lov tizimining callback'i → bizning server (imzoni tekshiramiz, idempotent) + fallback sifatida status so'rovi → muvaffaqiyat → START_SESSION → PK uyg'onadi. Fiskal chek — Soliq bilan to'g'ridan-to'g'ri integratsiyamiz (14-bo'lim): chekni sahifada ko'rsatamiz, kelajakda — mijoz raqamiga avto-ro'yxatga olish.
5. Uzaytirish — aktiv sessiya ekranidagi dinamik QR orqali (sessiyaga bog'langan, yangilanadi): uni faqat PK da o'tirgan odam ko'radi, begona nakleyka orqali uzaytira olmaydi. Kichkina nakleykani izlash shart emas — QR ekranda yirik. «Do'stim to'ldirib beradi» ishlaydi — o'yinchi kodini o'zi ulashadi.

**Bir sahifalik UX (V1):** bloklar tartibi — zona → vaucher bloki (zona bloki ostida) → paketlar/summa. Mijoz vaucher kiritdi → backend joriy zonadagi qoldiq vaqtni qaytaradi; pending-so'rov paytida «To'lash» tugmasi bloklangan, javobdan keyin — yana aktiv. Maslahat: paket tanlash yoki o'z summasini kiritib to'lash ham mumkin. Zona bittadan ko'p bo'lsa paketlar zonalar bo'yicha tablarda ko'rsatiladi.

## 6. To'lovlar

### Model (bazaviy, V1)

Oddiy asos: bir soat o'yin shuncha turadi, undan keyin — «qancha ko'p olsang, soati shuncha arzon» hajmiy chegirma marketing richagi sifatida.

- Bazaviy birlik — soat, har bir zonaga o'z stavkasi bilan (Standart / Pro / VIP; kerak bo'lsa — konsol). Zonani, stavkalarni va bloklarni egasi sozlaydi; sutka vaqti bo'yicha dinamika mumkin (ertalab/kecha/ish kunlari).
- Hajmiy chegirmali bloklar: 30 daq / 1 s / 2 s / 3 s / 5 s / tungi. Blok qancha yirik — soatning effektiv narxi shuncha past; 30 daqiqa daqiqasiga qimmatroq, soat foydaliroq ko'rinishi uchun (soat — yakor). Misol (illyustrativ, Standart): 30 daq ≈ 8k · 1 s = 15k · 2 s = 28k (14k/s) · 3 s = 39k (13k/s) · 5 s = 60k (12k/s) · tungi — fiks. Raqamlarni egasi belgilaydi.
- Har bir zonaning o'z setkasi.
- **Ishlatilmagan qoldiq (V1, akkauntsiz)** — egasi tanlaydigan ikki variant: **(a) kuyadi** (blok oldingmi — sessiyada ishlat; oddiy, bilet kabi); **(b) qoldiq vaucheri** — qoldiq bilan tugatilganda tizim sarflanmagan daqiqalarga kod/QR beradi (imzolangan bearer-token; 10/13-bo'limlardagi vaucher primitivini qayta ishlatamiz), keyinroq istalgan PK da so'ndiriladi, imzolangan va yashash muddati (TTL) bilan. Qoldiq soniyagacha aniqlikda saqlanadi va qo'llanadi; vaucher istalgan musbat qoldiqqa beriladi (hatto 15–50 soniyaga ham), minimum yo'q. Bearer = kodni kim ushlab tursa, o'sha ishlatadi.

**Vaucher yetkazish** (bizning Telegram-bot orqali; SMS ishlatmaymiz). Olishning ikki usuli:

1. **Mijozning o'zi** — sessiya tugashidan oldin ekranda (yoki sahifada) o'z Telegram raqamini kiritadigan maydon bor; unga botimiz vaucherni yuboradi («keyingi safar ishlatishingiz mumkin»). Mijoz raqamni agent PK sida ham kirita oladi.
2. **Menejer orqali** — mijoz yaqinlashdi («qoldiqni qanday olsam bo'ladi?»), menejer panelda sessiya yonida mijoz raqamini kiritib yuboradi — bot vaucherni jo'natadi. Ikkala holatda ham raqam bazada saqlanadi — bu bizning mijozlar bazamiz (minimal PII, 14-bo'lim) va V2 akkauntiga yumshoq ko'prik (o'sha raqam akkauntga aylanadi). Yuborishni menejer, sahifada mijozning o'zi yoki PK agenti (oldindan Telegram raqamini so'rab) amalga oshirishi mumkin.

Telegram hali bog'lanmagan bo'lsa: botga token bilan deep-link beramiz — mijoz Start ni bosadi, bot chat_id ni raqamga bog'laydi va kutayotgan vaucherni yuboradi.

**Vaucherni so'ndirish oflaynda ham ishlaydi:** kontroller amaldagi vaucherlar ro'yxatini bazadan davriy tortib lokalda ushlab turadi, shuning uchun server ishlamayotganda mijoz vaucher kiritadi va sessiya boshlanadi (9-bo'lim).

V2 da qoldiq balans/akkauntda yashaydi (klassik klubdagi kabi) — bu qoldiqning «to'g'ri uyi»; ro'yxatdan o'tish va hamyon — V2 (6.2-bo'lim).

**«Ko'p to'lasang — soati arzon» sodiqlik dasturi** — ikki darajada: hajmiy bloklar allaqachon V1 da; V2 da balansni to'ldirish uchun pog'onali bonuslar qo'shiladi (6.2-bo'lim): ko'proq yuklading — ko'proq olding (100k → 100k · 200k → +bonus · 300k → +kattaroq). Maqsad — mijozga balansni ushlab turish va saxiy to'ldirish foydali bo'lishi.

### Dvigatel

**V1** — bizning bir sahifali sahifa + to'lov tizimlari bilan to'g'ridan-to'g'ri integratsiya (Click / Payme / Uzum / kartalar). Flou 1:1 Rahmat PAY kabi (UX namunasi sifatida olingan, Rahmat'ning o'zi ishlatilmaydi): QR skan → bizning sahifa → tarif/summa tanlash → to'lov tizimiga o'tish → mijoz telefonida ilova ochiladi → to'lov → sahifamizga qaytish.

**Fiskal chek** — Soliq (Soliq.uz) bilan to'g'ridan-to'g'ri integratsiyamiz: chekni o'zimiz shakllantirib sahifada ko'rsatamiz; kelgusi versiyalarda — chekni mijozning kontakt raqamiga avto-ro'yxatga olish / agar ro'yxatdan o'tgan bo'lsa tekshirish (14-bo'lim). Rahmat fiskalizatsiyada ham qatnashmaydi.

**Zaxira variant — agregator** (masalan, Rahmat/Multicard): barcha metodlarga bitta integratsiya + chek. Jamoa Rahmat-sandbox ni sinab ko'rgan — ishlaydi; lekin mahsulot qarori — nazorat va kerakli fiskal UX uchun to'g'ridan-to'g'ri integratsiya. Agregatorni to'g'ri yo'l juda og'ir chiqib qolsa fallback sifatida saqlaymiz.

**To'lov mexanikasi (umumiy, har bir to'lov tizimi bilan to'g'ridan-to'g'ri amalga oshiramiz):**

- Summalar — tiyinlarda (1 so'm = 100; 15 000 so'm = 1 500 000).
- To'lov tizimi API si orqali to'lov yaratish → to'lovga havola/deeplink olamiz (ilovani ochadi). Bizning order_id ni provayder to'lovi id si bilan bog'lab o'zimizda saqlaymiz.
- Callback + bizning backendda imzo tekshiruvi → muvaffaqiyatni idempotent qayd etamiz → START_SESSION; fallback — status so'rovi. PK ni blokdan chiqarish — faqat muvaffaqiyat statusida.
- **Klub merchant'i (MVP — splitsiz):** to'lov to'g'ridan-to'g'ri klubning Payme/Click'dagi merchant/kassasiga boradi; klubning merchant-kredlarini faqat to'lov yaratish va tasdiqlash (callback/so'rov) uchun ishlatamiz, pulga tegmaymiz. Split/ko'p qabul qiluvchilar — faqat keyinroq marketpleys uchun (V2+).
- Qaytarish — kamida to'liq; qisman ishlatilgan sessiya → qoldiq vaucheri (pul emas), to'liq pul qaytarish — faqat «to'ladi, lekin PK blokdan chiqmadi» uchun.
- Uzaytirish — har safar yangi to'lov (order_id = session_id + uzaytirish raqami).

*(Ma'lumot uchun: Rahmat/Multicard sandbox testida mexanika xuddi shunday edi — invoice → checkout/deeplink → callback, sign = md5(store_id+invoice_id+amount+secret) → uuid bo'yicha status → full refund. Bu flou bo'yicha mo'ljal, to'lov tizimlari bilan to'g'ridan-to'g'ri amalga oshiramiz.)*

**V2 / avto-uzaytirish:** kartani to'lov tizimida tokenlashtirish (Payme Subscribe va sh.k.): birinchi to'lov = bog'lash + OTP, keyin blok uchun zero-tap yechish. Talab qiladi: rozilik, limitlar, qo'lda to'lovga fallback, idempotentlik, faqat token saqlash (karta raqami emas).

### Qo'shimcha to'lov tezligi

Eng tez yo'l — to'lovning yo'qligi (balans/token). V1 da hamyonsiz va tokenizatsiyasiz qo'shimcha to'lov qo'lda (skan → to'lov), shuning uchun erta ogohlantirishlar va sessiya muzlatish kritik (7-bo'lim).

**Nega agregator orqali emas, to'g'ridan-to'g'ri:** flou va fiskal UX ustidan to'liq nazorat (Soliq'dan o'z cheki, kelajakda raqamga avto-ro'yxat). Narxi — ko'proq integratsiya ishi (har bir to'lov tizimi + Soliq alohida). Agregator ongli fallback bo'lib qoladi (20-bo'lim).

### 6.1 Bron qilish va pul modeli (V2)

Bron — V2 ning ixtiyoriy fichasi (alohida opsiya; bronsiz yuzma-yuz kelish qoladi), o'z pul mantig'i bilan. Umumqabul qilingan reservation-tizimlar modeliga tayanamiz (Gamers Lounge, Cornell, Tagvenue/Midlane, Giggster).

- **Bron depoziti = birinchi tarif bloki** (yoki fiks-yig'im), platforma orqali onlayn yechiladi. No-show ni kesadi va niyatni mustahkamlaydi.
- **Kelishga greys (~10–15 daq):** slot boshlanishida PK uyg'otiladi (WoL) va o'yinchi uchun ushlab turiladi; greys ichida kod bilan kirmadi → PK bo'shatiladi, depozit kuyadi (no-show to'lovi).
- **Bekor qilish tariflari (klub sozlaydi):** oldindan (≥ N soat) → to'liq qaytarish; oyna ichida → qisman/qaytarishsiz; no-show → 0%.
- **Anti-abyuz:** takroriy no-show/bekor qilishlar → kattaroq depozit yoki bronga vaqtinchalik ban.
- **Pul split orqali:** depozitni platforma o'tkazadi; tugatish → klubga hisob-kitob minus komissiya; bekor qilish → revers; kuyish → siyosat bo'yicha bo'linadi. Provayderda qaytarish/revers va holdlar qo'llab-quvvatlanishi kerak. Bron — marketpleys-talab → marketpleys-komissiya o'rinli (15-bo'lim).
- **Billing boshqaradigan pul holatlari:** yechish, hold (avtorizatsiya), qaytarish, qisman qaytarish, depozit kuyishi, klubga hisob-kitob, komissiya, sverka.

### 6.2 Hamyon / balans (V2)

Mijoz o'z balansini ushlab turadi, to'ldiradi va o'yin uchun balansdan yechish bilan to'laydi. Bu qo'shimcha to'lovni o'yindan olib tashlaydi (indamay yechamiz) va ushlab qolishni (retention) kuchli oshiradi.

- **Bazaviy:** balans (saqlanadigan qiymat), to'ldirish, sessiyalar uchun avto-yechish; karta bog'lash (token) → bir tapda to'ldirish; balans chegaradan pastda avto-to'ldirish (rozilik + limit).
- **To'ldirish:** bog'langan karta, Click/Payme/Uzum, menejerda naqd (balansga kredit).
- **Bildirishnomalar:** past balans, to'ldirish muvaffaqiyati, avto-to'ldirish, yechish cheki, keshbek, balansga qaytarish, balans muddati tugashi.
- **Sodiqlik va o'sish:** balansga keshbek, to'ldirish uchun pog'onali bonus (qancha ko'p yuklasang — bonus shuncha katta: 100k → 100k · 200k → +bonus · 300k → +kattaroq), referal krediti, promokodlar, salomlashish/bayram krediti, a'zolar uchun chegirmalar; balans sovg'asi/o'tkazish (ota-ona→bola, jamoaviy hamyon).
- **Nazorat:** xarajat limitlari, ota-ona nazorati (voyaga yetmaganlar uchun kunlik limit).
- **Pul/hisob:** qaytarishlar va bron depozitlari — balansdan; real balansni (yuklangan pul) va bonus/promo-kreditni (to'ldirish uchun tarqatma: qaytarilmaydigan, muddati tugashi mumkin, va bu e-money emas — regulyatorikani soddalashtiradi) ajratish. Real balans — bizning majburiyatlarimiz (float) — segregatsiyalangan hisob, sverka, qoldiq muddati qoidalari.
- **Qamrov:** balans bitta klubga (closed-loop) yoki tarmoq bo'ylab o'tuvchi (open-loop) — killer-ficha: bitta balans barcha klublarda. O'tuvchisi kuchliroq (tarmoq effekti), lekin bu «ochiq» e-money (hamkor-bank). Hisob-kitob: balansni istalgan klubda sarfladi → platforma shu klub bilan float'dan hisoblashadi (V1-splitdan murakkabroq — pul oldinroq va ehtimol boshqa klubda yuklangan).

⚠️ **Regulyator bayrog'i:** mijoz pulini balansda saqlash — bu e-money, O'zRda esa e-money chiqarishga faqat banklar/MB haqli. Haqiqiy hamyon = hamkor-bank (emitent — bank, biz — UX/tex-qatlam); bu V1 ning «pul ushlamaymiz» afzalligini teskari buradi. Yengil alternativalar: (a) Click/Payme/Uzum hamyonlari; (b) «balans» = karta-fayl-ustida + avto-to'ldirish. Qarang: 14, 20-bo'limlar.

## 7. Vaqt tugashi haqidagi bildirishnomalar

**Tamoyil:** oxirigacha to'lov → uzilishsiz; keyin → sessiya muzlatiladi va o'sha kadrdan davom etadi. Hech qanday «boshidan boshla» yo'q.

**Ogohlantirishlar narvoni** (chegaralarni egasi sozlaydi):

- 10 daqiqa qolganda — yumshoq; 5 daqiqada — taymer sarg'ayadi, tost; 1 daqiqada — qizil + banner;
- 0 — bloklash, sessiya muzlatilgan (greys 5–10 daq); greysdan keyin — tugatish, PK uyquga.

**Muzlatish ekrani (0 ga yetganda):**

- Chiroyli bezatilgan blokda yirik dinamik uzaytirish QR ini ko'rsatadi (sessiyaga bog'langan, 13-bo'lim) — «Davom etmoqchimisiz? QR ni skanerlang — sessiyangiz saqlangan» (pattern/zastavka, vizual yoqimli). O'yinchi telefonini ekranga qaratib skanerlaydi — «o'sha kadrdan» davom etadi. Agar greys paytida ekran so'nib/uyquga ketgan bo'lsa, uyg'onganda yana o'sha QR ko'rsatiladi.
- **Bu umuman uzaytirishning asosiy usuli** (jumladan o'yin paytida ham — «vaqt oz» bildirishnomasi bo'yicha QR ekran burchagida ko'rinadi): yorqin ekrandagi yirik QR qorong'i klubdagi kichkina nakleykadan ancha oson skanerlanadi. Nakleyka faqat bo'sh PK da start uchun kerak.
- **Davomga chegirmali ushlab qoluvchi apseyl:** keyingi blokka pasaytirilgan stavka bilan motivatsion matn — qancha uzoq o'ynagan bo'lsa, shuncha foydali (hajmiy/sodiqlik chegirmasi, 6-bo'lim). Misol: «2 soat o'ynayapsan — davomi 50k o'rniga 30k/soat». Maqsad — o'yinchini uzoqroq ushlab qolish.
- **Kontekst (V2/V3, AIga qarab):** apseyl-chegirmani faqat foydali bo'lganda ko'rsatish — pik soatdan tashqarida va PK bron qilinmagan bo'lsa; pik soatda yoki bronda — ko'rsatmaslik (talab bor / joy boshqa birovga slotga kerak). V2/V3 da — pik/nopik jadval qoidasi + bron statusi; talab bo'yicha to'laqonli AI-yield — V7.

**Kanallar:**

- Asosiy — PK ekrani (burchak taymeri + «uzaytirish uchun skanerlang» QR bilan to'g'ridan-to'g'ri ekranda). V1 da bu bosh kanal, chunki veb-sahifa mijozning yopiq qurilmasiga ishonchli push yubora olmaydi.
- Push V2 da — mobil ilova orqali (nativ FCM/APNs).
- Telegram-bot (V1) — telefonga push (vaqt oz, chek, vaucher): bepul, UZ da mashhur; V2 to'laqonli ilovasigacha vaqtinchalik kanal.
- Avto-uzaytirish (keyingi blokni indamay yechish) faqat karta tokenizatsiyasida mumkin → V2 fichasi. V1 da uzaytirish qo'lda.
- Hamyon bilan (V2) balans bildirishnomalari qo'shiladi (6.2-bo'lim).

**Push-bildirishnomalar V2 (barcha mantiqlar bo'yicha)**

V2 da (mobil ilova, nativ push FCM/APNs) — yagona push tizimi. Kategoriyalar:

- **Sessiya/vaqt:** vaqt tugashi (10/5/1) — shu jumladan telefonga push, start/tugatish, muzlatish/davom ettirish, avto-uzaytirish (boshlandi/muvaffaqiyat/rad).
- **To'lovlar:** muvaffaqiyat/rad, qaytarish, fiskal chek.
- **Balans/hamyon:** past balans, to'ldirish, avto-to'ldirish, yechish, keshbek, qaytarish, muddat tugashi (6.2-bo'lim).
- **Bron:** tasdiq, eslatma, «slot boshlandi — kod bilan kir», greys-ogohlantirish, no-show/kuyish, bekor qilish + qaytarish statusi.
- **Masofaviy o'yin (V5–V6):** sessiya tayyor (ulanish ma'lumotlari), tez orada tugaydi.
- **Sodiqlik/marketing:** promo, bonuslar, referallar, sevimli klublarda happy-hour.
- **Marketpleys:** sevimli klub ochildi / bo'sh PK lar paydo bo'ldi.
- **Akkaunt/xavfsizlik:** karta bog'landi/o'chirildi, yangi kirish, avto-yechishga rozilik, shubhali faollik.

Auditoriyalar har xil: mijozga — ilovada push + PK ekrani; egasi/menejerga — o'z alertlari (to'lov o'tdi, kassa anomaliyalari, kontroller oflayn) o'z panelida. Har bir kategoriya sozlanadi, sokin soatlar bor.

**Blok ekrani / kod kiritish**

- Ko'rinadigan maslahat: «Kod kiritish uchun Enter bosing». Uyg'otish — istalgan klavishdan.
- Mijoz vaucheri va menejer master-kodi uchun yagona maydon (agent turini o'zi taniydi).
- Menejer master-kodi — kriptobardoshli va bir martalik/almashtiriladigan (TOTP-simon yoki imzolangan token).

## 8. Energiya tejash va uyg'otish

- Bo'sh PK lar — uyquda (S3), gibernatsiyada emas va o'chirilgan emas (~3 Vt).
- QR — bosma retroreflektiv nakleykada (logo + kod): qorong'i klubda telefon fonari ostida yaltiraydi. Asosiy QR uyg'onishda ekranda ko'rsatiladi (monitor — yorug'lik manbai). Ekrani o'chgan uxlayotgan PK hech narsa ko'rsatmasligi kerak.
- «Vitrina» uchun alternativa: PK larning bir qismini bekor turish ekrani bilan yoqiq holda + energiya tejash rejasi bilan ushlab turish — lekin bu endi uyqu emas.

**Ikki uyg'otish rejimi:**

1. **Tarmoq orqali (WoL)** — kontroller biladi (va boshqaruv tarmog'idagi istalgan qurilma, jumladan menejer ilovasi). Masofadan yoqish (V2) va avtomatika uchun.
2. **Lokal** — klavish/sichqoncha bosish (S3 da) yoki quvvat tugmasi. Kontroller talab qilmaydi; joyidagi mijoz/menejer uchun.

Sozlash: idle-PK S3 da + BIOS da «wake on keyboard/mouse».

**Avto-uxlatish (uyquga qaytish)**

PK sikli: uyqu (S3) → mijoz uyg'otdi → sessiya → mijoz ketdi/sessiya yopildi → yana uyqu. Mantiqni agentning o'zi lokal ishga tushiradi (taymer bo'yicha), kontroller/bulutsiz — oflaynda ishlaydi.

PK S3 ga ketadi, qachonki **uchchala shart** bajarilsa: sessiya to'liq yopilgan (greys/muzlatish tugagan); klaviatura/sichqoncha faolligi idle-timeoutdan uzoqroq yo'q (~5–10 daq, sozlanadi); yaqin bron yo'q (V2). Uxlatilmaydi, qachonki: to'langan sessiya ketyapti; greys/muzlatish; tez orada bron.

**Degraded-rejim nuansi:** avto-uxlatish xavfsiz — joyidagi mijoz PK ni S3 dan klavish bilan uyg'otadi, menejer ilovasi esa LAN orqali WoL yuboradi. Agar LAN-uyg'otuvchisiz masofaviy wake kerak bo'lsa — uxlatishni bostiramiz.

**Uch xil taymer, adashtirmaslik kerak:** bron bo'yicha kelish greysi (~10–15 daq), to'langan vaqt tugagandan keyingi greys/muzlatish (~5–10 daq), uyqugacha idle-timeout (~5–10 daq).

## 9. Nosozlikka bardoshlilik (otkazoustoychivost)

### «Kontroller» roli xostlar zanjirida; ingichka (thin) agentlar

**Terminlarga aniqlik.** Bu yerda «server» — bizning bulut backendimiz (platformamizning miyasi), Click/Payme emas. «Agent → kontroller → bizning server» vertikali — bu bizning holatimizni bizning tugunlarimiz orasida sinxronlash. To'lov tizimi (Click/Payme/Uzum), uning mijoz telefonidagi ilovasi va Soliq — tashqi servislar: ulardan «to'landimi?» deb so'raymiz va chek urib chiqaramiz, bizning vertikalga taalluqli emas.

**Yagona «kontroller» dvigateli + ingichka agentlar.** Avtoritet-rol (sessiyalar, to'lov tekshiruvi, vaucherlar, grantlar, lokal saqlash) prioritetli xostlar zanjirida lightweight-kontroller sifatida ijro etiladi: bulut serveri → Pi-1 → Pi-2 → menejer PK si. Kim yuqoriroq va tirik — o'sha avtoritet. O'yin PK lari — ingichka agentlar: faqat buyruqlarni bajaradi (lock/unlock/wake, kiosk, jonli taymer), «miya» bo'lib olmaydi.

**Nosozlik darajalari:**

- **T0 — norma:** avtoritet — bulut serveri; kontroller (Pi-1) keshlaydi/rele qiladi; agent jonli taymer yuritadi.
- **T1 — server yotdi:** avtoritet — Pi-1 (yoki Pi-1 yetib bo'lmasa Pi-2): to'liq sikl lokalda (sahifa captive-portal orqali, to'lovni to'lov tizimidan to'g'ridan-to'g'ri so'rab tekshirish, keshdagi vaucherlar, start/uzaytirish), hodisalar — outbox'ga. PK uyg'otish — talab bo'yicha (to'lovda WoL).
- **T2 — server va ikkala Pi yotdi:** avtoritet — menejer PK si (unda ham kontroller-servis aylanadi). PK lar bu paytga kelib allaqachon yoqilgan (yoqiq ushlab turish oldindan ishlagan, quyiga qarang).
- **Tiklanish:** kontroller-avtoritet → (qaytganda) server. Idempotent; yuqoridagi pastdagini qayta yozadi.

**Avtoritetni aniqlash va tanlash — ilova darajasida (VRRP emas).** Menejer PK sida Windows, shuning uchun keepalived/VRRP yaramaydi; tarmoq VIP o'rniga agentda avtodiskaveri qilamiz:

- Agent prioritet bo'yicha xostlar ro'yxatini ushlab turadi [Pi-1, Pi-2, menejer PK si] + ularni mDNS orqali topadi, eng yuqori tirik turganiga ulanadi, health-check, uzildi → o'zi keyingisiga o'tadi.
- Kontrollerlar o'zaro yengil lease/lider tanlashni ushlab turadi — rovno bitta yozuvchi aktiv; order_id/grant_id bo'yicha idempotentlik tutashuvda sug'urta qiladi. Mijoz sahifasi uchun ham xuddi shu.

**Yoqiq ushlab turish — oldindan, T2 gacha.** Uxlayotgan PK ni uyg'otuvchi yo'qolsa uyg'otib bo'lmaydi, shuning uchun PK ni aloqa to'liq yo'qolishidan oldinroq yoqiq ushlaymiz. Qoida: agent avtoritetlarning yetib borishligini kuzatadi; uyg'otish yo'li zaxirasini yo'qotishi bilan (LANda bitta yetib boriladigan avtoritet-uyg'otuvchi qoldi) — agent lokal no-deep-sleep ga o'tadi, qolgan avtoritet esa allaqachon uxlayotgan PK larni oldindan uyg'otadi. Shunda oxirgi tugunning o'limi hech kimni uyquda «qamab» qo'ymaydi.

**Monitoring:** har bir tugun holati (server, Pi-1, Pi-2, menejer PK si) va zaxira yo'qolishi fakti monitoring qilinadi; istalganining yiqilishida va ayniqsa zaxira yo'qolishida alert.

### To'lov tasdig'i — batafsil (callback vs so'rov)

Bu tizimning yadrosi: pul qayerda tekshiriladi va blokdan chiqarish avtorizatsiya qilinadi. To'lov doim Click/Payme ga boradi; faqat muvaffaqiyat haqida bizning qaysi tugunimiz bilishi o'zgaradi. Quyida «aktiv avtoritet» = bulut → Pi-1 → Pi-2 → menejer PK si (kim yuqori va tirik).

**A tarmoq — norma (callback, server tirik):**

1. Mijoz to'ladi → to'lov tizimi bizning statik callback-URL ni chaqiradi. Click — SHOP API: Prepare (rezerv) → Complete (tasdiq). Payme — Merchant JSON-RPC: CheckPerformTransaction → CreateTransaction → PerformTransaction.
2. Server imzoni tekshiradi (Click — ularning sekretli formulasi bo'yicha; Payme — JSON-RPC avtorizatsiya); autentik bo'lmagan so'rov tashlanadi.
3. Idempotent tekshiruv: order_id topilgan, summa mos, status hali success emas. Takror → avvalgi natijani qaytarish, hech narsani qaytadan qilmaslik.
4. Payment = success yozish (bitta yozuv) → imzolangan START grantini chiqarish → server → kontroller → agent.

**B tarmoq — oflayn (so'rov, server yotdi):**

1. Mijoz baribir to'laydi — pul bizdan mustaqil harakatlanadi; yiqilgan serverga callback yetib bormaydi → to'lov tizimi uni retray'ga qo'yadi.
2. Aktiv avtoritet order_id ni biladi: sahifani o'zi bergan (captive-portal), yopilmagan buyurtmalar ro'yxatini yuqoridan replika bilan olgan, yoki order_id ni mijoz telefoni LAN orqali olib kelgan.
3. Avtoritet statusni so'raydi (avtorizatsiyalangan holda, bizning order_id bo'yicha):
   - Click: `GET /v2/merchant/payment/status_by_mti/:service_id/:order_id/:date`;
   - Payme: `CheckTransaction` account.order_id bo'yicha. Backoff 3→5→10 s, TTL ~10–15 daq, final statusda to'xtash. Ochiq emas (sekret kerak), «har soniyada» emas → DDoS emas. Click limitlari — tasdiqlash kerak (20-bo'lim).
4. «To'langan» → summa va order_id tekshiruvi, idempotent → lokal imzolangan grant (o'sha shaklda) → agent → blokdan chiqarish. Hodisa — outbox'ga.

**Tasdiqdan keyin umumiy (ikkala tarmoq):**

- Grant pc_id + summa + nonce + TTL + bir martalikka bog'langan → suratga olingan QR / ushlangan referensni qayta o'ynatib yoki boshqa PK ga qo'llab bo'lmaydi.
- Blokdan chiqarish — faqat to'lov tizimi tasdiqlaganda (u — pul haqidagi haqiqat manbai; lokalda «to'langan»ni soxtalab bo'lmaydi).
- Fiskalizatsiya: chek Soliq'ga; asinxron kelsa — fiscal_pending ushlab turamiz, N daqiqada kelmasa alert; chekni sahifada + Telegram'da ko'rsatamiz.
- Orqaga sink: outbox → zanjir bo'ylab yuqoriga (agent → kontroller → server), idempotent (order_id bo'yicha dedup); kechikkan retray callback o'sha order_id bo'yicha yelimlanadi — dublikat yo'q.

**So'rov uchun sekret (kompromis):** so'rovni imzolash uchun klubning merchant-sekreti kerak (har klubniki o'ziniki).

- **A** — kalit tugunda (himoyalangan saqlash, rotatsiya) → to'liq oflayn-so'rov;
- **B (MVP uchun, xavfsizroq)** — karta-to'lovlarni server tasdiqlaydi; u yiqilganda yangi karta-to'lovlar → vaucher/naqd, server tiklanganda solishtiradi. B dan boshlaymiz.

**Chekka holatlar:**

| Vaziyat | Harakat |
|---|---|
| To'lov o'tdi, PK blokdan chiqmadi (agent oflayn / unlock failed) | Retraylardan keyin → to'liq qaytarish («to'ladi-lekin-startlamadi») yoki menejer qo'lda aralashuvi; panelda xato |
| To'lov osilib qoldi / tugamadi | Blokdan chiqarmaymiz; TTL bo'yicha buyurtma eskiradi |
| Ikki marta tasdiq (callback ham, so'rov ham ishladi) | order_id bo'yicha idempotentlik → blokdan chiqarish rovno bir marta |
| Tiklanishdan keyin kechikkan callback | order_id bo'yicha dedup — grant/yechish ikkilanmaydi |
| Oflaynda tugunda sekret yo'q (B variant) | Yangi karta-to'lov → vaucher/naqd; tiklanishda server sverkasi |
| Qisman ishlatilgan sessiya | Qoldiq vaucheri (pul emas); to'liq pul qaytarish — faqat «to'ladi, lekin startlamadi» |

### Aniqlash va manzillash (kontroller/agentlarda statik manzil yo'q)

Muammo: serverda statik manzil bor, kontroller/agentlarda — yo'q, ularga webhook yuborib bo'lmaydi. Yechim — yo'nalishni teskari qilamiz: pastki tugunlar o'zlari yuqoriga chiquvchi ulanish ochadi va tinglaydi; hech kim dinamik manzilga kiruvchi webhook yubormaydi.

- Agent kontrollerga chiquvchi ulanishni ushlab turadi (LAN orqali long-poll/websocket), kontroller — serverga. Bildirishnomalar va buyruqlar shu ochiq kanallar orqali orqaga boradi (NAT o'tiladi).
- To'lov tasdig'ini joriy avtoritet tortadi: server (to'lov tizimining statik callback'i) → yoki kontroller to'lov tizimini so'raydi → yoki agent o'zi so'raydi. Blokdan chiqarish — faqat to'lov tizimi tasdiqlaganda (lokal soxtalab bo'lmaydi); grant pc_id + summa + nonce + TTL + bir martalikka bog'langan, shuning uchun suratga olingan QR ni qayta o'ynatib bo'lmaydi. Dinamik tugunga kiruvchi webhook hech qachon kerak emas.
- Kontrollerni LANda topish — mDNS/zeroconf (controller.local / juftlikning virtual IP si); mijoz sahifasi va agentlar uni topadi, manzilni keshlaydi. Kontrollerlardan javob yo'q → mijoz sahifasi agent bilan LAN orqali to'g'ridan-to'g'ri gaplashadi (PK manzili ma'lum), agent — avtoritet (T2; 23-bo'lim).

### Porsiyali sinxronizatsiya (mayda temir)

Kontroller — kuchsiz tugun, sink — teskari bosim (backpressure) bilan porsiyalarda, portlash bilan emas:

- Har bir tugun hodisalarni append-only outbox'da to'playdi (faqat deltalar, to'liq steyt emas), event_id/nonce bilan → idempotentlik.
- Rekonnektda kichik batchlar bilan boshqariladigan tempda beradi (rate-limit, cheklangan navbat); qabul qiluvchi tasdiqlaydi — yuboruvchi outbox'ni tozalaydi. Prioritet: pul/sessiyalar telemetriyadan yuqori.
- Kontroller — klubning serverga yagona voronkasi (barcha agentlar hodisalarini agregatlaydi → batchlar bilan), shuning uchun bulut bo'g'ilib qolmaydi (quyida thundering herd ga qarang).

### Kontrollerning oflayn-rejimi

- Kontroller avtonom: lokal kesh (PK lar, tariflar, sessiyalar, a'zolar) → klub bulutsiz LANda ishlaydi.
- Server bilan aloqa yo'qolganda: aktiv va yangi sessiyalar davom etadi; qo'lda boshqarish va naqd ishlaydi; masofaviy boshqaruv/analitika pauzada.
- To'lov tasdig'i — provayderdan to'g'ridan-to'g'ri status so'rovi, faqat bulut orqali emas.
- Kontrollerda vaucherlar keshi: Pi amaldagi vaucherlar ro'yxatini bazadan davriy tortadi → server ishlamasa mijoz vaucher kiritadi va sessiya boshlanadi.
- Kontroller bizning server o'rnini bosuvchi sifatida: serverimiz to'xtaganda kontroller to'liq siklni (provayderga to'g'ridan-to'g'ri so'rov bilan to'lov + keshdan vaucherlarni so'ndirish + sessiyalar starti) LANda tortadi.
- Sverka: rekonnektda outbox (oflayn hodisalari) + inbox (kechiktirilgan buyruqlar), idempotentlik kalitlari.
- Heartbeat + «avtonom ishlayapmiz» banneri; klub jim tursa marketpleys masofaviy boshqaruvni yashiradi.
- Temir: UPS, lokal BD, watchdog, ishonchli soat (RTC+NTP).

### Kontroller ma'lumotlar modeli

- Server = haqiqat manbai va uzoq muddatli saqlash. Pi = sarflanadigan keshli operatsion tugun + outbox (serverga uzluksiz strim; startda holatni tortib olish).
- **Billing↔agent kontrakti:** jonli teskari sanashni agent lokal yuritadi (real vaqt, oflayn); billing — berilgan vaqt bo'yicha haqiqat manbai (grantlar/uzaytirishlar). Qoldiq = berilgan − sarflangan, solishtiriladi. Ko'prik — barqaror external_pc_id (QR ga tikilgan: club+pc).
- Pi kuyib qoldi → zaxirasini qo'yamiz → klub ID si bo'yicha hammasini serverdan tortadi → davom etadi. Ma'lumotlar yo'qolmaydi.
- Hardware: SD o'rniga USB-SSD dan yuklash/saqlash, UPS, «klub ID si bo'yicha almashtirilishlik»; opsiya — N100.

### Kontroller rezervatsiyasi (avtodiskaveri + lease, VRRP emas)

**Nega VRRP/keepalived emas:** menejer PK si — Windows, keepalived esa — Linux; har xil turdagi uchlikda (2 Pi + Windows) umumiy tarmoq VIP yaramaydi. Avtoritet tanlashni ilova darajasida qilamiz.

- **V1** — yakka kontroller + sovuq zaxira (oldindan tayyorlangan obraz, konfiguratsiya serverdan, almashtirish daqiqalar ichida; aktiv sessiyalar agentlar keshida yashaydi, pauza — faqat yangi ishga tushirishlarga).
- **V2** — avtoritet zanjiri Pi-1 → Pi-2 → menejer PK si:
  - Agentda avtodiskaveri: prioritet bo'yicha xostlar ro'yxati + mDNS; agent eng yuqori tirikka ulanadi, health-check, uzildi → o'zi keyingisiga. Hech qanday VIP/gratuitous ARP/multikast-tyuning yo'q.
  - Kontrollerlar orasida lease/lider tanlash: rovno bitta yozuvchi aktiv (eng yuqori yetib boriladigan qisqa TTL-lease oladi, qolganlari — standby). order_id/grant_id bo'yicha idempotentlik tutashuvda sug'urta.
  - Holatning yuqoriga-pastga replikasi (append-only log, idempotent) — zaxira iliq bo'lishi uchun.
  - Fallback-manzil: agent konfigida — mDNS nosozligi holatiga menejer PK sining to'g'ridan-to'g'ri IP si.
- Topologiya/quvvat: boshqaruv VLAN alohida; Pi va svitchga UPS; internet + ixtiyoriy 4G-zaxira. Qoldiq SPOF — svitch (ixtiyoriy ikkinchi svitch / kritiklar uchun zaxira Wi-Fi-SSID).
- Zaxira yo'qolishi (bitta yetib boriladigan avtoritet qoldi) = «yoqiq ushlab turish» triggeri (yuqoriga qarang).

### Revyu yopgan bo'shliqlar (v5.3)

- **Soat va taymer dreyfi.** Agent taymerni granted_seconds dan monoton lokal vaqt bo'yicha yuritadi (faqat ends_at bo'yicha emas), + tarmoq borida NTP-sinxronizatsiya. ends_at — solishtirish, monotonika — sanash manbai.
- **Bitta bo'sh PK uchun poyga.** Start external_pc_id ga lock oladi (first-wins): sessiya yaratilayotganda PK «band qilinmoqda» deb belgilanadi, unga ikkinchi to'lov rad etiladi/qaytarishga ketadi.
- **«To'ladi-lekin-startlamadi» qaytarishi.** MVP splitsiz — pul darhol klub merchant'ida; qaytarishni to'lov tizimi klub merchant'i ostida boshlaydi (reversal/refund). (Settlement-split-mantiq V2 marketpleysi bilan qaytadi.)
- **ShM (PD) ga rozilik.** Telefonni mijozlar bazasiga yig'ish — aniq rozilik bilan (sahifada/botda katak/shart) va huquqiy asos bilan; O'z qonuni bo'yicha saqlash va o'chirish (14-bo'lim).
- **Agent/kontrollerni yangilash va orqaga qaytarish.** Staged rollout (kanareyka PK/klub) + apdeytdan keyin health-check muvaffaqiyatsiz bo'lsa avto-rollback; versiya faqat zal ko'tarilganda mahkamlanadi.
- **WoL siz PK.** Temir WoL/S3 ni barqaror bilmasa — bunday PK ni doim yoqiq ushlaymiz (deep-sleep'siz), inventarda belgilaymiz; uyg'otish o'rniga fallback.
- **Tarif/zona/paket ma'lumotlar modeli (ishlab chiqish uchun):** zone(id, name, tur), package(id, zone_id, seconds, price, chegirma), tariff(zona→soat-yakor), service(id, name, mxik, price) — BD sxemasi bilan rasmiylashtiruvi kerak.
- **Outbox chidamliligi.** Lokal saqlash USB-SSD da (SD emas), pul yozuvlarida fsync, navbat o'sishiga limit + alert; uzoq oflaynda to'lib ketish xavfida — naqd/vaucherlarga degradatsiya.

### Masshtab: bulutni to'lqindan himoya (thundering herd)

- Kontroller — agregator/qalqon: normada bulut har klubga ~1 ulanish ko'radi, har PK ga emas.
- Agent→bulut kanali — talab bo'yicha, doimiy emas; agentlar avtonom (kesh, lokal vaucherlar).
- Retraylarda backoff + jitter; bulut yiqildi → agentlar lokal rejimlarga (to'lqin yo'q); umumiy sabab — noto'g'ri yangilanish → canary-chiqarish. CDN/edge ortida zaxira endpoint, rate-limit, load-shed. Telemetriya — butun klub uchun pachkalarda.

## 10. Oflayn-to'lov (kontrollerlar ishdan chiqishi / aloqa yo'q)

Sessiya avtorizatsiyasi — PK agentida, bir nechta mustaqil validator, istalgan bittasi yetarli:

1. **Kontroller LAN orqali** (norma).
2. **Bulut to'g'ridan-to'g'ri** — agentning zaxira kanali; internet tirik bo'lib «ikkala kontroller yotgan» holat (talab bo'yicha, backoff+jitter bilan — 9-bo'lim).
3. **Imzolangan kod-vaucher** — tikilgan ochiq kalit bilan lokal tekshiruv, blok ekranida kiritish maydoni, takrordan PK + nonce bog'lash. Tarmoqsiz ishlaydi.
4. **Menejer master-blokdan-chiqarishi + naqd** — nol bog'liqlik, doim ishlaydi. Eng oxirgi chegara: hatto shu yerda ham avval onlaynni taklif qilamiz (u tizimda ko'rinadi), naqd — faqat onlayn imkonsiz bo'lsa (anti-o'g'irlik — 11-bo'lim).
5. **Faqat veb-ilova** (server va kontroller ishlamayapti): to'lov provayder orqali to'g'ridan-to'g'ri; sessiya/to'lov/vaucher ma'lumotlari — vebda lokal (IndexedDB/localStorage) keyinchalik sinxronizatsiya bilan (davriy sink bulut/kontroller ishga tushdimi tekshiradi va holatni yetkazadi).

**Nuans:** uxlayotgan PK ni tarmoq orqali faqat kontroller uyg'otadi → nosozlik rejimida PK larni yoqiq ushlab turish yoki qo'lda yoqish (tugma). Yoqiq PK lokal ochiladi (klavish) va istalgan yo'l bilan. Barcha aylanma berilishlar lokal loglanadi va aloqa tiklanganda jamlanadi.

**Mijozda aloqa yo'q bo'lganda to'lovga kirish:**

- Mehmon WiFi to'lov walled-garden'i sifatida: alohida SSID/VLAN (o'yin PK laridan izolyatsiya), faqat to'lov domenlariga whitelist, tezlik limiti, captive-portal to'lov sahifasini ochadi; PK identifikatsiyasi — portalda raqam kiritish.
- Klubda internet umuman yo'q: onlayn to'lov imkonsiz → 4G-router tavsiyasi (u PK lar ishi uchun ham kerak), qo'shimcha zaxira naqd / vaucherlar / SIMli terminal.

## 11. Menejer tizimi (qo'lda yo'l — eng oxirgi chegara)

- Menejer veb-PWA si (hech narsa o'rnatilmaydi, yorliq «o'rnatish» mumkin) klubning boshqaruv tarmog'idagi qurilmada. Lokal kontroller beradi → internetsiz ishlaydi; service worker qobiqni keshlaydi, kontroller ishdan chiqsa keshdan yuklanadi va agentlarga LAN orqali to'g'ridan-to'g'ri boradi. Nuans: PWA HTTPS talab qiladi → kontrollerda lokal sertifikat (mkcert / lokal CA).
- PK setkasi (bo'sh/band/uyquda/remont/diqqat) + start/uzaytirish/tugatish/yoqish/uyqu/o'chirish.
- **Prioritet — onlayn-to'lov, naqd — eng oxirgi punkt.** Hatto qo'lda bosqichda ham menejer avval onlayn-to'lovni taklif qiladi: u avtomatik qayd etiladi va tizimda ko'rinadi. Naqd — faqat onlayn imkonsiz bo'lganda.
- Transport narvon bo'yicha: kontroller orqali → LAN orqali agentga to'g'ridan-to'g'ri → PK ning o'zida master-kod. Menejer ilovasi LAN orqali WoL ni o'zi yubora oladi.

### Menejer / egasi paneli — V1 UI-talablari

- Rollar: egasi / menejer / kassir («Admin» roli «Menejer»ga qayta nomlangan).
- «Kompyuterlar» sahifasi (nomdan «QR» olib tashlansin) + jadvalda to'g'ridan-to'g'ri ustunda zona bo'yicha filtr (jadvalda mavjud zonalar selekti).
- «Dashbord» (avvalgi «Hisobot») — sukut bo'yicha bosh sahifa.
- Zonalar: tahrirlashda status «Remont»ni o'z ichiga oladi → zonaning barcha PK lari «Remont» plashkasini oladi.
- Paketlar: zona bittadan ko'p bo'lsa zonalar bo'yicha tablar; tab bosishda filtrlash.
- Yangi zona / yangi paket / yangi PK formalari faqat yaratish tugmasi bosilgandan keyin ko'rinadi va yaratilgandan keyin yana yashirinadi.
- «Tartib» ustuni hozircha olib tashlansin (kerak emas).
- Sessiya yonida mijoz raqami maydoni — menejer Telegram raqamini kiritadi va bot orqali vaucher yuboradi; raqam bazada saqlanadi (mijozlar bazasi, 6-bo'lim).

### Pul o'g'irlanishidan himoya (ayniqsa naqd)

Onlayn pulga odam tegmaydi → o'g'irlaydigan narsa yo'q. Shuning uchun — maksimal onlayn, minimal naqd, naqdga qattiq audit:

- Har bir naqd sessiya menejer ID si, PK, davomiylik, summa, vaqt bilan yoziladi.
- Kassa sverkasi: «kassadagi naqd = naqd sessiyalar summasi»; egasi tafovutlarni ko'radi.
- Naqd — alohida, belgilangan harakat, sabab kodi bilan («jim» defolt emas).
- Anomaliyalarga alertlar (bitta menejerda ko'p qo'lda naqd, to'lovsiz blokdan chiqarishlar, tungi sakrashlar).
- Ixtiyoriy: smenaga naqd limiti, egasi tasdig'i. Imzolangan buyruqlar; hammasi loglanadi.

### Ommaviy operatsiyalar (V2)

Menejer tanlangan PK lar bo'yicha ommaviy yoqish/o'chirish/remont qila oladi. «Ahmoqdan himoya»: (1) «rostdan o'chirish/remont?» tasdig'i; (2) keyingi oynada — hozir o'ynalayotgan aktiv sessiyalar ro'yxati; menejer avval barcha sessiyalarni tugatadi, faqat keyin o'chirish/remont/boshqalar ochiladi.

## 12. Masofaviy o'yin (V5–V6)

- O'yin klub PK sida ishga tushadi; o'yinchiga videostrim boradi, orqaga — boshqaruv.
- Xost — Sunshine/Parsec; mijoz — Moonlight/Parsec. Orkestratsiya: PK tanlash → WoL → xost ishga tushirish → token → billing.
- NAT — relay-server yoki mesh-VPN (Tailscale/ZeroTier).
- Sessiyalar izolyatsiyasi va orqaga qaytarish; latentlik (region ichida); o'yin litsenziyalari — hisobga olish.

## 13. Xavfsizlik

- **Kartalar:** faqat provayder tokenini saqlaymiz, raqamni emas (PAN); karta kiritish — provayder tomonida.
- **Vaucherlar:** bulutning maxfiy kaliti bilan imzolangan, agent tikilgan ochiq kalit bilan tekshiradi; takrorga qarshi PK + nonce bog'lash.
- **Menejer buyruqlari:** tikilgan menejer kaliti bilan imzolangan; master-kod — bir martalik/almashtiriladigan.
- **Sessiyalar:** izolyatsiya (ayniqsa masofaviy o'yinda), sessiyadan keyin PK ni orqaga qaytarish.
- **Agentni aylanib o'tishga qarshi:** Task Manager, Safe Mode, ikkinchi Windows foydalanuvchisi orqali aylanib o'tishni bloklash; agent — himoyalangan xizmat + UI, olib tashlashga urinishda tiklanish.
- **Audit:** barcha qo'lda va aylanma harakatlar loglanadi (kim/PK/summa/vaqt), kassa sverkasi.
- **Idempotentlik** barcha pul operatsiyalarida (ikki marta yechishdan himoya).
- **Ma'lumotlar va roziliklar:** kartani saqlash/avto-yechishga aniq rozilik; O'zR shaxsiy ma'lumotlar va kiberxavfsizlik qonunlariga muvofiqlik.
- **O'yinlarga kirishlar (avtologin, V3):** faqat shifrlangan saqlash (hech qachon plaintext emas), afzal — rasmiy integratsiyalar/OAuth; 2FA (Steam Guard) va platformalar ToS ini hisobga olish; ixtiyoriy va rozilik bilan. Sessiya tugaganda — barcha akkauntlardan avto-logaut va toza holatgacha tozalash (mijozlar orasida zachistka). Xavfsiz baza — «mijoz o'zi login qiladi, tizim chiqishda kafolatli tozalaydi».
- **Start QR vs uzaytirish QR:** statik nakleyka — bo'sh PK da start; bandda «band» ko'rsatadi, to'lov yopiq. Uzaytirish — faqat dinamik, sessiyaga bog'langan ekrandagi QR → begona uzaytira olmaydi va aralasha olmaydi; «do'stim to'ldiradi» — o'yinchi kodni o'zi ulashadi. Akkaunt/hamyon (V2) — faqat login ortida, nakleykaning quruq skani bilan emas.

## 14. Regulyatorika va fiskalizatsiya (O'zbekiston)

**Disklamer:** mo'ljallar, yuridik xulosa emas. Ishga tushirishdan oldin — mahalliy yurist konsultatsiyasi.

**Fiskalizatsiya — majburiy va kritik.**

- Onlayn/virtual kassa majburiy: har bir operatsiyaga (naqd, karta, QR, e-commerce/elektron to'lov tizimlari orqali to'lov) xaridorga QR-kodli va fiskal belgili elektron fiskal chek (ChEK) beriladi, real vaqtda Soliq.uz ga. 2023 dan onlayn-kassa integratsiyasi e-commerce uchun majburiy; API-integratsiya afzal.
- Berilmagan ChEK uchun jarima — operatsiyaga taxminan 5–10 BHM (≈$130–260), SK 221-modda.
- **Xulosa: har bir to'lov fiskal chek tug'dirishi kerak.** Chek bo'yicha sotuvchi — klub (uning soliq shaxsi). MVP yechimi: onlayn to'lovlar fiskalizatsiyasini Payme/Click klub kassasi orqali qiladi — klub merchant'i uning OFD/kassasiga bog'langan, chek klub nomidan uriladi (to'lovga mxik/IKPU pozitsiyalari, nom, narx, birlik, QQS uzatamiz). MVP da Soliq bilan to'g'ridan-to'g'ri integratsiya qilmaymiz. Clubpay ning to'g'ridan-to'g'ri Soliq-integratsiyasi — keyinroq opsiya (V2+), agar provayder fiskalizatsiyasi yetkazish UX ini qoplamasa.
- Naqd: to'lov API si ularni fiskallashtirmaydi → naqdni klub kassasi uradi (uning onlayn-kassasi); tizimda chek reference'ini saqlaymiz.
- **«Blokdan chiqarish vs chek» uzilishi:** PK ni muvaffaqiyatli to'lov bo'yicha blokdan chiqaramiz; chek asinxron kelsa — fiscal_pending ushlab turamiz va N daqiqada kelmasa alert qilamiz. To'lov yozuvi (PaymentOrder): order_id, to'lov id si, status, summa, klub, PK, tarif, sessiya, chek havolasi/raqami, fiskalizatsiya statusi.
- Chekni mijozga yetkazish: to'lovdan keyin sahifada chekni tap-havola + QR sifatida ko'rsatamiz (provayder nima bersa).
  - V1: to'lov sahifasida havola/QR.
  - V2: chek akkaunt/kontaktga bog'langan → avto-yetkazish / mijoz raqamiga ro'yxatga olish.
  - V3: bog'lanmagan bo'lsa — ro'yxatdan o'tish taklifi. (Yetkazishning to'liq nazorati — to'g'ridan-to'g'ri Soliq-integratsiyaga o'tishda, V2+.)

**To'lov litsenziyalash — bizni «pul ushlamaymiz» qutqaradi.**

- O'zRda MB litsenziyalaydigan kategoriyalar: to'lov tizimlari operatorlari va to'lov tashkilotlari (PSP). To'lov tashkiloti o'zi e-money chiqara olmaydi — emitent faqat bank/MB.
- Bizning dizayn (pul to'g'ridan-to'g'ri klub merchant'iga, hamyon chiqarmaymiz, MVP da split yo'q) litsenziyalangan provayderlar ustida tex-qatlam bo'lib ishlashga va to'lov tashkiloti talablariga tushmaslikka imkon beradi. Lekin V2 dagi hamyon/balans (6.2-bo'lim) — bu allaqachon e-money → hamkor-bank yoki aylanma sxemalar kerak. «Pul ushlamaymiz» afzalligi V1 da amal qiladi, V2 da haqiqiy balansda teskari buriladi.

**Ma'lumotlar va ShM lokalizatsiyasi (arxitektura uchun muhim).**

- «Shaxsiy ma'lumotlar to'g'risida»gi qonun (27-1-modda, 2021 dan) O'zR fuqarolari ShM ini O'zbekistondagi serverlarda saqlashni, BD ni regulyator davlat reyestrida ro'yxatga olishni talab qilardi. 2026 da qoidalar yumshatilmoqda: aksariyat ShM ni shartlar bilan chet elda saqlash mumkin, lekin sezgir kategoriyalar (biometriya, genetika, telekom-ma'lumotlar) — faqat mamlakat ichida. Qonun harakatda — dolzarb tahrirni tekshirish kerak.
- Xulosa: ma'lumotlar rezidentligini rejalashtirish (mijozlar ShM li server/BD — UZ da yoki transchegaraviy uzatish shartlariga rioya bilan) va ShM BD sini regulyatorda ro'yxatga olish.
- V1 — minimal ShM (anonim scan-pay-play); ShM V2 da paydo bo'ladi (telefon, karta tokeni, tarix) → minimal zarurni saqlash, saqlash muddatlarini belgilash, roziliklar (13-bo'lim).

**Klublarga oid profil masalalar (joyida tasdiqlash):**

- Litsenzion dastur: klub litsenzion OS va o'yin klientlarini ishlatishi, huquq egalari bilan shartnomalari bo'lishi shart; piratlik → jarimalar. Bizning soft litsenziyalar hisobiga yordam berishi kerak (16-bo'lim).
- Voyaga yetmaganlar va soatlar: ma'lum soatlarda bolalar bo'lishiga cheklovlar tipik; buzilishlarga egasi javob beradi. Tizim yosh va soatlarni hisobga olishi/cheklashi mumkin.
- Klub ruxsatnomalari: sanitariya-epidemiologiya xulosasi, yong'in deklaratsiyasi (klub tomonida).

## 15. Monetizatsiya

### Klub qanday pul topadi

- Bekor turishni to'ldirish uchun dinamik tariflar (ertalabki/ish kunlari/tungi paketlar, happy hour).
- Bo'sh joylarda masofaviy o'yin (marketpleys orqali) — mahsulot kengaytmasi (V5–V6).
- F&B (ovqat/ichimliklar), merch, vitrina-ekranlarda reklama.
- Qo'shimcha xizmatlar (yelka/bosh massaji va sh.k.) — egasi sozlaydigan katalog: egasi xizmat va narxni o'zi qo'shadi, POS orqali sotiladi / shell'dan buyurtma qilinadi. Har bir xizmat nom/IKPU/narx olib yuradi → fiskal chek uchun OFD ga ketadi (14-bo'lim).
- ❌ **Mayning rad etilgan:** Ethereum PoS ga o'tgandan keyin deyarli foydasiz, temirni yeyilish bilan o'ldiradi va energiya tejashga zid.

### Biz (platforma) qanday pul topamiz — bosqichli model

**MVP-moliya modeli (muhim, mahkamlangan).** Splitsiz. Xizmat sotuvchisi — klub; har klubning Payme/Click'da o'z merchant/kassasi, pul to'g'ridan-to'g'ri klubga boradi — biz pul oqimida emasmiz. Clubpay obuna/o'rnatish/shartnoma bilan monetizatsiya qilinadi, har to'lovdan komissiya bilan emas. To'lovni faqat tasdiqlaymiz (klub merchant-kredlari bo'yicha callback/so'rov), PK ni blokdan chiqarish uchun. Split, to'lovdan-komissiya, yagona hamyon va to'g'ridan-to'g'ri Soliq-integratsiya — V2+ ga kechiktirilgan, marketpleys kerak bo'lsa.

Model bosqichga qarab o'zgaradi: pilotda real pul turadigan narsaga to'laymiz (chiqish, temir, sozlash, qo'llab-quvvatlash); masshtabda — platforma qatlami.

- **V1 / pilot — pullik** (aks holda yuqori hands-on qoplanmaydi): o'rnatish to'lovi + PK uchun obuna (Fixed, masalan ~55k so'm/PK/oy, to'lovlardan bizning komissiyasiz — pul oqimida emasmiz). Qo'shimcha minimal oylik to'lov va ixtiyoriy 24/7-qo'llab-quvvatlash. Pilot — chegirma, lekin abadiy bepul emas: shartnoma ~6 oydan.
- **V2 (marketpleys, ixtiyoriy):** marketpleys/yagona hamyon paydo bo'lsa — shunda split ham, ~10–15% komissiya ham faqat marketpleys olib kelgan sessiyalarga (bu alohida moliya sxemasi, MVP emas).
- **Masshtab:** o'rnatish mahsulotlashuvi va marketpleys/reklama/Pro-tirlar ishga tushishi bilan bazani pasaytirsa bo'ladi — «iCafe8 dan past» pozitsiyalash shu yerda yashaydi.

Pozitsion tanlov: premium-pullik-ishonchli (erta bosqich) vs arzon-ommaviy (masshtabda). Raqamlar — mo'ljallar (so'm/USD ~12.5k).

### Klub uchun tariflash (klub bizdan nima sotib oladi)

Bu fichalarning tirlar bo'yicha taqsimoti (nima kiradi); narx bosqichliligi — yuqorida. iCafe8 Xitoyda mohiyatan bepul (Shunwang masshtabda reklama/distributsiyadan topadi); g'arbliklar PK dan oladi: iCafeCloud ~$2.5/PK/oy, ggLeap ~$5, SENET ~$3.75 (modulli), diskless (CCBoot) +$4–5.

Tirlar bo'yicha taqsimot (SENET kabi — klub keragini oladi):

- **Core:** billing, QR-to'lov, blok/taymer, oflayn, fiskal chek, bazaviy panel/hisobotlar. Pilotda — obunada; masshtabda bepul bo'lishi mumkin (iCafe8 usuli).
- **Pro (boshqaruv, V3):** o'yinlar/license pools boshqaruvi, POS/do'kon, profillar/avtologin, sessiya sbrosi, chat, kengaytirilgan analitika, turnirlar. Modulli (bazaga qo'shimcha).
- **Diskless (agar qilsak, V3+)** — bozor bo'yicha alohida addon (~$4–5/PK).
- **Marketpleys (V2+)** — ro'yxatga chiqish bepul; olib kelingan sessiyalardan topamiz (split bilan), klubdan oldindan to'lovsiz.

**Klubga pul oqimi (MVP):** to'lov to'g'ridan-to'g'ri klubning Payme/Click'dagi merchant/kassasiga boradi — Clubpay birovning pulini ushlamaydi va to'lovdan komissiya olmaydi. Egasi paneli klub oborotlarini (uning merchant/kassasidan) va Clubpay obuna hisobini alohida ko'rsatadi — bu biz orqali hisob-kitob emas.

### Reklama monetizatsiya sifatida (uzoq muddat, V4–V5)

Reklama tushumi — geymingdagi asosiy oqimlardan biri (iCafe8/Shunwang'da bu yadro; kibersportda homiylik/reklama daromadning >60% ini beradi). Bizning richag — klublar tarmog'i reklama inventari sifatida:

- Shell va bekor turish/vitrina ekranlarida nativ reklama — o'yinni uzmaydigan (shunday yaxshiroq ishlaydi).
- Brendlangan zonalar va turnirlar (homiy zona/ivent uchun to'laydi).
- Sempling va promo (klublarda energetiklar/sneklar).
- Platforma ko'p klublarni agregatlaydi → brendlarga alohida klub ololmaydigan tarmoq kampaniyalarini sotadi.

Yengil brending/homiylik zonalari — V4 dan; to'laqonli reklama tarmog'i — V5.

### Kollaboratsiyalar va hamkorliklar

O'zbekistonga mos prioritetlar (jahon amaliyotidan hammasi darhol qo'llanmaydi):

- **Hamjamiyat va tadbirlar — prioritet (V3–V4):** kibersport-tashkilotlar, strimerlar, universitetlar → umumiy turnirlar, kontent, «harakat». Shu yerga — kompaniyalar/tashkilotchilardan turnirlar: kompaniya akkaunt ochadi va xodimlar/filiallar orasida turnir o'tkazadi («tashkilotchi» roli + ochiq API — 4-bo'lim; V4).
- **Milliy o'yinchilar reytingi (V5–V6):** mijozlarimiz reytingi — O'zbekistondagi birinchi shunday baza. Kibersport-federatsiya/vazirlik bilan bog'lanish (ularda o'yinchilar bo'yicha ma'lumot yo'q): toplarni saralash, milliy terma jamoalar yig'ish, o'yinchilarni motivatsiya qilish; istiqbolda — o'z jamoalarimiz. Kuchli uzoq muddatli moat.
- **Energetiklar/F&B + ovqat yetkazish (V4):** lokal brendlar (muzlatgichlar, sempling, brendlangan turnirlar) bizning POS ga; buyurtmani lokal yetkazish bilan integratsiya.
- **Banklar/fintex:** o'tuvchi hamyon uchun hamkor-bank — kritik yo'lda (bu e-money, marketing emas; 14-bo'lim); keshbek-ko-brend — ixtiyoriy.
- **Temir/periferiya — uzoq istiqbol/past prioritet:** UZ da yetkazib berish asosan Xitoydan va to'g'ridan-to'g'ri ishlaydigan yetkazib beruvchi bor; jahon brendlari bilan kollab (Intel/NVIDIA/…) — keyin, umuman bo'lsa.
- **Telekom, nashriyotlar/o'yin distributsiyasi** — savol ostida, uzoq istiqbol.
- Global amaliyot (Intel/Red Bull/Mastercard/Riot…) — kelajak mo'ljali, startga reja emas.

## 16. Funksional qamrov va backlog (bozorga muvofiqlik)

Yetuk tizimlar bilan solishtirish (SmartLaunch, SENET, ggLeap, Antamedia, iCafeCloud) — bozor yadrosini o'tkazib yubormaslik uchun.

| Ficha | Bizda qayerda |
|---|---|
| Vaqt bo'yicha tariflash, PK bloklash/nazorat | V1 |
| Masofadan boshqarish, real vaqt monitoring | V1 (menejer paneli) |
| Zonalar va differensiyalangan tariflar | V1/V2 (6-bo'lim) |
| Mijoz to'lovi, hamyon, sodiqlik | V1 (to'lov) / V2 (hamyon) |
| Onlayn bron | V2 (6.1) |
| Hisobotlar va analitika | V2 (egasi paneli) |
| O'yinlar boshqaruvi: katalog, yangilanishlar, license pools | V3 — bozor yadrosi |
| O'yinchi profillari: launcher/o'yinlarga avtologin (Steam/ICCup…) | V3 (ixtiyoriy, shifrlash bilan) |
| Sessiya sbrosi / mijozlar orasida akkauntlar tozalash | V3 |
| POS / do'kon + qo'shimcha xizmatlar (F&B, tovarlar, massaj va h.k. — egasi katalogi) | V3 |
| Menejer↔mijoz chat | V3 |
| Smena/xodimlar hisobi | V2 → V3 da kengaytirish |
| Turnirlar va tadbirlar | V3 — prioritet |
| Kompaniyalar/tashkilotchilardan turnirlar (rol + ochiq API) | V4 |
| O'yinchilar reytingi, hamjamiyat, invaytlar, matchmeyking | V4 (milliy reyting + federatsiya — V5–V6) |
| Lending/sayt + ochiq API portali | lending boshidan / API V4 |
| Konsollar (faqat PK emas) | V4 |
| VR | V7 |
| Bo'sh joylarda masofaviy o'yin (cloud/remote play) | V5–V6 (12-bo'lim) |
| Reklama va kollaboratsiyalar/hamkorliklar (monetizatsiya) | V4–V5 (15-bo'lim) |
| Brendlanadigan mijoz shell'i (o'yinlar kutubxonasi, F&B-buyurtma, sodiqlik, POS) | V1 baza → V3 to'liq (22-bo'lim) |
| Disksiz yuklash (diskless) | V3 — hozir emas, u yerda kerakligini hal qilamiz |

Relizlar bo'yicha to'liq taqsimot — yo'l xaritasida (21-bo'lim). O'yinlar boshqaruvi / license pools — bozor yadrosi (va 14-bo'limdagi litsenzion dastur talabini yopadi), V3 ga olamiz.

## 17. Klub onbordingi va temir (BOM)

**Klub onbordingi:**

- Egasi ulanish uchun ariza qoldiradi (ishlaydigan klubni o'zi yaratmaydi — chiqish, Pi/Core/Agent o'rnatish, PK/to'lovlar/fiskalizatsiya sozlash kerak). Klubni superadmin (Clubpay jamoasi) yaratadi va provision qiladi: klub ochadi, egasini tayinlaydi, zonalar/paketlar/PK lar, Payme/Click, Soliq/OFD, aktivlashtiradi. Nom/soliq rekvizitlari (chek bo'yicha sotuvchi = klub) shu bosqichda yig'iladi. To'liq self-service onbording — keyinroq (V2+).
- Klubning Payme/Click'dagi merchant/kassasini ulash (uning rekvizitlari; fiskalizatsiya klub nomidan uning OFD si orqali). MVP da split kerak emas (20-bo'lim).
- Kontroller provisioning'i (Pi klub ID si bo'yicha konfiguratsiyani tortadi), tarmoq/VLAN sozlash.
- PK larga agentlar o'rnatish, zonalar va tariflarni belgilash.
- Retroreflektiv QR-nakleykalar chop etish (URL = PK ID si).
- Oflayn-rejim, to'lov va fiskal chek testi.

**Klublar tarmog'i:** bitta egasi/tarmoq klublari platforma darajasida bog'lanadi (umumiy egasi akkaunti, multiklub-panel, mijoz uchun esa — tarmoq bo'ylab yagona balans, 6.2-bo'lim). Klublarni tarmoqqa bog'lash modelini V2 da o'tuvchi hamyon bilan birga ishlab chiqish.

**Klubga nima kerak (BOM):**

- Kontroller: Raspberry Pi (4/5) yoki mini-PK N100, USB-SSD bilan, + UPS, + sovuq zaxira Pi.
- Tarmoq: VLAN li boshqariladigan kommutator (o'yin / boshqaruv / mehmon walled-garden); kerak bo'lsa zaxira/uplink sifatida 4G-router.
- Har bir PK ga: agent (soft) + retroreflektiv QR-nakleyka.
- Ixtiyoriy: menejer PWA si uchun qurilma (planshet/stoykadagi PK).

## 18. Nofunksional talablar (NFR)

- **Mavjudlik (availability):** bazaviy pay-and-play sikli bulutsiz lokal ishlaydi; internet/bulut/bitta kontroller ishdan chiqsa klub to'xtamaydi.
- **Latentlik va buyruq timeout'i:** buyruq bajarish timeout'i (start/extend/end) — 10 s (MVP); «to'lov → blokdan chiqarish» maqsad latentligi — bir necha soniya. Kelgusi relizlarda parametr KPI sifatida qattiqlashadi: target-latentlikni pasaytiramiz (masalan ≤3–5 s), adaptiv timeout, muvaffaqiyatli blokdan chiqarishlarga SLA metrikasi (21-bo'lim). Masofaviy o'yin — region ichida past ping.
- **Masshtab:** bulutga yuk ~ klublar soni (kontroller-agregator), PK lar soni emas.
- **Ma'lumotlar ishonchliligi:** server — haqiqat manbai; kontroller sarflanadigan; outbox/inbox sverkasi, idempotentlik.
- **Xavfsizlik:** PAN o'rniga tokenlar, imzolar, sessiyalar izolyatsiyasi, audit (13-bo'lim).
- **Fiskalizatsiya:** har bir operatsiyaga real vaqtda Soliq'ga chek (14-bo'lim).
- **Lokalizatsiya:** interfeyslar o'zbek va rus tillarida; valyuta — so'm (UZS).

## 19. Risklar va farazlar

- Monetizatsiya — obuna/shartnoma (MVP), to'lovdan komissiya emas; split kritik emas (faqat V2+ marketpleysi uchun kerak).
- Klub nomidan fiskalizatsiya Payme/Click klub kassasi orqali chek uradimi-yo'qmi ga bog'liq — bu production-shart (20-bo'lim); to'g'ridan-to'g'ri Soliq — zaxira yo'l.
- Hamyon = e-money — hamkor-bank yoki aylanma sxemalar kerak (6.2, 14-bo'limlar). «Bitta balans barcha klublarda» killer-fichasi shunga bog'liq (o'tuvchi open-loop).
- Fiskalizatsiya — majburiy; provayder/integratsiyaga bog'liq (14-bo'lim).
- O'yinlar boshqaruvi / diskless — bozor yadrosi; V3 ga rejalashtirilgan (21-bo'lim), o'tkazib yubormaslik.
- Masofaviy o'yin latentligi — geografiyani cheklaydi (V5–V6).
- Internet-klublar regulyatorikasi (yosh/soatlar/dastur litsenziyalari) — joyida aniqlash.

## 20. Ochiq savollar

Adresatlar har xil: provayderga — tashqi faktlar, qo'ng'iroq/sandbox'da tekshiriladi (egasi qarori emas); arxitektura — biz/ishlab chiqish uchun; regulyatorika — yurist uchun; mahsulot qiymatlari — egasi uchun. Provayder punktlariga bizning defolt qo'yilgan — qo'ng'iroqda tasdiqlashgina qoladi.

**To'g'ridan-to'g'ri integratsiyalar (bizning qaror) — har tomondan nimani tasdiqlash:**

*To'lov tizimlari (Click / Payme / Uzum / kartalar — to'g'ridan-to'g'ri integratsiya qilamiz):*

- Aniq summaga to'lov yaratish, mijoz ilovasiga deeplink/redirect, sahifamizga qaytish.
- Status haqida callback + imzo tekshiruvi; fallback sifatida status so'rovi; blokdan chiqarish faqat muvaffaqiyatda.
- Click (doklar bo'yicha): SHOP API callback-rejimi (bizning URL ga Prepare/Complete); status so'rovi `GET /v2/merchant/payment/status_by_mti/:service_id/:merchant_trans_id/:date` (avtorizatsiyalangan, bizning order_id bo'yicha); V2 rekurrentlari uchun karta tokenlari (card_token); OFD/IKPU fiskalizatsiyasi. So'rov limitlarini (fallback-so'rov uchun) va kontrollerda sekret saqlash shartlarini aniqlash (A/B variant — 9-bo'lim).
- Klubga merchant (MVP): har klub — o'z merchant/kassasi, pul to'g'ridan-to'g'ri klubga, fiskalizatsiya provayder tomonidan klub nomidan. MVP da split kerak emas (faqat V2+ marketpleysi uchun).
- Qaytarishlar (to'liq; qisman — bo'lsa), holdlar (V2 bronlari uchun), tokenizatsiya/rekurrentlar (zero-tap V2; Payme'da — Subscribe API).
- Kanallar bo'yicha komissiyalar, klublarga to'lovlar muddatlari va sverka API si, yuridik jihatdan merchant kim, shartnomalar.

*Soliq (fiskalizatsiya — to'g'ridan-to'g'ri integratsiya qilamiz):*

- Aniq klub nomidan ChEK shakllantirish: mxik/IKPU maydonlari, package_code, nom («kompyuter vaqti» / «PK ijarasi» / «o'yin vaqti»), QQS, birlik (daqiqa/soat/xizmat).
- Blokka bitta chek yoki har uzaytirishga; naqd — ham Soliq/onlayn-kassa orqali; chekda qaytarishni aks ettirish.
- Chekni mijozga yetkazish: sahifada tap-havola/QR (V1) → raqamga avto-ro'yxat (V2).

*Zaxira variant — agregator (Rahmat/Multicard):* jamoa sandbox'ni sinagan (dev-mesh.multicard.uz), flou ishlaydi (invoice → checkout_url/deeplink → callback sign = md5(store_id+invoice_id+amount+secret) → uuid bo'yicha status → full refund; summalar tiyinlarda; split fiks-summa bilan). Lekin receipt_url ≠ Soliq ChEK, naqdni u fiskallashtirmaydi. To'g'ri yo'l juda og'ir chiqsa fallback sifatida saqlaymiz.

*Ekvayring yig'imi:* kim to'laydi (klub/mijoz/biz) (defolt: sozlanadigan, iloji boricha mijozga).

**Arxitektura qarori:**

- V1 — to'lov tizimlari (Click/Payme/Uzum) + Soliq bilan to'g'ridan-to'g'ri integratsiya; Rahmat — faqat UX-namuna va zaxira agregator (6-bo'lim).
- Menejer paneli: PWA uchun lokal HTTPS sxemasi (mkcert / lokal CA / sertifikatli lokal domen).
- Kontroller discovery (mDNS / setup-QR orqali juftlash) — prototip qilish (23-bo'lim).
- Agent: shakli (Windows-xizmat + UI / shell replacement / overlay) va anti-aylanib-o'tish (Task Manager, Safe Mode, ikkinchi Windows foydalanuvchisi); klubni buzmasdan agent/kontrollerni yangilash mexanizmi.
- V1 muvaffaqiyat metrikasi: «to'lov → X soniyada blokdan chiqarish». Cold-backup Pi — majburiymi yoki pullik opsiya; USB-SSD majburiymi yoki pilotda SD; router/kommutatorni kim sozlaydi (VLAN/guest/walled-garden); eski temirda xatti-harakat (beqaror WoL/S3).

**Regulyatorika (O'zbekiston):**

- «Pul ushlamaslik» to'lov tashkiloti litsenziyalashiga tegmasligini tasdiqlash.
- Hamyon (V2): balans hamkor-bank orqalimi (e-money) yoki provayder hamyonlari / karta-fayl-ustida? closed-loop vs open-loop? Float hisobi, segregatsiyalangan hisoblar, qoldiq muddati.
- Internet-klublar profili: tashrif buyuruvchilarni ro'yxatga olish, kontent filtrlash, soatlar/yosh, dastur litsenziyalari.
- ShM lokalizatsiyasi: mijozlar ShM li server/BD ni qayerga joylashtirish (UZ-rezidentlik yoki transchegaraviy uzatish shartlari), ShM BD sini regulyatorda ro'yxatga olish (14-bo'lim).
- V2 uchun — ro'yxatdan o'tishda KYC/yosh; shaxsiy ma'lumotlar, kiberxavfsizlik.

**Sukut bo'yicha mahsulot qiymatlari (belgilash kerak):**

- Uyqugacha idle-timeout, ogohlantirish chegaralari va greys davomiyligi (defolt: chegaralar 10/5/1 daq, greys 10 daq, idle-uyqu 10 daq).
- Bron: depozit hajmi, kelish greysi, bekor qilish oynalari va foizlari.
- Ishlatilmagan qoldiq siyosati (V1): kuyadi yoki qoldiq vaucheri (defolt: vaucher; soniyalarda saqlash; istalgan musbat qoldiqqa beriladi; TTL 30 kun; o'tkazma qilinadigan) (6-bo'lim).
- Qoldiq vaucherini yetkazish (V1): bizning Telegram-bot orqali (SMS ishlatmaymiz).
- Hamyon (V2): avto-to'ldirish chegarasi va summasi, muddat qoidalari, xarajat limitlari.
- Sukut bo'yicha zonalar va tarif stavkalari.
- Davomga ushlab qoluvchi chegirma: o'yin vaqti bo'yicha hajmi; pik soat / bronda bostirish qoidasi (7-bo'lim).
- V1 tillari: rus, o'zbek (lotin) va/yoki o'zbek (kirill) (defolt: ru + uz, lotin birinchi).
- Rollar va naqd: egasi / menejer / kassir; naqd sessiyani kim boshlashi mumkin, sabab kodi, smenaga naqd limiti, kassa sverkasi, tarmoq to'liq ishdan chiqqanda master-kod (11-bo'lim).
- Klub uchun tariflar (15-bo'lim): Fixed-obuna (MVP, to'lovdan komissiyasiz), o'rnatish to'lovi, minimal to'lov, yadro qachon bepul bo'ladi (masshtabda). Hybrid/komissiya — faqat marketpleys bilan (V2+).
- App Store / Google Play (V2): klubda vaqt uchun to'lov — real xizmat → karta bilan tashqi to'lov; qoidalarni aniqlash.

## 21. Yo'l xaritasi (V1–V10)

V1–V2 — 3-bo'limga qarang. Quyida V3–V10. V6+ — AI ga urg'u bilan yo'nalish ko'rinishi (vision), yo'l-yo'lakay aniqlashtiriladi.

- **V3 — operatsion yetuklik + turnirlar.** Latentlik KPI si: «to'lov → blokdan chiqarish» targetini qattiqlashtiramiz (MVP-10 s dan ≤3–5 s gacha), adaptiv buyruq timeout'i va muvaffaqiyatli blokdan chiqarishlarga SLA-metrika kiritamiz (18-bo'lim). Turnirlar va tadbirlar (jadval, setkalar, sovrinlar, liderbordlar) — prioritet. O'yinlar boshqaruvi (katalog, markazlashgan yangilanishlar/patchlar, license pools); POS/do'kon (F&B, tovarlar, interfeysdan vaqt sotish); qo'shimcha xizmatlar — egasi sozlaydigan katalog (massaj va sh.k.); menejer↔mijoz chat; smena/xodimlar hisobi. O'yinchi profillari: avtologin va sessiya sbrosi — to'langan sessiya startida saqlangan kirishlar yoki integratsiya bo'yicha tanlangan launcher/o'yinga kirish (Steam/ICCup va b.); tugatishda — barcha akkauntlardan avto-logaut va toza holatgacha tozalash (restore-on-reboot bilan birga), akkauntlar keyingi mijozga oqib ketmasligi uchun (kirishlar xavfsizligi — 13-bo'lim). O'yinchi profili prioritet o'yin va bazaviy reyting olib yuradi (profil/o'yin statlari asosida) — hamjamiyat fundamenti (V4–V6). Disksiz yuklash (diskless) — shu yerga olamiz, lekin kerakligini va qandayligini joyida hal qilamiz.
- **V4 — platformalar va hamjamiyat.** Konsollar (PK kabi boshqarish); a'zolik dasturlari/obunalar; o'yin nashriyotlari bilan kontent-hamkorliklar; V3 turnirlari ustiga kibersport-funksiyalarni kengaytirish (ligalar, mavsumlar). O'yinchilar reytingi va hamjamiyat: prioritet o'yin tanlash, klub/tarmoqdagi boshqa o'yinchilarni va ularning reytingini ko'rish, «birga o'ynaymiz» invaytlari va timmeyt qidirish (matchmeyking — qoidalar; AI-aniqlashtirish V8 da); reyting turnirlarga urug' (seed) bo'lib xizmat qiladi va jamoa tarkiblarini yig'ishga yordam beradi. Kompaniyalar/tashkilotchilardan turnirlar (tashkilotchi roli + ochiq API): kompaniyalar xodimlar/filiallar orasida turnir o'tkazadi. Birinchi hamkorliklar (energetiklar/F&B, hamjamiyat) va brendlangan/homiylik zonalari + shell'da yengil nativ reklama (15-bo'lim).
- **V5 — masshtabda monetizatsiya + milliy hamjamiyat (iCafe8 qatlami).** Milliy o'yinchilar reytingi (O'zbekistonda birinchi) — toplarni saralash, jamoalar yig'ish, kibersport-federatsiya/vazirlik bilan bog'lanish (ularda bunday baza yo'q — bu kuchli motivator va moat); istiqbolda — o'z jamoalarimiz (15-bo'lim). Bo'sh joylarda masofaviy o'yin (cloud/remote play) — yangi daromad oqimi (12-bo'lim), V5–V6 da ochiladi. Vitrina-ekranlarda reklama tarmog'i va to'laqonli kollaboratsiyalar/hamkorliklar (15-bo'lim); o'yinlar distributsiyasi/ko-marketing; marketpleys-fichalar (reytinglar, aksiyalar, talab bo'yicha narx dinamikasi); referal/sodiqlik-iqtisodiyot; merch va tadbirlar tiketingi.
- **V6 — ma'lumotlar va analitika (AI uchun fundament).** Yagona ma'lumotlar ko'li; egalariga BI-dashbordlar; kogorta tahlili; tushum/bandlik prognozi; tariflarda A/B.
- **V7 — operatsiyalar uchun AI (+ VR).** VR (PK/konsollar kabi boshqarish). AI-dinamik narxlash (talab bo'yicha yield); bandlik prognozi va xodimlar avto-jadvali; energiya avto-optimizatsiyasi (qachon uxlatish/uyg'otish); temirning prediktiv xizmati (telemetriya bo'yicha nosozliklarni bashorat qilish); to'lovlar/naqd anomaliyalari bo'yicha anti-frod.
- **V8 — mijoz uchun AI.** Ilovada AI-yordam assistenti; o'yinlar/tariflar/vaqt bo'yicha shaxsiy tavsiyalar; aqlli pushlar (kelish uchun eng yaxshi vaqt, nuqtaviy promolar); matchmeyking va timmeyt qidirish.
- **V9 — AI-agentlar va avtonom klub.** Agentli «administrator» (rutina: uzaytirishlar, nizolar, qaytarishlar, to'ldirishlar, joylashtirish); kompyuter ko'rishi (zal bandligi, xavfsizlik, yosh/soat nazorati — maxfiylik bilan); avtonom smenalar va talab bo'yicha F&B xaridlari; turnirlar/kontent/marketing AI-generatsiyasi.
- **V10 — AI-ekotizim va tarmoq.** Barcha klublar ustidan tarmoq AI si (benchmarking, «top-klublardagidek» tavsiyalar); yangi klublar uchun lokatsiyalarni prediktiv tanlash; klublar uchun AI-servislar marketpleysi; sifat/latentlikni AI-optimizatsiya qiluvchi bulutli geyming.

V6–V10 — yo'nalish ko'rinishi, mahkamlangan hajm emas; AI ma'lumotlar fundamentidan (V6) avtonom agentlar va tarmoq intellektigacha (V9–V10) o'sadi.

## 22. Agentning mijoz interfeysi (shell)

Agent — mahsulotning yuzi; UI chiroyli, tez, qulay bo'lishi kerak. Yetuk tizimlarning brandable-klientlari bilan solishtirish (ggLeap, SENET, SmartLaunch).

**Holatlar:** (1) blok/to'lov ekrani (7-bo'lim) — toza, jozibali; (2) sessiyadagi shell — ish stoli ustida o'yinlar va servislar vitrinasi, issiq klavish bo'yicha, o'yinga xalaqit bermaydi.

**Shell tarkibi (relizlar bo'yicha):**

- **V1 (sayqallangan minimum):** vaqt qoldig'i va balans-vidjet, shell'dan «uzaytirish/to'lash», chiroyli bezovta qilmaydigan bildirishnomalar (vaqt tugayapti, buyurtma tayyor), klub brendingi, uz/ru tillari.
- **V2:** profil/akkaunt, hamyon va sodiqlik-vidjet (keshbek/bonuslar), boy pushlar, temalar/brending.
- **V3:** o'yinlar kutubxonasi (muqovalar, qidiruv, kategoriyalar, sevimlilar, yaqindagilar, o'xshash/tavsiya etilganlar, bir klikda ishga tushirish); launcherlarga avtologin (Steam/Origin/Battlenet — 13-bo'lim); o'yindan chiqmasdan joydan bir necha klikda ovqat va ichimlik buyurtma qilish; joydan qo'shimcha xizmatlar buyurtmasi (massaj va sh.k.); do'kon/POS (vaqt, sneklar, periferiya); koinlar/mukofotlar (o'yinda topiladi, POS orqali sovg'alarga almashinadi); turnirlar/tadbirlar (ro'yxatdan o'tish, setkalar, liderbordlar); sots-qatlam va hamjamiyat (prioritet o'yin va o'yinchi reytingi bilan profil, klub/tarmoqdagi boshqa o'yinchilarni va reytingini ko'rish, «birga o'ynaymiz» invaytlari, timmeyt qidirish/matchmeyking, do'stlar, chat) va «menejerni chaqirish»/yordam tugmasi.

**UX tamoyillari:** tez ishga tushish va sessiyani yo'qotmasdan o'yin↔shell almashish; muqovali yirik plitkalar; bildirishnomalarning yagona chiroyli uslubi; klub brendiga moslashtirish; ko'p tillilik (uz/ru).

## 23. Kontrollerni aniqlash (discovery) va panellar aloqasi

Admin-panel (veb-PWA) va agentlar lokal kontrollerni topishi va internetsiz ishlashi kerak.

- **Discovery — mDNS/zeroconf:** kontroller LANda lokal nomni e'lon qiladi (controller.local / <clubid>.local); PWA va agentlar uni bulutsiz topadi. Nom lokal HTTPS-sertifikat bilan mos keladi (11-bo'lim).
- **Onbordingda juftlash:** menejer qurilmasi kontrollerdagi setup-QR ni skanerlaydi → kontroller manzilini saqlaydi va lokal sertifikatga ishonadi.
- **Manzil keshi:** PWA kontroller manzilini eslab qoladi → qayta qidiruvsiz oflayn qayta ulanadi.
- **Cloud-assist (tarmoq borida):** bulut-reyestr «klub → kontroller» discovery ni bootstrap qiladi; juftlashdan keyin bulut kerak emas.
- **Kontroller ishdan chiqishi:** PWA keshdan agentlarga LAN orqali to'g'ridan-to'g'ri boradi (11-bo'lim); agentlar — mDNS/kesh-ro'yxat bo'yicha.
- **Lokal aloqa:** buyruqlar va menejer↔mijoz chat LAN orqali kontroller orqali boradi — oflaynda ishlaydi.
- **Menejerning PK ustidagi harakatlari (paneldan):** start/uzaytirish/tugatish, yoqish/uyqu/o'chirish, bloklash, ekranga xabar, sessiyani qayta ishga tushirish/orqaga qaytarish.

## 24. Glossariy

- **Menejer** — smena operatori (avvalgi «admin»): qo'lda boshqarish, naqd qabul qilish. Rollar: egasi / menejer / kassir.
- **Bizning server** — platformamizning bulut backendi (haqiqat manbai, butun klublar tarmog'i). To'lov tizimi bilan adashtirmaslik.
- **To'lov tizimi** — tashqi (Click / Payme / Uzum / kartalar): unda to'lov yaratamiz va «to'landimi?» statusini so'raymiz.
- **Click/Payme ilovasi** — mijoz telefonida, u yerda to'lovni tasdiqlaydi; to'lov tizimiga tegishli, bizga emas.
- **Tarif bloki** — sotib olishga tayyor vaqt paketi (hajmiy chegirma bilan; 6-bo'lim).
- **Zona** — o'z stavkalariga ega PK lar guruhi (Standart / Pro / VIP; va konsol).
- **Kelish greysi** — bron startidan keyin kelish/kirish oynasi (aks holda depozit kuyadi).
- **Muzlatish (freeze)** — to'langan vaqt tugagandan keyin qo'shimcha to'lov uchun sessiyani «o'sha kadrda» ushlab turish.
- **float** — mijozlarning balansdagi pullari = bizning majburiyatlarimiz.
- **walled-garden** — faqat to'lov domenlariga kirish mumkin bo'lgan mehmon WiFi.
- **Validator** — agentda sessiya avtorizatsiyasining mustaqil manbai.
- **S3** — PK uyqu rejimi (xotira tirik, quvvat deyarli nol).
- **WoL** — Wake-on-LAN, PK ni magic-paket bilan tarmoq orqali uyg'otish.
- **PWA** — o'rnatiladigan veb-ilova, service worker orqali oflayn ishlaydi.
- **Split** — bitta to'lovni bir nechta qabul qiluvchiga taqsimlash. MVP da ishlatilmaydi (pul to'g'ridan-to'g'ri klub merchant'iga); faqat V2+ marketpleysi uchun kerak.
- **Hold (avtorizatsiya)** — kartada summani yechmasdan bloklash; keyin yechiladi (capture) yoki qo'yib yuboriladi (release).
- **e-money** — elektron pul; O'zRda emitent faqat bank/MB.
- **ChEK** — elektron fiskal chek, real vaqtda Soliq.uz ga uzatiladi.
- **diskless** — PK ni markaziy obrazdan disksiz yuklash (o'yinlarni ommaviy yangilash).
- **license pool** — sessiyaga mijozlarga tarqatiladigan umumiy o'yin akkauntlari/litsenziyalari puli.




