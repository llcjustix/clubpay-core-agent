# Litsenziya / Obuna / Hardware-lock — Backend uchun talablar

Maqsad: har bir o'rnatilgan Agent faqat **o'z clubi + o'z PC'si** uchun, faqat **faol obuna**
bo'lgan holatda ishlashi; token/config faylini boshqa kompyuterga ko'chirib ishlatib bo'lmasligi;
buni T0 (cloud) → T1 (controller/Pi) → T2 (admin PC) zanjirining hech bir bosqichida buzmasligi —
ya'ni cloud yoki controller o'chib qolishi club faoliyatiga (mavjud, to'langan sessiyalarga)
ta'sir qilmasligi kerak, lekin **doimiy va qasddan oflayn qolish** orqali obunasiz foydalanish ham
imkonsiz bo'lishi kerak.

Bu hujjat — backend jamoasiga beriladigan talablar ro'yxati. Format/nomlash mavjud
Billing/Core kontrakt v1.2 (`docs/_Clubpay - Billing core agent contract.docx`) uslubiga mos
yozilgan (`type/name/command_id/event_id/payload` zarfi, `external_pc_id`/`club_id` nomlash).

## 1. Asosiy g'oya — imzolangan, muddatli, hardware'ga bog'langan litsenziya

Cloud (Billing) **litsenziya obyektini Ed25519 bilan imzolab** beradi. Agent, Controller va Admin
PC — barchasi shu obyektni **faqat cloud public key bilan lokal tekshiradi** (network kerak emas).
Hech bir daraja o'zidan "litsenziya bor" deb qaror qabul qilmaydi — faqat cloud imzosini tekshiradi.

```json
{
  "club_id": "club12",
  "external_pc_id": "club12-pc07",
  "hardware_fingerprint": "sha256:...",
  "subscription_status": "ACTIVE",
  "issued_at": "2026-07-21T10:00:00Z",
  "valid_until": "2026-08-04T10:00:00Z",
  "signature": "ed25519:..."
}
```

- `valid_until` — muddat (taklif: 14 kun, har muvaffaqiyatli cloud ulanishda yangilanadi/uzayadi).
  Qisqa/o'rta uzilishlarda (soatlab-kunlab) faoliyat **to'xtamaydi**. Uzoq/doimiy uzilishda muddat
  tugagach, T1/T2 ham yangi sessiya boshlashni cheklaydi.
- `hardware_fingerprint` — pastga qarang (§3). Boshqa kompyuterga ko'chirilgan litsenziya/token
  darhol mos kelmay qoladi.
- Private key **faqat cloud'da** (HSM yoki shunga yaqin xavfsiz saqlash), hech qachon
  Controller/Admin/Agent'ga chiqmaydi. Public key agent/controller/admin build vaqtida
  o'rnatiladi (yoki bootstrap orqali olinadi, lekin key rotation uchun mexanizm kerak — §6).

## 2. Backend saqlashi kerak bo'lgan ma'lumotlar

Muhim: shartnoma/to'lov — **klub darajasida** (bitta klub = bitta shartnoma, N ta PC uchun), lekin
litsenziya va hardware-lock — **har bir alohida PC darajasida**. 20 ta PC'li klub uchun
`subscriptions`da 20 ta qator bo'ladi (har birida o'z `external_pc_id`+`hardware_fingerprint`+
`status`i), bitta `contract_id` bilan birlashtirilgan. Bitta PC'ning uskunasi buzilsa/almashtirilsa
yoki obunasi to'xtatilsa — bu xuddi shu klubning boshqa PC'lariga ta'sir qilmasligi kerak.

| Jadval/obyekt | Maydonlar |
|---|---|
| `club_contracts` | `contract_id`, `club_id`, `plan`, `pc_count`, `billing_status`, `contract_months`, `paid_until` — klub uchun bitta shartnoma, uning barcha PC'larini birlashtiradi |
| `subscriptions` | `contract_id`, `club_id`, `external_pc_id`, `price`, `status` (ACTIVE/GRACE/SUSPENDED/CANCELLED) — har bir PC uchun alohida qator |
| `hardware_bindings` | `external_pc_id`, `hardware_fingerprint`, `registered_at`, `last_seen_fingerprint`, `last_seen_at` |
| `license_issuance_log` | `external_pc_id`, `issued_at`, `valid_until`, `issuer_key_version` — audit uchun |
| `activation_requests` | birinchi marta o'rnatishda: `club_id`, `external_pc_id`, `hardware_fingerprint`, `requested_at`, `status` (PENDING/APPROVED/REJECTED) — admin tomonidan tasdiqlanadi (pastga qarang §4) |

Mavjud klubga yangi PC qo'shilishi = xuddi shu `contract_id` ostida yangi `subscriptions` qatori
(+ `club_contracts.pc_count`/summaning oshishi), yangi alohida shartnoma emas.

## 3. Hardware fingerprint — talab

Agent tomonidan hisoblanadi (backend buni faqat qabul qiladi/saqlaydi), lekin backend **formatini
biladi va tekshiradi**: bir nechta stabil identifikator hash'i (motherboard serial + disk serial +
CPU ID + asosiy NIC MAC), **faqat C: disk serial emas** (reformatda o'zgaradi, spoof qilish oson).

Backend talab qilinadigan xatti-harakat:
- Bitta `external_pc_id` uchun **bitta faol** `hardware_fingerprint` bo'ladi.
- Agar bir xil `external_pc_id`/`agent_token` bilan **boshqa fingerprint** ulanishga urinsa →
  ulanish/litsenziya **rad etiladi** + admin panelga alert ("bu PC boshqa uskunadan ulanmoqchi
  bo'ldi — klonlash/ko'chirish shubhasi").
- Uskuna qonuniy almashtirilganda (masalan disk almashtirildi) — **qo'lda admin tasdiqlash**
  orqali qayta bog'lash kerak (avtomatik emas — bu xavfsizlik nazorat nuqtasi).

## 4. Aktivatsiya oqimi (birinchi o'rnatish)

1. Agent birinchi ishga tushishda `hardware_fingerprint` hisoblaydi, `activation_requests`ga
   so'rov yuboradi (`club_id`/`external_pc_id`/`fingerprint`, hali `agent_token` yo'q yoki
   provisioning-token bilan).
2. Backend/admin (ClubPay operator, klub emas) so'rovni tasdiqlaydi — yangi klubga o'rnatish
   uchun shartnoma/obuna bosqichi shu yerda tekshiriladi.
3. Tasdiqlangach: `agent_token` (bearer, WS autentifikatsiya uchun, mavjud kontraktdagidek) +
   birinchi imzolangan litsenziya beriladi.
4. Shu paytdan boshlab litsenziya §1dagi tsikl bo'yicha yangilanib turadi.

## 5. WS kontraktga qo'shimchalar (mavjud v1.2 zarfiga mos)

- **Yangi/kengaytirilgan komanda** `apply_license` (Billing→Core, mavjud ixtiyoriy
  `apply_config`ga o'xshash): payload = §1dagi litsenziya obyekti. Agent buni lokal saqlaydi,
  keyingi tekshiruvlarda ishlatadi.
- **`heartbeat` event payload'iga qo'shimcha maydonlar** (Core→Billing): `hardware_fingerprint`,
  `license_valid_until`, `license_status` (`VALID`/`GRACE`/`EXPIRED`) — backend'ga fingerprint
  anomaliyasini kuzatish va muddat monitoring imkonini beradi.
- **Yangi error_code**: `license_expired`, `hardware_mismatch` — `start_session`/`extend_session`
  komandalari shu sabab bilan rad etilishi mumkin bo'lishi kerak.

## 6. Key rotation va xavfsizlik

- Cloud imzolash kaliti versiyalangan bo'lishi kerak (`issuer_key_version`) — kompromatatsiya
  yoki rejalashtirilgan almashtirish holida eski litsenziyalarni bekor qilib, yangi kalit bilan
  qayta chiqarish imkoni bo'lishi uchun.
- Public key agent/controller/admin'da **hardcoded emas, config orqali yangilanadigan** bo'lishi
  ma'qul (lekin yangilanish o'zi ham imzolangan bo'lishi kerak — aks holda bu yerdan ham soxta
  kalit tarqatish mumkin bo'lib qoladi).

## 7. Ochiq savollar (backend bilan kelishilishi kerak)

- Grace-period aniq muddati (taklif: 14 kun) — biznes/moliyaviy tomon bilan tasdiqlanishi kerak.
- Muddat tugagach aniq nima cheklanadi: faqat yangi sessiya boshlash bloklanadimi, yoki mavjud
  Frozen/Locked holatlarda ham cheklov ko'rsatiladimi? (Taklif: faqat yangi sessiya bloklanadi,
  mavjud oqilona ishlayotgan sessiya davom etadi — CLAUDE.md'dagi "foydalanuvchiga texnik xabar
  ko'rsatilmaydi" tamoyiliga mos, lekin admin ekranida ochiq ogohlantirish bo'ladi.)
- Fingerprint mos kelmaganda avtomatik SUSPENDED qilinadimi, yoki faqat alert + qo'lda ko'rib
  chiqishmi? (Noto'g'ri positive'lar xavfi bor — masalan disk almashtirilgandan keyin.)
- Public key distribution/rotation mexanizmi (bootstrap endpoint orqalimi, build-time'da
  o'rnatilganmi) — hali qaror qilinmagan.
