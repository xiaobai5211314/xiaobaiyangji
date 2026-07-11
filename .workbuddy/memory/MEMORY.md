# 小白养基 项目长期记忆

## 部署架构
- 后端：ASP.NET Core 8，systemd service `guzhi-assistant.service`，监听 `127.0.0.1:7084`，`ASPNETCORE_ENVIRONMENT=Production`。
- 反代：宝塔 nginx `guzhi.21212121.xyz` → `127.0.0.1:7084`，配置见 `.github/server/nginx-guzhi-api.conf`。
- 前端 WebApp：单文件 `wwwroot/index.html`，CDN 域名 `guzhicdn.21212121.xyz`（又拍云），源站 `guzhi.21212121.xyz`。
- API 固定域名：前端 `API_BASE = 'https://guzhi.21212121.xyz'`，CDN 页面跨域请求源站 API。
- CORS 允许源：`appsettings.Production.json` 的 `AllowedOrigins`（含 guzhi + guzhicdn）。

## CI/CD
- 后端部署 `deploy-backend.yml`：push `.cs/.csproj/appsettings*.json/wwwroot/**/tools/.github/server/**` 触发，SSH 上传 publish 包覆盖 `/www/wwwroot/小白养基/`，含 appsettings.Production.json 覆盖 + DB DDL 迁移 + 健康检查 + 推文翻译缓存检查。
- 前端部署 `upyun-deploy.yml`：push `wwwroot/**` 触发，upx 上传 index.html 到又拍云并 purge CDN 缓存。
- force push 到 master 不会自动触发 upyun-deploy（GitHub 对 force push 的 push 事件处理特殊）。

## 关键坑（已踩）
- **.NET 8 minimal hosting CORS 顺序**：`UseCors` 必须在显式 `UseRouting` 之后。否则隐式 routing 在 `MapControllers` 时才加，CORS 跑在 routing 前，跨域 preflight 不被拦截返回 405，跨域页面全挂（同源正常）。修复：`app.UseRouting(); app.UseCors(...);`。
- **CORS 响应头读取**：fetch API 的 `res.headers.get('access-control-allow-origin')` 在跨域场景可能返回 null（浏览器过滤），查 CORS 头必须用 Playwright `browser_network_request` 看 network 层原始响应头。
- **Iconify 图标拦截按钮点击**：Iconify 3.x 把 `<span data-icon>` 替换成含 `<svg>` 结构，SVG/子元素可能拦截父 button 的 @click。需 `pointer-events: none` 覆盖到按钮所有子元素。
