#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="/www/wwwroot/小白养基"
DEPLOY_USER="deploy"
CONTROL_SCRIPT="/usr/local/sbin/xiaobaiyangji-deploy-control"
SERVICE_FILE="/etc/systemd/system/xiaobaiyangji.service"
SUDOERS_FILE="/etc/sudoers.d/xiaobaiyangji-deploy"

mkdir -p "$APP_DIR" /home/deploy

if id "$DEPLOY_USER" >/dev/null 2>&1; then
  chown -R "$DEPLOY_USER:$DEPLOY_USER" "$APP_DIR" /home/deploy
else
  echo "警告：系统没有 deploy 用户。你的 GitHub Secrets.SERVER_USER 如果不是 deploy，请把脚本里的 DEPLOY_USER 改成实际用户。"
fi

install -m 0755 ./xiaobaiyangji-deploy-control "$CONTROL_SCRIPT"
install -m 0644 ./xiaobaiyangji.service "$SERVICE_FILE"

cat > "$SUDOERS_FILE" <<SUDOERS
$DEPLOY_USER ALL=(root) NOPASSWD: $CONTROL_SCRIPT *
SUDOERS
chmod 0440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"

systemctl daemon-reload
systemctl enable xiaobaiyangji.service >/dev/null || true

echo "初始化完成。现在可以重新跑 GitHub Actions。"
