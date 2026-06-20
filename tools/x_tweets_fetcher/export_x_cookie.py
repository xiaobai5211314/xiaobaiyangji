#!/usr/bin/env python3
"""
从 /opt/x-login-firefox 的 cookies.sqlite 提取 X cookie，
写入 /www/wwwroot/小白养基/.secrets/influencer.env。
不打印真实 cookie 内容。
"""
import os
import sqlite3
import stat
import sys
from pathlib import Path

SECRETS_DIR = Path("/www/wwwroot/小白养基/.secrets")
ENV_FILE = SECRETS_DIR / "influencer.env"
FIREFOX_PROFILE_GLOB = "/opt/x-login-firefox/**/cookies.sqlite"

def find_cookies_db():
    for p in sorted(Path("/opt/x-login-firefox").rglob("cookies.sqlite")):
        if p.is_file():
            return str(p)
    return None

def extract_cookies(db_path):
    conn = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()
    auth_token = None
    ct0 = None
    for row in cur.execute("SELECT host, name, value FROM moz_cookies WHERE host LIKE '%x.com%' OR host LIKE '%twitter.com%'"):
        if row["name"] == "auth_token":
            auth_token = row["value"]
        elif row["name"] == "ct0":
            ct0 = row["value"]
    conn.close()
    return auth_token, ct0

def main():
    # 先停止容器
    print("[export_x_cookie] 停止 Firefox 容器...")
    os.system("docker stop x-login-firefox 2>/dev/null || true")

    db_path = find_cookies_db()
    if not db_path:
        print("[export_x_cookie] 未找到 cookies.sqlite", file=sys.stderr)
        sys.exit(1)

    print(f"[export_x_cookie] 读取: {db_path}")
    auth_token, ct0 = extract_cookies(db_path)

    if not auth_token or not ct0:
        print("[export_x_cookie] 未找到 auth_token 或 ct0，请确认已登录 X", file=sys.stderr)
        sys.exit(1)

    x_cookie = f"auth_token={auth_token}; ct0={ct0}"
    print(f"[export_x_cookie] 已提取 X_COOKIE，长度: {len(x_cookie)}")

    # 写入 .secrets/influencer.env
    SECRETS_DIR.mkdir(parents=True, exist_ok=True)
    os.chmod(SECRETS_DIR, stat.S_IRWXU)  # 700

    env_content = f"""# influencer.env — 白毛股神推文 X cookie 配置
# 自动生成于 export_x_cookie.py，请勿手动编辑
X_COOKIE={x_cookie}
INFLUENCER_TARGET_HANDLE=aleabitoreddit
INFLUENCER_SYNC_INTERVAL_MINUTES=30
INFLUENCER_POSTS_CACHE_PATH=/var/lib/xiaobaiyangji/influencer-posts.json
INFLUENCER_POSTS_MAX_STORE=100
INFLUENCER_POSTS_MAX_DISPLAY=10
TWS_TELEMETRY=0
"""

    ENV_FILE.write_text(env_content)
    os.chmod(ENV_FILE, stat.S_IRUSR | stat.S_IWUSR)  # 600

    print(f"[export_x_cookie] 已写入: {ENV_FILE}")
    print(f"[export_x_cookie] 文件权限: .secrets/ 700, influencer.env 600")
    print("[export_x_cookie] 完成。可以执行 stop_x_cookie_browser.sh 清理容器。")

if __name__ == "__main__":
    main()
