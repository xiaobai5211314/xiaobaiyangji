#!/bin/bash
set -euo pipefail
echo "[stop_x_cookie_browser] 清理临时 Firefox 容器"
docker rm -f x-login-firefox 2>/dev/null || true
echo "[stop_x_cookie_browser] 完成"
