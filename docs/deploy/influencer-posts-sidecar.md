# 白毛股神推文 sidecar 部署说明

## 固定路径与安全边界

| 用途 | 路径 |
|------|------|
| 服务器项目目录 | `/www/wwwroot/小白养基` |
| 私有环境文件 | `/www/wwwroot/小白养基/.secrets/influencer.env` |
| 推文 JSON 缓存 | `/var/lib/xiaobaiyangji/influencer-posts.json` |
| twscrape 账号数据库建议路径 | `/var/lib/xiaobaiyangji/twscrape-accounts.db` |

私有环境文件只能存在于服务器本地。禁止读取到终端、复制进 appsettings、写入日志或提交到 Git。检查时只允许使用 `test -f`、`stat` 等不显示内容的命令。

~~~bash
cd /www/wwwroot/小白养基
install -d -m 700 .secrets
install -d -m 700 /var/lib/xiaobaiyangji
test -f .secrets/influencer.env
stat -c '%a %n' .secrets .secrets/influencer.env /var/lib/xiaobaiyangji
~~~

目标权限为 `.secrets` 目录 `700`、私有环境文件 `600`。

## 安装 sidecar

~~~bash
cd /www/wwwroot/小白养基/tools/x_tweets_fetcher
python3 -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt
~~~

sidecar 会自行读取项目根目录下的私有环境文件；命令行不要拼接 cookie 或翻译密钥。

## 临时 Firefox 登录方案

X 登录由用户手动完成。Docker Firefox 只绑定回环地址：

~~~bash
docker run -d --name x-login-firefox \
  -p 127.0.0.1:5800:5800 \
  -v /opt/x-login-firefox:/config \
  <firefox-image>
~~~

`<firefox-image>`、容器内 profile 路径和最终 `cookies.sqlite` 位置依赖实际镜像，部署前必须核实，不能凭文档猜测。端口 `5800` 不允许长期绑定 `0.0.0.0` 或直接公网暴露。

需要远程登录时，使用专用域名的 HTTPS + Basic Auth 反向代理到 `127.0.0.1:5800`。域名、证书路径和认证文件由服务器实际配置决定：

~~~nginx
server {
    listen 443 ssl;
    server_name x-login.example.com;

    ssl_certificate     /path/to/fullchain.pem;
    ssl_certificate_key /path/to/privkey.pem;

    auth_basic "Restricted";
    auth_basic_user_file /etc/nginx/.htpasswd-x-login;

    location / {
        proxy_pass http://127.0.0.1:5800;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto https;
    }
}
~~~

登录和导出完成后应停止临时 Firefox 或关闭该反代入口。配置依据：[Docker 端口发布](https://docs.docker.com/engine/network/port-publishing/)、[Nginx proxy_pass](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_pass)、[Nginx Basic Auth](https://nginx.org/en/docs/http/ngx_http_auth_basic_module.html)。

## 一键导出脚本方案

仓库已包含 `tools/x_tweets_fetcher/export_x_cookie.py`。脚本会更新 `X_COOKIE`，并保留同一私有文件中的翻译配置。服务器 Firefox profile 的实际位置仍为**待核实**。当前实现行为如下：

1. 在服务器本地停止临时 Firefox 容器，再从 `/opt/x-login-firefox` 下找到 `cookies.sqlite` 并只读查询 X 登录所需的两项 cookie。
2. 写入 `/www/wwwroot/小白养基/.secrets/influencer.env`，目录权限 `700`、文件权限 `600`。
3. 更新 cookie 时保留文件内其他非 cookie 配置，包括腾讯云翻译配置。
4. 日志不输出 cookie 内容、长度、请求头或翻译密钥。
5. 找不到 cookie 或字段为空时退出失败，不覆盖旧文件。

## 翻译配置

翻译只在 sidecar 中执行，默认 provider 为 `none`。基础配置项：

~~~text
TRANSLATE_PROVIDER
TRANSLATE_TARGET_LANG
TRANSLATE_CACHE_ENABLED
TRANSLATE_MAX_CHARS_PER_POST
~~~

腾讯云机器翻译配置如下，所有值只允许写入服务器本地 `.secrets/influencer.env`：

~~~text
TRANSLATE_PROVIDER=tencent
TRANSLATE_TARGET_LANG=zh-CN
TRANSLATE_TENCENT_SOURCE_LANG=en
TRANSLATE_TENCENT_REGION=ap-guangzhou
TRANSLATE_TENCENT_SECRET_ID=<new-secret-id>
TRANSLATE_TENCENT_SECRET_KEY=<new-secret-key>
~~~

`TextTranslate` 请求使用 `SecretId` 和 `SecretKey` 完成 TC3-HMAC-SHA256 鉴权；腾讯云账号 `APPID` 不作为本实现的鉴权字段。`SourceText`、`Source`、`Target`、`ProjectId` 是该接口的业务参数，本实现对固定英文推文使用 `Source=en`、`Target=zh`、`ProjectId=0`。依据：[腾讯云 TextTranslate](https://cloud.tencent.com/document/api/551/15619)、[腾讯云公共参数](https://cloud.tencent.com/document/api/551/15615)、[腾讯云访问密钥](https://cloud.tencent.com/document/product/598/40488)。

已在聊天、截图、日志或其他公开位置出现过的密钥必须先禁用并重新创建，禁止继续配置到服务器。腾讯云说明访问密钥由 `SecretId` 与 `SecretKey` 共同组成，并提供禁用、删除等管理操作；具体处置以控制台当前状态为准。依据：[腾讯云访问密钥](https://cloud.tencent.com/document/product/598/40488)。

GitHub Actions 部署使用以下仓库 Secrets，工作流不得输出它们的值：

~~~text
TENCENT_TRANSLATE_SECRET_ID
TENCENT_TRANSLATE_SECRET_KEY
~~~

`.github/workflows/deploy-backend.yml` 会把 sidecar 一并放入发布包，通过临时权限受限的 JSON 文件传到服务器，再由 `configure_translation_env.py` 原子合并到 `.secrets/influencer.env`。合并必须保留现有 `X_COOKIE`，随后部署任务运行一次 sidecar，并要求缓存中至少产生一条成功译文。

也可使用 `custom` provider。它向配置的 endpoint 发送 JSON：

~~~json
{
  "text": "source text",
  "targetLang": "zh-CN"
}
~~~

返回体必须包含 `translatedText`。如配置了密钥，sidecar 使用 Bearer Authorization 请求头，但任何日志均不得记录该请求头。`openai` 和 `libretranslate` provider 也必须显式配置 endpoint；OpenAI 兼容模式还需配置 model。所有 provider 失败时只把该条状态写为 `failed`，不影响英文原文或页面加载。

## systemd 定时同步

`/etc/systemd/system/xiaobai-influencer-posts.service`：

~~~ini
[Unit]
Description=Xiaobai influencer posts fetcher

[Service]
Type=oneshot
WorkingDirectory=/www/wwwroot/小白养基/tools/x_tweets_fetcher
ExecStart=/www/wwwroot/小白养基/tools/x_tweets_fetcher/.venv/bin/python fetch_posts.py
User=www
Group=www
~~~

`/etc/systemd/system/xiaobai-influencer-posts.timer`：

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

~~~bash
systemctl daemon-reload
systemctl enable --now xiaobai-influencer-posts.timer
systemctl start xiaobai-influencer-posts.service
systemctl status xiaobai-influencer-posts.service --no-pager
~~~

`User`、`Group` 必须按服务器实际运行账户核实，并保证该账户可读私有环境文件、可写 `/var/lib/xiaobaiyangji`。

也可用 cron 调用同一个 Python 命令。不要在 crontab 命令行中展开任何密钥。

## 验证

~~~bash
test -s /var/lib/xiaobaiyangji/influencer-posts.json
curl --fail --silent 'http://127.0.0.1:7084/api/influencer-posts/latest?limit=20' > /tmp/influencer-posts-response.json
python3 -m json.tool /tmp/influencer-posts-response.json > /dev/null
~~~

页面验收：底部导航出现第 5 个“推文”tab；持仓页底部没有推文模块；推文页显示缓存时间、最多 20 条、中文译文优先、英文原文和原文链接。

## 常见故障

| 现象 | 只读检查 | 处理 |
|------|----------|------|
| sidecar 报 cookie 缺失 | `test -f` 和 `stat` 私有环境文件，禁止 `cat` | 用户重新手动登录并在服务器本地导出 |
| 缓存目录不存在 | `stat /var/lib/xiaobaiyangji` | `install -d -m 700` 后设置正确属主 |
| twscrape 报数据库无法打开 | 检查数据库父目录权限和 systemd 运行用户 | 修正目录属主/权限，不把数据库放进 Git |
| 接口为空列表 | 检查缓存文件是否存在、非空、JSON 有效，再看 service 状态 | 修复 sidecar 后重跑；失败期间保留旧缓存 |
| 前端没有“推文”tab | 检查部署的 `wwwroot/index.html` 或小程序构建是否来自当前提交 | 重新部署正式入口，不恢复旧 v2 目录 |
| 翻译为空 | 检查 provider 是否为 `none`；腾讯云模式检查 SecretId、SecretKey、source、region 是否配置，但不得输出值 | 无配置时属于预期降级，页面显示英文原文 |
| 腾讯云翻译失败 | 检查 `translationStatus`、sidecar 状态和腾讯云错误码；禁止输出请求头或密钥值 | 按腾讯云错误码修复权限、密钥、地域或时间同步问题；失败不能阻塞抓取缓存 |

## 相关实现

- `tools/x_tweets_fetcher/fetch_posts.py`
- `Services/InfluencerPostsCacheService.cs`
- `Controllers/InfluencerPostsController.cs`
- `docs/adr/0005-influencer-posts-source-and-cache.md`
