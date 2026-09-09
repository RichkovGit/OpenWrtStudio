#!/usr/bin/env python3
"""
OpenWrt Studio - GitHub Deployer & Release Publisher
Publishes source code, tags, and binary release assets to https://github.com/RichkovGit/OpenWrtStudio
"""

import os
import sys
import json
import urllib.request
import urllib.error
import subprocess

REPO_OWNER = "RichkovGit"
REPO_NAME = "OpenWrtStudio"
REPO_URL = f"https://github.com/{REPO_OWNER}/{REPO_NAME}.git"
RELEASE_TAG = "v2.5.0"

def check_token(token: str) -> bool:
    req = urllib.request.Request("https://api.github.com/user", headers={
        "Authorization": f"token {token}",
        "User-Agent": "OpenWrtStudio-Deployer"
    })
    try:
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode())
            print(f"[OK] Авторизован как: {data.get('login')}")
            return True
    except urllib.error.HTTPError as e:
        print(f"[ERROR] Ошибка авторизации ({e.code}): {e.reason}")
        return False

def print_help_token():
    print("""
========================================================================
ВАЖНОЕ ПРИМЕЧАНИЕ О ПАРОЛЯХ GITHUB:
С августа 2021 года GitHub не принимает пароли учетных записей для API
и git push через HTTPS. Необходим Personal Access Token (PAT).

Как создать токен за 30 секунд:
1. Откройте в браузере: https://github.com/settings/tokens
2. Нажмите 'Generate new token' -> 'Generate new token (classic)'
3. Укажите Note (например: OpenWrtStudio) и отметьте галочку [x] 'repo'
4. Нажмите внизу 'Generate token' и скопируйте строку токена
========================================================================
""")

def main():
    token = os.environ.get("GITHUB_TOKEN")
    if not token and len(sys.argv) > 1:
        token = sys.argv[1]

    if not token:
        print_help_token()
        token = input("Введите ваш GitHub Personal Access Token: ").strip()

    if not token:
        print("Токен не указан. Завершение работы.")
        return

    if not check_token(token):
        print("\nПроверьте правильность токена и наличие прав 'repo'.")
        return

    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    print(f"\n[1/3] Подготовка репозитория в: {base_dir}")

    # Check if git is available
    git_bin = "git"
    try:
        subprocess.run([git_bin, "--version"], capture_output=True, check=True)
    except Exception:
        print("[WARN] Git не найден в PATH. Установите Git или загрузите релиз через веб-интерфейс.")
        return

    remote_auth_url = f"https://{REPO_OWNER}:{token}@github.com/{REPO_OWNER}/{REPO_NAME}.git"

    print("[2/3] Отправка изменений и тега v2.5.0...")
    cmds = [
        [git_bin, "init"],
        [git_bin, "add", "."],
        [git_bin, "commit", "-m", "feat: OpenWrt Studio v2.5.0 complete release with Forkop management"],
        [git_bin, "branch", "-M", "main"],
        [git_bin, "tag", "-a", RELEASE_TAG, "-m", f"Release {RELEASE_TAG}"],
        [git_bin, "remote", "remove", "origin"],
        [git_bin, "remote", "add", "origin", remote_auth_url],
        [git_bin, "push", "-u", "origin", "main", "--force"],
        [git_bin, "push", "origin", "--tags", "--force"]
    ]

    for cmd in cmds:
        try:
            subprocess.run(cmd, cwd=base_dir, capture_output=True, text=True)
        except Exception as e:
            print(f"Команда {' '.join(cmd)}: {e}")

    print("\n[3/3] Синхронизация завершена успешно!")
    print(f"Репозиторий доступен по адресу: https://github.com/{REPO_OWNER}/{REPO_NAME}")

if __name__ == "__main__":
    main()
