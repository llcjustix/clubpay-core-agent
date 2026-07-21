# Лицензия / подписка / привязка к оборудованию — задачи для backend

Цель: каждый установленный Agent работает только для своего клуба/ПК и только при активной
подписке; скопировать токен/конфиг на другой компьютер и запустить там — невозможно. Ниже —
только то, что должен реализовать backend.

## 1. Криптография (подпись лицензии)

- Сгенерировать пару ключей Ed25519. Приватный ключ хранится только на backend (HSM или
  аналогичное защищённое хранилище), никогда никуда не передаётся.
- Реализовать сервис, который подписывает объект лицензии этим ключом:

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

- `valid_until`: срок — 14 дней, продлевается автоматически при каждом успешном подключении
  agent к cloud (если подписка активна).
- Ключ должен быть версионирован (`issuer_key_version`) — чтобы можно было отозвать старые
  лицензии и переиздать новым ключом при компрометации/плановой замене.
- Публичный ключ backend должен отдавать agent/controller/admin (через конфиг/bootstrap), с
  возможностью ротации.

## 2. Модель данных

Важно: договор/оплата — на уровне клуба (один клуб = один договор на N ПК), но лицензия и
привязка к оборудованию — **на уровне каждого отдельного ПК**. Один клуб с 20 ПК даёт 20 строк
в `subscriptions` (каждая — свой `external_pc_id` + свой `hardware_fingerprint` + свой статус),
объединённых одним `contract_id`. Если оборудование одного ПК скомпрометировано/заменено или его
подписка приостановлена — это не должно затрагивать остальные ПК того же клуба.

| Таблица | Поля |
|---|---|
| `club_contracts` | `contract_id`, `club_id`, `plan`, `pc_count`, `billing_status`, `contract_months`, `paid_until` — один договор на клуб, объединяет все его ПК |
| `subscriptions` | `contract_id`, `club_id`, `external_pc_id`, `price`, `status` (ACTIVE/GRACE/SUSPENDED/CANCELLED) — одна строка на каждый ПК |
| `hardware_bindings` | `external_pc_id`, `hardware_fingerprint`, `registered_at`, `last_seen_fingerprint`, `last_seen_at` |
| `license_issuance_log` | `external_pc_id`, `issued_at`, `valid_until`, `issuer_key_version` (аудит) |
| `activation_requests` | `club_id`, `external_pc_id`, `hardware_fingerprint`, `requested_at`, `status` (PENDING/APPROVED/REJECTED) |

Добавление нового ПК в существующий клуб = новая строка `subscriptions` под тем же `contract_id`
(+ рост `pc_count`/суммы в `club_contracts`), а не новый договор.

## 3. Проверка hardware_fingerprint (принимает, не вычисляет)

Agent сам вычисляет и присылает `hardware_fingerprint` (хэш из серийника материнской платы +
серийника диска + CPU ID + MAC) — backend его только принимает и сверяет:

- На один `external_pc_id` может быть привязан только **один** активный `hardware_fingerprint`.
- Если приходит запрос/heartbeat с тем же `external_pc_id`/`agent_token`, но **другим**
  `hardware_fingerprint` → отклонить (`error_code: hardware_mismatch`) и создать алерт для
  ручной проверки администратором ClubPay.
- Замена оборудования на новое (легитимная) — отдельный API-эндпоинт для администратора,
  который вручную одобряет перепривязку `external_pc_id` → новый `hardware_fingerprint`.
  Автоматической перепривязки быть не должно.

## 4. Активация нового Agent (первая установка)

1. Endpoint для приёма запроса на активацию: `club_id`, `external_pc_id`, `hardware_fingerprint`.
   Записывается в `activation_requests` со статусом `PENDING`.
2. Endpoint/механизм для администратора ClubPay — одобрить или отклонить запрос (здесь же
   проверяется, что с клубом заключён договор/оплачена подписка).
3. После одобрения — выдать `agent_token` (bearer, для WS-аутентификации, как в текущем
   контракте v1.2) и первую подписанную лицензию (см. §1).

## 5. Расширение существующего WS-контракта (v1.2)

- Новая команда `apply_license` (Billing → Core, по аналогии с уже существующей опциональной
  `apply_config`): payload — объект лицензии из §1. Отправляется при выдаче/продлении.
- В payload события `heartbeat` (Core → Billing) добавить поля: `hardware_fingerprint`,
  `license_valid_until`, `license_status` (`VALID`/`GRACE`/`EXPIRED`). Нужно для мониторинга
  сроков и обнаружения аномалий fingerprint на backend.
- Новые `error_code`: `license_expired`, `hardware_mismatch` — backend должен уметь возвращать
  их как причину отказа в `command_result` для `start_session`/`extend_session`.

## 6. Логика подписки

- Лицензию (§1) выдавать/продлевать только если `subscriptions.status == ACTIVE`.
- Если подписка `SUSPENDED`/`CANCELLED` — новая лицензия не выдаётся, действующая доходит до
  своего `valid_until` и не продлевается (агент сам ограничит запуск новых сессий после этого).
- Нужен внутренний endpoint/механизм, которым ClubPay-оператор может вручную отозвать
  (`SUSPENDED`) лицензию конкретного `external_pc_id` (например, при неоплате или обнаружении
  клонирования).

## 7. Вопросы, которые нужно закрыть до начала разработки

- Точный срок `valid_until` (предложение — 14 дней) — подтвердить с бизнес-стороной.
- Автоматический `SUSPENDED` при несовпадении fingerprint, или только алерт + ручное решение
  администратора? (Риск ложных срабатываний при легитимной замене оборудования.)
- Способ распространения и ротации публичного ключа agent/controller/admin — через bootstrap
  endpoint или зашивать при сборке.
