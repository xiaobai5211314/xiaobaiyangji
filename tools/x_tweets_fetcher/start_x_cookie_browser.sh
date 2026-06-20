#!/bin/bash
set -euo pipefail
echo "[start_x_cookie_browser] 启动临时 Firefox 容器（仅 127.0.0.1:5800）"
docker rm -f x-login-firefox 2>/dev/null || true
mkdir -p /opt/x-login-firefox
docker run -d \
  --name x-login-firefox \
  --restart=no \
  -p 127.0.0.1:5800:5800 \
  -e WEB_AUTHENTICATION=0 \
  -e SECURE_CONNECTION=0 \
  -e FF_OPEN_URL=https://x.com/i/flow/login \
  -v /opt/x-login-firefox:/config:rw \
  jlesage/firefox
echo "[start_x_cookie_browser] 容器已启动，请通过 https://xcookie.21212121.xyz 访问"
echo "[start_x_cookie_browser] 登录 X 完成后，执行 export_x_cookie.py 导出 cookie"
