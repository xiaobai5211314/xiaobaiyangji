# 白毛股神推文 sidecar 部署说明

第一版不自动安装定时任务。确认服务器环境后，可选择 cron 或 systemd timer。

## systemd service 示例

`/etc/systemd/system/xiaobai-influencer-posts.service`

~~~ini
[Unit]
Description=Xiaobai influencer posts fetcher

[Service]
Type=oneshot
WorkingDirectory=/www/wwwroot/小白养基/tools/x_tweets_fetcher
ExecStart=/www/wwwroot/小白养基/tools/x_tweets_fetcher/.venv/bin/python fetch_posts.py
~~~

## systemd timer 示例

`/etc/systemd/system/xiaobai-influencer-posts.timer`

~~~ini
[Unit]
Description=Run Xiaobai influencer posts fetcher every 30 minutes

[Timer]
OnBootSec=3min
OnUnitActiveSec=30min
Persistent=true

[Install]
WantedBy=timers.target
~~~

## 私有环境文件示例

项目根目录 .secrets/influencer.env：

~~~bash
X_COOKIE='auth_token=xxx; ct0=yyy'
INFLUENCER_TARGET_HANDLE=aleabitoreddit
INFLUENCER_SYNC_INTERVAL_MINUTES=30
INFLUENCER_POSTS_CACHE_PATH=/var/lib/xiaobaiyangji/influencer-posts.json
INFLUENCER_POSTS_MAX_STORE=100
INFLUENCER_POSTS_MAX_DISPLAY=10
TWS_TELEMETRY=0
~~~

可以在服务器执行：

~~~bash
cd /www/wwwroot/小白养基/tools/x_tweets_fetcher
X_COOKIE='auth_token=xxx; ct0=yyy' bash ./write_influencer_env.sh
~~~

不要把真实 `auth_token`、`ct0` 或完整 `X_COOKIE` 提交到仓库。
