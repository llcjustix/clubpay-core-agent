> **Развёртывание dev-стенда: Mac + Docker + VirtualBox (агент)** · Версия 1.0 · июнь 2026
> Спутник к `club-system-spec.md`, `billing-core-agent-contract.md`, `e2e-test-plan.md`.
> Цель: поднять весь стек на одном маке и прогнать flow «клиент → оплата → разблокировка ПК → …».
> Код компонентов дают разработчики; здесь — **инструкция по развёртыванию и связке**.

---

## 0. Важные оговорки (прочитать первым)

1. **`network_mode: host` на macOS ≠ Linux.** Docker Desktop на маке работает внутри скрытой Linux-VM,
   поэтому host-режим **не даёт контейнеру доступ к реальной сети мака**. Broadcast (WoL magic-пакет) из
   контейнера до VirtualBox-VM не дойдёт. → Для связи используем **опубликованные порты + IP мака**, а
   для пробуждения — **wol-proxy на хосте** (§5).
2. **Реальный WoL между Docker и VirtualBox на маке ненадёжен** (выключенная VBox-VM не просыпается от
   magic-пакета). На стенде «пробуждение» делаем через host-proxy (`VBoxManage`); **настоящий WoL
   валидируем на физических Windows-ПК** перед пилотом (см. ТЗ, WoL).
3. **VirtualBox + Windows 11 не работает на Apple Silicon** (M1/M2/M3). Если мак на Apple Silicon:
   - Intel-мак — идёт по этой инструкции;
   - иначе — **UTM** (x86-эмуляция, медленно) / **Parallels**, либо **физический Windows-ПК в той же
     LAN** (он же даст реальный WoL — предпочтительно).

---

## 1. Что где крутится

```
┌───────────────────────── Mac (хост) ─────────────────────────┐
│  Docker Desktop:                                             │
│    postgres · server(наш облак) · controller · web(сайт+PWA) │
│    · telegram-bot                                            │
│  На хосте (не в Docker): wol-proxy                           │
│                                                              │
│  VirtualBox: Windows 11 VM  →  Agent (C#/.NET)               │
└──────────────────────────────────────────────────────────────┘
        │ published ports (:8080 server, :8081 controller, :3000 web)
        │ агент ↔ контроллер: агент открывает исходящее к IP мака
        │ wake: controller → wol-proxy(host) → VBoxManage/magic → VM
   Внешнее: Payme/Click sandbox · Telegram · (Soliq — позже)
```

Ключевой принцип из ТЗ (раздел 9): **агент сам открывает исходящее соединение к контроллеру**, команды
идут обратно по нему. Значит контроллеру НЕ нужно «входить» в агента — кроме пробуждения (WoL).

---

## 2. Предварительные требования

- macOS (**Intel** для VirtualBox; Apple Silicon — см. §0.3).
- **Docker Desktop** (последняя версия).
- **VirtualBox** (последняя версия) + образ **Windows 11**.
- **Homebrew** (для утилит): `brew install wakeonlan cloudflared` (или `ngrok`).
- Доступ в интернет (Payme/Click sandbox, Telegram).
- От разработчиков: образы/исходники компонентов (server, controller, web, bot, agent) и их README по сборке.

---

## 3. Docker: backend через docker-compose

Разработчики дают код; ниже — **скелет `docker-compose.yml`** (подставить их `build:`/`image:`).
На macOS host-режим не используем — публикуем порты.

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: clubpay
      POSTGRES_USER: clubpay
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]

  server:                      # наш облачный бэкенд
    build: ./server            # или image: clubpay/server:dev
    environment:
      DATABASE_URL: postgres://clubpay:${DB_PASSWORD}@postgres:5432/clubpay
      CLICK_SERVICE_ID: ${CLICK_SERVICE_ID}
      CLICK_MERCHANT_ID: ${CLICK_MERCHANT_ID}
      CLICK_SECRET_KEY: ${CLICK_SECRET_KEY}
      PAYME_MERCHANT_ID: ${PAYME_MERCHANT_ID}
      PAYME_KEY: ${PAYME_KEY}
      TELEGRAM_BOT_TOKEN: ${TELEGRAM_BOT_TOKEN}
    ports: ["8080:8080"]
    depends_on: [postgres]

  controller:                  # контроллер клуба (на стенде — контейнер)
    build: ./controller
    environment:
      SERVER_URL: http://server:8080
      WOL_PROXY_URL: http://host.docker.internal:9000   # см. §5
      CLICK_SECRET_KEY: ${CLICK_SECRET_KEY}             # для фоллбэк-опроса (вариант A, ТЗ 9/20)
    ports: ["8081:8081"]
    depends_on: [server]

  web:                         # одностраничный сайт + PWA менеджера
    build: ./web
    environment:
      API_BASE: http://localhost:8080
    ports: ["3000:3000"]

  bot:                         # Telegram-бот (long-polling, §6)
    build: ./bot
    environment:
      TELEGRAM_BOT_TOKEN: ${TELEGRAM_BOT_TOKEN}
      SERVER_URL: http://server:8080
    depends_on: [server]

volumes:
  pgdata:
```

`.env` (рядом с compose):
```
DB_PASSWORD=devpass
CLICK_SERVICE_ID=...      CLICK_MERCHANT_ID=...   CLICK_SECRET_KEY=...
PAYME_MERCHANT_ID=...     PAYME_KEY=...
TELEGRAM_BOT_TOKEN=...
MAC_LAN_IP=192.168.1.50   # реальный IP мака в LAN (для агента), узнать: ipconfig getifaddr en0
```

Запуск: `docker compose up -d --build` · логи: `docker compose logs -f server`.
Проверка: открыть `http://localhost:3000` (сайт), `http://localhost:8080/health` (сервер).

> Примечание: `host.docker.internal` из контейнера указывает на хост-мак — так контроллер зовёт wol-proxy.

---

## 4. VirtualBox: Windows 11 + агент

1. Создать VM: Windows 11, 4+ ГБ RAM, 2+ CPU.
2. **Сеть VM — Bridged Adapter** (мост к физическому интерфейсу мака) → VM получит IP в той же LAN, что и
   мак. (Проверить: в VM `ipconfig` → адрес вида `192.168.1.x`.)
3. Установить Windows 11, затем **агент** (сборка от разработчиков).
4. Настроить агента: адрес контроллера = `http://<MAC_LAN_IP>:8081` (агент открывает исходящее сам).
5. **Подготовить Windows к WoL** (для последующего переезда на физические ПК — на VBox работает частично):
   - Отключить **Fast Startup** (Панель управления → Электропитание → действия кнопок → снять «Быстрый запуск»).
   - В свойствах сетевого адаптера → «Управление электропитанием» → включить «Разрешить будить», «Только magic packet».
   - Записать **MAC-адрес** сетевого адаптера VM (нужен для magic-пакета).

---

## 5. Пробуждение (WoL) на стенде — через wol-proxy на хосте

Почему: из Docker-контейнера на маке magic-пакет до VM не дойдёт (§0), а выключенная VBox-VM и так не
просыпается от WoL. Поэтому запускаем **маленький wol-proxy на самом маке**, который по запросу
контроллера будит VM.

Мини-proxy (пример на хосте, слушает `:9000`, будит по `external_pc_id` → MAC/VM-имя):
```bash
# wol-proxy.sh (запускать на маке, не в Docker)
# принимает POST {"pc":"club12-pc07"} и либо шлёт magic-пакет, либо резюмит VBox-VM
# вариант 1 (реальный magic-пакет в LAN, если VM спит на уровне Windows):
#   wakeonlan <MAC_VM>
# вариант 2 (надёжно для стенда — резюм из saved state):
#   VBoxManage controlvm "Win11-Agent" resume   # или: VBoxManage startvm "Win11-Agent" --type headless
```
- Для стенда рекомендуется **вариант 2** (`VBoxManage`) — детерминированно.
- Контроллер в момент команды `wake` (контракт §4.5) шлёт запрос на `WOL_PROXY_URL`
  (`http://host.docker.internal:9000`), proxy будит нужную VM. С точки зрения флоу всё честно:
  «оплата → команда пробуждения → ПК ожил → агент показал экран».
- «Сон» на стенде = `VBoxManage controlvm "Win11-Agent" savestate` (имитация S3).

> Реальный WoL (BIOS/UEFI + NIC + S3/S5) на VBox-маке не воспроизвести — это проверяем на 2–3 физических
> Windows-ПК перед пилотом (E2E-стенд, ТЗ). На стенде мака мы валидируем **логику флоу**, не железо.

---

## 6. Telegram-бот — long-polling (без публичного URL)

- Создать бота у **@BotFather**, получить токен → в `.env` (`TELEGRAM_BOT_TOKEN`).
- Бот работает в режиме **long-polling (`getUpdates`)** — это исходящие запросы к Telegram, публичный URL
  **не нужен**, всё работает за NAT. (Webhook-режим не используем на стенде.)
- Проверка: отправить боту `/start`, проверить привязку `chat_id` к номеру и доставку ваучера (E2E-07/08).

---

## 7. Платёжный sandbox (Payme/Click)

- Креды sandbox → в `.env`.
- **Callback vs polling:**
  - Чтобы протестировать **callback-путь** (норма), серверу нужен публичный URL. Поднять туннель:
    `cloudflared tunnel --url http://localhost:8080` (или `ngrok http 8080`) → полученный HTTPS-URL
    указать как callback в sandbox Payme/Click.
  - **Polling-путь** (сценарий «сервер лёг», ТЗ 9) публичного URL **не требует** — контроллер сам
    опрашивает статус. Так тестируем E2E-15/16 без туннеля.
- Тестовые карты — из sandbox-доков провайдера.

---

## 8. Порядок запуска и проверка

1. `docker compose up -d --build` → дождаться `server/health` OK.
2. Запустить **wol-proxy** на маке (`./wol-proxy.sh`).
3. Поднять VM Windows 11, дождаться, пока агент подключится к контроллеру (лог контроллера: агент онлайн).
4. (Опц.) поднять туннель для callback-теста.
5. **Smoke-тест:** открыть сайт → выбрать пакет → оплатить sandbox-картой → убедиться, что контроллер
   получил успех (callback или polling) → команда `wake`/`unlock` → VM ожила → агент показал экран/таймер.
6. Далее — прогон **`e2e-test-plan.md`**: начать с A/B/C, затем E (падение cloud: `docker compose stop
   server` → проверить polling-путь; восстановление → sync).

---

## 9. Частые проблемы

- **Контейнеры не видят LAN / агент не достучался:** не используйте `network_mode: host` на маке;
  агент ходит на `http://<MAC_LAN_IP>:8081`, узнать IP: `ipconfig getifaddr en0`.
- **VM не в той сети:** адаптер VM должен быть **Bridged**, не NAT/Host-only.
- **VM «не будится»:** это ожидаемо для VBox — используйте wol-proxy вариант 2 (`VBoxManage`).
- **Callback от Payme/Click не приходит:** нужен туннель (cloudflared/ngrok); либо тестируйте polling-путь.
- **Apple Silicon:** VirtualBox не потянет x86-Win11 → UTM/Parallels или физический Windows-ПК в LAN.
- **ARM-образы:** на Apple Silicon для сторонних образов при необходимости добавить `platform: linux/amd64`.
- **`host.docker.internal` не резолвится:** это имя доступно в Docker Desktop; для контроллера в контейнере
  оно указывает на мак-хост (где слушает wol-proxy).

---

## 10. Что этот стенд покрывает и что нет

- **Покрывает:** весь программный flow — оплата (callback и polling), сессии, продление, ваучеры +
  Telegram, панель менеджера, отказоустойчивость (гасим `server`/`controller` контейнеры → T1/T2),
  восстановление и синк, идемпотентность.
- **Не покрывает:** реальное железо WoL/сон (BIOS/NIC/драйвер), которое проверяется на физических
  Windows-ПК + Pi + свитч **до пилота**.
