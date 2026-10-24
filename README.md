<div align="center">

# 🌐 OpenWrt Studio (WrtPilot)

**Современный центр управления, глубокой настройки, мониторинга и автоматического устранения неполадок для роутеров OpenWrt.**

[![Release](https://img.shields.io/badge/release-v2.5.21-blue.svg?style=flat-square)](https://github.com/RichkovGit/OpenWrtStudio/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20x64-0078D4.svg?style=flat-square)](https://github.com/RichkovGit/OpenWrtStudio/releases)
[![Framework](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![OpenWrt](https://img.shields.io/badge/OpenWrt-SSH-2596be.svg?style=flat-square)](https://openwrt.org/)
[![License](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](LICENSE)

[📥 Установщик и Portable](#-загрузка-и-установка) • [📝 История изменений](RELEASE_NOTES.md) • [🚀 Возможности](#-ключевые-возможности) • [🛠️ Сборка из исходников](#-сборка-из-исходников)

</div>

## Скачать Windows-программу

- [Установщик EXE — Windows x64](https://github.com/RichkovGit/OpenWrtStudio/releases/download/v2.5.21/OpenWrtStudio_Setup_v2.5.21.exe).
- [Portable ZIP — Windows x64](https://github.com/RichkovGit/OpenWrtStudio/releases/download/v2.5.21/OpenWrtStudio_v2.5.21_Portable.zip).
- [Все файлы последнего выпуска (Assets)](https://github.com/RichkovGit/OpenWrtStudio/releases/latest).

На GitHub откройте **Releases → Assets**; EXE находится в Windows-репозитории. APK и Linux-пакеты находятся в отдельном [репозитории приложения](https://github.com/RichkovGit/OpenWrtStudio-Mobile/releases/latest).

---

## Исправление VPN в 2.5.20

Удалены заранее заданные авто-группы, демонстрационные данные и случайные задержки. Имена и raw tag читаются из реальных кэшей ForkOP и Clash API. Активная политика секции отделена от вложенного пути маршрутизации; переключение подтверждается повторным чтением состояния. Сценарии проверяются общим с Android набором данных, основанным на схеме upstream ForkOP.

Опубликованные установщик и Portable доступны в [Windows Releases](https://github.com/RichkovGit/OpenWrtStudio/releases/latest). Linux x86_64 (AppImage и tar.gz) собирается из Flutter-приложения: [инструкция](https://github.com/RichkovGit/OpenWrtStudio-Mobile/blob/main/LINUX_INSTALL.md). WPF-интерфейс Windows под Linux не переносится.

## 📖 О проекте

**OpenWrt Studio** — это автономное настольное Windows-приложение, созданное на стыке высоких технологий и максимального удобства («всё для людей»). Программа заменяет десятки разрозненных веб-страниц LuCI и терминальных команд единым, быстрым и красивым Fluent Design интерфейсом.

Приложение позволяет настраивать Wi-Fi, интернет-соединения, правила брандмауэра, маршруты обхода блокировок, настраивать современные протоколы (AmneziaWG, Sing-box, Mihomo, WireGuard, Tailscale), внедрять подписки и автоматически восстанавливать доступ в интернет при сбоях.

Доступность отдельных функций зависит от прошивки, архитектуры и установленных на роутере пакетов. Для работы нужны доступ по SSH и права на соответствующие настройки.

---


## 📸 Скриншоты десктопного интерфейса

![Обзор сети OpenWrt Studio v2.5.19](screenshots/dashboard-v2.5.19.png)

В v2.5.19 добавлены стартовый экран подключения, время последнего снимка и сохранение текстового отчёта без профилей, IP/MAC-адресов, паролей и ключей. Горячие клавиши: `Ctrl+1` — обзор, `Ctrl+2` — клиенты, `Ctrl+3` — диагностика, `Ctrl+,` — настройки. Полный список исправлений — в [заметках о выпуске](RELEASE_NOTES.md).

---

## 🚀 Ключевые возможности

### 1. 🔔 Фоновый мониторинг и Уведомления Windows
* **Фоновое слежение**: Программа непрерывно опрашивает роутер в фоновом режиме (каждые 25 секунд).
* **Нативные уведомления (Toast Notifications)**:
  * ⚠️ Оповещение при обрыве SSH/связи с роутером и автоматическое уведомление при её восстановлении.
  * 🚨 Предупреждения при критической перегрузке памяти (RAM > 92%) или заполнении диска (`/overlay` > 92%).
  * 🩺 Оповещения сторожевого пса (сбои DNS, обрыв PPPoE, потеря несущей на порту WAN).
* **Системный трей Windows (System Tray)**:
  * Отображение живого статуса роутера в тултипе трея (`В сети: RAM 28%`).
  * Меню быстрого доступа: *Открыть Studio*, *Перезапустить сеть*, *Очистить кэш DNS*, *Выход*.
  * При закрытии окна крестиком «X» приложение сворачивается в трей и продолжает мониторить роутер.

### 2. 🌐 Конструктор Mihomo (Clash.Meta) + Внедрение подписок
* **Вкладка «Подписки»**: вставка ссылок на подписки в совместимом с Mihomo формате.
* **Инжекция в конфигуратор**: автоматическое создание блоков `proxy-providers` и привязка к группам прокси (`use: [имя-подписки]`).
* **Прямое обновление на роутере**: скачивание свежих узлов через `curl/wget` прямо в `/etc/mihomo/proxy_providers/` и перезагрузка службы без перезагрузки всей системы.
* **Готовые пресеты в 1 клик**:
  * ⚡ **«Умный обход (Антизапрет + RU напрямую)»**: сайты РФ и банки идут на полной скорости провайдера, а заблокированные ресурсы и YouTube/Discord направляются через прокси.
  * 🌐 **«Полный туннель»**: весь внешний трафик направляется через VPN.

### 3. ⚡ Репозиторий ForkOP и глубокая диагностика
* **Автоопределение архитектуры**: определение процессора роутера (`mips_24kc`, `aarch64_cortex-a53`, `x86_64`) и версии OpenWrt для генерации оптимизированных фидов ForkOP в `/etc/opkg/customfeeds.conf`.
* **Установка компонентов в 1 клик**: PassWall 2, SmartDNS, ChinaDNS-NG, MosDNS, Sing-box Core, Xray Core.
* **Глубокая диагностика**:
  * Анализ таблиц `nftables` (`table inet fw4`) и `iptables`.
  * Проверка перехвата DNS на порты анти-DPI резолверов (5335).
  * Тест разрешения российских и обходных доменов для выявления DNS-утечек (DNS Leak).

### 4. 🔍 Интерактивная «Карта возможностей» роутера
* Динамическое сканирование установленных пакетов (`opkg list-installed`), сетевых интерфейсов (`ip link`) и процессов (`pidof`).
* Интерактивная матрица статуса для 9 протоколов: **AmneziaWG, WireGuard, Sing-box, Mihomo, PassWall, OpenVPN, Tailscale, ZeroTier, Xray**.
* Статусы: `Активен и работает`, `Установлен, но выключен`, `Не установлен` (с кнопкой быстрой установки).

### 5. 🛡️ Конфигураторы VPN-протоколов
* **Amnezia WireGuard (AWG)**: импорт файлов `.conf` с поддержкой параметров обфускации:
  * Мусорные пакеты: **`Jc`**, **`Jmin`**, **`Jmax`**
  * Сдвиг заголовков рукопожатия: **`S1`**, **`S2`**
  * Магические заголовки: **`H1`**, **`H2`**, **`H3`**, **`H4`**
  * Генерация UCI для `/etc/config/network` и активация интерфейса `awg0`.
  * В v2.5.19 исправлены IPv6 endpoint, применение DNS и проверка имени интерфейса.
* **WireGuard**: классический конфигуратор ключей, эндпоинтов и маршрутов.
* **Sing-box**: конфигуратор VLESS Reality / Hysteria 2 / TUIC с генерацией `config.json` и TUN-режимом.
* **OpenVPN**: импорт `.ovpn` профилей и автонастройка клиента.
* **Tailscale & ZeroTier**: ввод Auth-ключа, включение режима Exit-Node и анонс подсетей (Subnet Router).

### 6. 🎛️ Все настройки роутера (Full Router Management)
* **Wi-Fi сети (2.4 GHz / 5 GHz)**: смена SSID, скрытие сети, смена пароля, переключение безопасности (`WPA2-PSK`, `WPA3-SAE`, `WPA2/WPA3 Mixed`), каналы, ширина полосы и кнопка `wifi reload`.
* **LAN и DHCP**: смените IP-адрес роутера, маску подсети, диапазон пула DHCP, а также добавляйте статические IP по MAC-адресам (Static Leases).
* **WAN (Интернет)**: настройка PPPoE (логин/пароль, MTU 1492), DHCP-клиента или Статического IP с указанием шлюза и DNS.
* **Брандмауэр (Проброс портов)**: добавление правил Port Forwarding (внешний порт -> внутренний IP:порт) для веб-серверов, игр и torrent-клиентов с мгновенным применением.
* **Система и администрирование**: смените Hostname, часовой пояс и пароль учётной записи `root`, а также скачайте архив настроек, созданный `sysupgrade -b`.

### 7. ⏰ Планировщик задач (Cron)
* **Шаблоны в 1 клик**:
  * 🌙 Ночная перезагрузка (04:00).
  * 📴 Отключение Wi-Fi на ночь (01:00-07:00).
  * 🔄 Автообновление подписок каждые 6 часов.
  * 🧹 Ночная очистка кэша RAM (`drop_caches`, 03:00).
  * 📡 Ping Watchdog (проверка доступности внешнего узла каждые 5 мин и рестарт сети при обрыве).
* **Конструктор произвольных задач**: ввод любого расписания, включение/отключение задач галочкой и кнопка **«Выполнить сейчас»**.

### 8. 🩺 Сторожевой пёс Sentinel и Авто-восстановление
* Автоматическая классификация источника сбоя сети:
  * **Сбой у провайдера**: обрыв физического кабеля или отказ сервера PPPoE провайдера.
  * **Сбой на роутере**: зависание локального DNS или сетевой службы.
* 5 методов автоматического решения проблем в один клик.

### 9. 🎨 Темы оформления LuCI и Каталог пакетов
* Установка тем интерфейса роутера: **Argon** (+ конфигуратор фонов и логотипов), **Edge**, **Material**, **OpenWrt 2020**. Совместимость зависит от версии LuCI; установка устаревшей темы Design блокируется.
* Полнотекстовый поиск по официальным репозиториям OpenWrt и сторонним источникам.

---

## 📥 Загрузка и установка

В [последнем стабильном выпуске](https://github.com/RichkovGit/OpenWrtStudio/releases/latest) используются автономные сборки для Windows 10/11 x64:

- **`OpenWrtStudio_Setup_v2.5.21.exe`** — установка для текущего пользователя в `%LOCALAPPDATA%\Programs\OpenWrt Studio`. Установщик предлагает ярлык рабочего стола и автозапуск в трее; оба пункта включаются по желанию.
- **`OpenWrtStudio_v2.5.21_Portable.zip`** — распаковка в выбранную папку и запуск `OpenWrtStudio.exe`.

Скачайте нужный файл и `SHA256SUMS.txt` из одного релиза, проверьте SHA-256 и завершите прежнюю копию приложения через меню трея «Выход» перед заменой. Закрытие окна крестиком оставляет приложение работающим в трее.

Отдельная установка .NET SDK или Runtime не требуется. Профили и настройки по умолчанию хранятся в `%APPDATA%\OpenWrtStudio`; установщик и его удаление не очищают эту папку. Скачанный через встроенную проверку обновлений ZIP нужно распаковать вручную.

---

## 🔒 Контрольные суммы (SHA-256)

Контрольные суммы относятся к конкретному файлу и опубликованы в `SHA256SUMS.txt` рядом с файлами выпуска. Получить SHA-256 в PowerShell:

```powershell
Get-FileHash -LiteralPath .\OpenWrtStudio_v2.5.21_Portable.zip -Algorithm SHA256
Get-FileHash -LiteralPath .\OpenWrtStudio_Setup_v2.5.21.exe -Algorithm SHA256
```

Сравните поле `Hash` со строкой соответствующего файла в `SHA256SUMS.txt`.

---

## 🛠️ Сборка из исходников

### Требования:
* ОС: Windows 10/11 x64
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Inno Setup 6](https://jrsoftware.org/isinfo.php) — для сборки установщика; Portable можно собрать без него.

### Инструкция по сборке:

1. **Клонирование репозитория**:
   ```bash
   git clone https://github.com/RichkovGit/OpenWrtStudio.git
   cd OpenWrtStudio
   ```

2. **Запуск автоматических тестов**:
   ```powershell
   dotnet test ./OpenWrtStudio.Tests/OpenWrtStudio.Tests.csproj -c Release
   ```

3. **Сборка автономного Single-File EXE**:
   ```powershell
   dotnet publish ./OpenWrtStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
   ```

4. **Создание Portable ZIP**:
   ```powershell
   Compress-Archive -Path ./publish/* -DestinationPath ./OpenWrtStudio_v2.5.21_Portable.zip
   ```

Команды выполняются из корня репозитория: `OpenWrtStudio.csproj` расположен в корне, проект тестов — в `OpenWrtStudio.Tests`.

5. **Сборка установщика существующим компилятором Inno Setup 6**:
   ```powershell
   ./scripts/build-installer.ps1
   ```
   Скрипт проверяет версию EXE, добавляет значок, лицензию и заметки о выпуске из репозитория и создаёт `installer_output/OpenWrtStudio_Setup_v2.5.21.exe`. Если компилятор находится вне стандартных папок, задайте `-CompilerPath 'C:\путь\ISCC.exe'`. Параметры `-PublishDirectory` и `-OutputDirectory` позволяют выбрать папки сборки. Скрипт не устанавливает инструменты автоматически. Источник установщика — [installer/setup.iss](installer/setup.iss).

Для v2.5.19 прошли **124 теста без подключения к роутеру**, ещё 5 интеграционных проверок в обычном запуске отключены. Отдельно прошли **2 проверки чтения реального роутера**. Это проверка выбранных сценариев, а не всех сочетаний прошивок и оборудования.

Workflow [Desktop build and tests](.github/workflows/desktop.yml) запускает тесты на `windows-latest` с .NET 8 и сохраняет Portable ZIP, установщик, SHA-256 и результаты тестов как артефакты Actions. Сборка установщика требует установленного Inno Setup 6 на runner. Данные доступа к роутеру и включение live-проверок для CI не нужны.

---


---

## 📱 Мобильная версия для Android

Доступно официальное мобильное приложение **[OpenWrt Studio Mobile](https://github.com/RichkovGit/OpenWrtStudio-Mobile)**:
- Управление клиентами, привязка статических IP, отключение интернета на устройствах в один тап.
- Мониторинг интерфейсов, ForkOP прокси, Sentinel Watchdog и быстрые команды.
- [Галерея скриншотов и загрузка APK](https://github.com/RichkovGit/OpenWrtStudio-Mobile).

---

## 💬 Сообщество и каналы
- 📢 **Telegram-канал программы**: [@OpenWrtStudio](https://t.me/OpenWrtStudio)
- 📢 **Telegram-канал автора**: [@RichkovChannel](https://t.me/RichkovChannel)
- 🐛 **Багрепорт и предложения**: [GitHub Issues](https://github.com/RichkovGit/OpenWrtStudio/issues)

## 📜 Лицензия

Проект распространяется под свободной лицензией [MIT](LICENSE).
OpenWrt является зарегистрированным товарным знаком OpenWrt project.
