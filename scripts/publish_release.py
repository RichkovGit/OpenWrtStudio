import os
import sys
import json
import urllib.request
import urllib.error

TOKEN = os.environ.get("GITHUB_TOKEN", "")
if not TOKEN and len(sys.argv) > 1:
    TOKEN = sys.argv[1]
REPO_OWNER = "RichkovGit"
REPO_NAME = "OpenWrtStudio"
TAG = "v2.5.12"
RELEASE_NAME = "OpenWrt Studio v2.5.12 — Расширенный Ping через туннели и интерфейсы, мгновенный Стоп, MT7915 Wi-Fi и AmneziaWG"
RELEASE_BODY = """### 🎉 Что нового в обновлении OpenWrt Studio v2.5.12:

Благодарим пользователей форума 4PDA за подробные баг-репорты, тестирование и обратную связь! В этом релизе мы улучшили сетевую диагностику и закрыли оставшиеся нюансы:

#### 🌐 1. Расширенная сетевая диагностика (Ping и Traceroute)
- **Выбор исходящего интерфейса (-I)**: Теперь можно направлять ICMP-трафик через любой сетевой интерфейс или VPN-туннель роутера (`br-lan`, `wan`, `AWG`, `AWG_Warp`, `pppoe-wan` и др.). Список интерфейсов подгружается автоматически прямо из ядра роутера.
- **Гибкое количество пакетов**: Добавлен выбор пресетов количества пингов (5, 10, 20, 50, 100) или непрерывный режим (**Бесконечно ∞**), а также возможность ввести произвольное число.
- **Размер пакета (-s)**: Поддержка указания размера payload пакета (полезно для проверки MTU и фрагментации туннелей AmneziaWG/WireGuard).
- **Мгновенная остановка (Стоп)**: Кнопка «Стоп» теперь срабатывает мгновенно — SSH-канал прерывается без зависаний, а на роутер немедленно отправляется сигнал остановки `killall -INT ping / traceroute`.

#### 📶 2. Поддержка Wi-Fi клиентов на чипах MediaTek (MT7915 / MT7981 / MT7986)
- **Динамический опрос всех радиоинтерфейсов**: Опрос через `ubus hostapd.*`, `iw dev` и `iwinfo` с корректной нормализацией вывода BusyBox ash.
- **Сохранение имён устройств**: При отключении или сбросе сессии база DNS-аренд больше не затирается, имена устройств сохраняются и не превращаются в маску MAC-адреса.
- **Точное определение диапазона 2.4 / 5 GHz**: Клиенты корректно сортируются по радиодиапазонам и частотам.
- **Фильтрация WAN Gateway**: Внешний шлюз провайдера исключён из списка LAN-клиентов.
- **Разделение Онлайн и Офлайн (Резерв IP)**: Неактивные DHCP-аренды и статические записи отображаются со статусом «💤 Офлайн / Резерв IP».

#### ⚡ 3. Мульти-интерфейсная поддержка AmneziaWG (AWG) и WireGuard
- Автоматическое обнаружение всех туннелей (`AWG`, `AWG_Warp` и др.) с удобным селектором переключения, мониторингом Handshake и счетчиками Rx / Tx.

#### 📋 4. Исправление таблицы «Статические DHCP-аренды»
- Корректный парсинг анонимных секций `@host[X]` в UCI и удобные заголовки колонок.

#### 📦 Файлы релиза:
- `OpenWrtStudio_Setup_v2.5.12.exe` — Инсталлятор Windows (.NET 8 WPF, Fluent Design)
- `OpenWrtStudio_v2.5.12_Portable.zip` — Портативная версия без установки
- `SHA256SUMS.txt` — Контрольные суммы файлов
"""

def request_json(url, method="GET", data=None):
    headers = {
        "Authorization": f"token {TOKEN}",
        "User-Agent": "OpenWrtStudio-ReleaseBot",
        "Accept": "application/vnd.github.v3+json"
    }
    encoded_data = json.dumps(data).encode("utf-8") if data else None
    req = urllib.request.Request(url, headers=headers, data=encoded_data, method=method)
    if data:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="ignore")
        if e.code == 404:
            return None
        print(f"HTTP {e.code}: {body}")
        raise

def upload_asset(upload_url, file_path, content_type="application/octet-stream"):
    base_name = os.path.basename(file_path)
    url = upload_url.split("{")[0] + f"?name={base_name}"
    size = os.path.getsize(file_path)
    print(f"Загрузка {base_name} ({size / 1024 / 1024:.2f} MB)...")
    
    with open(file_path, "rb") as f:
        file_data = f.read()

    req = urllib.request.Request(url, data=file_data, method="POST")
    req.add_header("Authorization", f"token {TOKEN}")
    req.add_header("User-Agent", "OpenWrtStudio-ReleaseBot")
    req.add_header("Content-Type", content_type)
    req.add_header("Content-Length", str(len(file_data)))

    try:
        with urllib.request.urlopen(req) as resp:
            res = json.loads(resp.read().decode("utf-8"))
            print(f"[OK] {base_name} успешно загружен! (ID: {res.get('id')})")
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="ignore")
        print(f"[ERR] Ошибка загрузки {base_name} ({e.code}): {body}")

def main():
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    installer_path = os.path.join(base_dir, "release_artifacts", "OpenWrtStudio_Setup_v2.5.12.exe")
    zip_path = os.path.join(base_dir, "release_artifacts", "OpenWrtStudio_v2.5.12_Portable.zip")
    sha_path = os.path.join(base_dir, "release_artifacts", "SHA256SUMS.txt")

    print(f"Проверка релиза {TAG} на GitHub...")
    rel = request_json(f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/tags/{TAG}")
    
    if rel:
        print(f"Релиз {TAG} уже существует (ID: {rel['id']}). Удаление старых ассетов...")
        for asset in rel.get("assets", []):
            try:
                del_req = urllib.request.Request(
                    f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/assets/{asset['id']}",
                    headers={"Authorization": f"token {TOKEN}", "User-Agent": "OpenWrtStudio-ReleaseBot"},
                    method="DELETE"
                )
                with urllib.request.urlopen(del_req) as _:
                    print(f"Удален старый ассет: {asset['name']}")
            except Exception as ex:
                print(f"Не удалось удалить ассет: {ex}")
    else:
        print(f"Создание нового релиза {TAG}...")
        rel = request_json(
            f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases",
            method="POST",
            data={
                "tag_name": TAG,
                "target_commitish": "main",
                "name": RELEASE_NAME,
                "body": RELEASE_BODY,
                "draft": False,
                "prerelease": False
            }
        )
        print(f"[OK] Релиз создан: {rel['html_url']}")

    upload_url = rel["upload_url"]

    if os.path.exists(installer_path):
        upload_asset(upload_url, installer_path, "application/vnd.microsoft.portable-executable")
    else:
        print(f"Файл не найден: {installer_path}")

    if os.path.exists(zip_path):
        upload_asset(upload_url, zip_path, "application/zip")
    else:
        print(f"Файл не найден: {zip_path}")

    if os.path.exists(sha_path):
        upload_asset(upload_url, sha_path, "text/plain")
    else:
        print(f"Файл не найден: {sha_path}")

    print("\nВсе файлы успешно опубликованы на GitHub!")
    print(f"Ссылка на релиз: {rel['html_url']}")

if __name__ == "__main__":
    main()
