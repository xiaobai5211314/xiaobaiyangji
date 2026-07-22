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
- **生产 JWT 密钥已配置（2026-07-22 hardening 修复）**：`appsettings.Production.json` 现含 `Auth:TokenSecret` 真实强随机密钥（32 字节 base64），不再回退占位符，旧 token 部署后失效需重新登录。密钥明文随仓库部署（项目既有模式）；如需更严格可改环境变量注入并滚动密钥。排查生产数据问题时若需临时 token 须用真实密钥自签，不再能用公开占位符。

## DailyArchive 数据损坏防护（不变式守卫）
- **根因类别**：单只基金的 `DailyArchive` 行可能被错写成 TOTAL 汇总值（如 2026-07-20 的 017968 行与 TOTAL 字节级相同，日历展示假 −4.32%）。诱因是官方净值缺失时上游 `MyFundConfig.HoldAmount`/`LastSettledProfit` 被错填成总数。
- **根治机制**：`DailyArchiveService.SanitizeArchiveRows`（静态方法）在落库前校验——多基金组合下，若某基金行 `Assets` ≈ TOTAL `Assets`（抄写指纹），判定损坏并剔除该坏行，并用剩余有效基金重算 TOTAL（`source=guard-recomputed-total`，恒等于 Σ基金Assets）；单基金组合豁免。已下沉到所有写入的汇聚点 `Controllers.FundController.UpsertDailyArchivesAsync`（覆盖前台结算 `BuildArchiveRowsFromCurrentHoldings`、客户端 `SaveArchive`、OCR 归档等全部路径）；另在 `DailySettlementService.BuildArchiveRows`（后台结算）保留守卫。settle-daily 两处（`BuildArchiveRowsFromCurrentHoldings` 与 `DailySettlementService.BuildArchiveRows`）均在基金官方净值缺失时写 `Source="nav-missing"` 标记行（不计入 TOTAL，前端当日明细显示「净值缺失」）。
- **回归测试**：`tests/PortfolioAccounting.Tests/Program.cs` 用例 A（6 基金含损坏 TOTAL→剔除1行+重算）与用例 B（单基金组合不触发）。`dotnet build` 0 错误、`dotnet run` 该测试工程验证通过。
- **历史扫描**：生产库某账号全历史 537 条归档扫描确认，除已修 07-20 外无同类损坏。
