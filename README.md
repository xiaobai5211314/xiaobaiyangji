# 小白养基

小白养基是一个自用的基金和股票投资管理应用，用来管理持仓、导入截图数据、查看收益归档、跟踪盘中估值，并展示“白毛股神”推文缓存。

项目事实来源主要是 [CONTEXT.md](./CONTEXT.md)、[docs/adr](./docs/adr/) 和当前源码。涉及金额、收益、OCR、缓存、推文和部署的规则，以这些文件为准；不确定的信息应标注“待核实”。

## 功能概览

- 基金持仓管理：通过蚂蚁基金 App 截图 OCR 导入持仓和收益信息。
- 股票持仓管理：支持 OCR 导入或手动录入股票持仓。
- 收益统计：维护每日收益归档、盈亏日历和首页 summary。
- 盘中估值：抓取东方财富基金估值作为临时参考。
- 微信小程序：正式源码位于 `miniprogram/src/`。
- WebApp：正式源码为单文件 `wwwroot/index.html`。
- 双主题：两端只保留默认深色“曜石流光”和浅色“雾光银蓝”，历史主题设置自动归并到这两种；前者是深色行情工作台，后者是清晰的系统信息层级。
- 界面术语：资产显示统一使用“资产可见、金额遮罩、收益遮罩、隐私保护”；底部导航统一使用“持仓、行情、资讯、复盘、观点”。
- 响应式布局：WebApp 各视口使用带安全区和页面底部留白的固定底部导航；金额、状态、弹窗和工具栏不得依赖隐藏溢出来规避遮挡。小程序页面为固定底栏预留滚动空间，页头、日历工具栏和操作区允许安全换行。
- CDN 发布校验：WebApp 上传又拍云后必须刷新 `https://guzhicdn.21212121.xyz/index.html`，再校验 CDN 返回正文的 SHA-256 与仓库 `wwwroot/index.html` 一致；刷新或校验失败应使工作流失败，不能只显示警告后继续。
- 推文模块：通过 sidecar 缓存固定账号 `@aleabitoreddit` 的推文和回复，前端只读取后端接口。

## 技术栈

| 层 | 技术 |
|---|---|
| 后端 | ASP.NET Core 8.0 / C# |
| 数据库 | MySQL |
| 缓存 | Redis、ASP.NET Core MemoryCache、MySQL 持久化缓存 |
| 微信小程序 | uni-app / Vue 3 / TypeScript / Vite |
| WebApp | `wwwroot/index.html` 单文件 |
| OCR | 百度 OCR API |
| 推文 sidecar | Python / twscrape / JSON 缓存 |

## 目录结构

```text
Controllers/                 ASP.NET Core API 控制器
Services/                    业务服务、后台服务、缓存和 OCR 逻辑
Models/                      EF Core 实体、DTO、AppDbContext
Migrations/                  EF Core 数据库迁移
miniprogram/src/             微信小程序正式源码
wwwroot/index.html           WebApp 正式入口
tools/x_tweets_fetcher/      推文抓取、翻译和缓存 sidecar
tests/                       控制台测试项目
docs/adr/                    架构决策记录
docs/deploy/                 部署说明
docs/agents/                 Agent 协作说明
AGENTS.md                    Codex / 通用 Agent 工作规则
CLAUDE.md                    Claude Code 工作入口
CONTEXT.md                   领域口径和业务事实源
```

注意：`frontend/src/` 不是正式前端入口；`wwwroot/v2/` 已废弃，不要恢复。

## 本地开发

### 1. 启动 MySQL 和 Redis

```powershell
docker compose -f docker-compose.local.yml up -d
```

### 2. 还原和构建后端

```powershell
dotnet restore
dotnet build
```

### 3. 启动后端

```powershell
dotnet run
```

本地启动地址以 `Properties/launchSettings.json` 和运行输出为准。

### 4. 小程序开发

```powershell
cd miniprogram
npm install
npm run dev:mp-weixin
```

生产构建：

```powershell
npm run build:mp-weixin
```

## 验证命令

后端构建：

```powershell
dotnet build
```

控制台测试：

```powershell
dotnet run --project tests/PortfolioAccounting.Tests/PortfolioAccounting.Tests.csproj
dotnet run --project tests/InfluencerPosts.Tests/InfluencerPosts.Tests.csproj
```

小程序检查：

```powershell
cd miniprogram
npm run typecheck
npm run check:pages-order
```

## 财务和 OCR 口径

本项目对基金金额和收益口径有硬约束：

- 蚂蚁 OCR 快照是平台展示金额的最高优先校准源，但不是每天唯一的当前金额来源。
- 同一北京时间自然日的 OCR 当前金额不得再次叠加盘中估算或官方净值收益；跨过 00:00 后旧 OCR 退出当前展示优先级。
- 平台当前金额必须拆分为已确认持仓金额和买入待确认金额。
- 无新鲜 OCR 时，首页当前金额必须随官方净值或盘中估值的单基当前市值合计滚动。
- `DailyArchive` 正式档案只能写入已确认金额和已确认收益。
- 盘中估值只能作为临时参考，不得写入正式历史日历。
- 昨日收益不得用于反推确认金额或持有收益。
- 周末、节假日和交易日 09:30 前只承接上一交易日金额与累计收益，当前自然日的今日收益、收益率和单基金涨跌幅显示 0。
- 资产详情页 OCR 到份额和成本价时，系统优先按平台字段校准份额和成本金额；持有收益口径差写入 `PlatformHoldingAdjustment`，`RealizedProfit` 只记录确认卖出，前端“已落袋”不再被 OCR 校准差误触发。常规 OCR 截图缺少份额或成本价时，系统可自动推导辅助字段。派生份额和成本不得覆盖同日蚂蚁 OCR 的金额、昨日收益或持有收益。

修改相关逻辑前必须先读：

- [CONTEXT.md](./CONTEXT.md)
- [0001 基金金额事实源](./docs/adr/0001-fund-accounting-source-of-truth.md)
- [0002 DailyArchive 确认策略](./docs/adr/0002-daily-archive-confirmation-policy.md)
- [0004 OCR 导入字段契约](./docs/adr/0004-ocr-import-data-contract.md)

## 推文 sidecar 和敏感信息

推文功能的架构边界见 [0005 白毛股神推文数据源、缓存与翻译边界](./docs/adr/0005-influencer-posts-source-and-cache.md) 和 [sidecar 部署说明](./docs/deploy/influencer-posts-sidecar.md)。

关键安全规则：

- 私有环境文件只能放在服务器 `/www/wwwroot/小白养基/.secrets/influencer.env`。
- 禁止读取、打印、复制、提交 cookie、token、腾讯云密钥或任何私有环境内容。
- 抓取或翻译失败只能降级，不得影响基金首页、收益计算、OCR、`DailyArchive`、盈亏日历或首页 summary。

## 部署

仓库包含 GitHub Actions：

- `.github/workflows/deploy-backend.yml`：后端构建、发布和健康检查。
- `.github/workflows/upyun-deploy.yml`：上传 `wwwroot/index.html` 到又拍云 CDN 并刷新缓存。

部署触发分支和路径以工作流文件当前内容为准。

## Agent 协作

- Codex / 通用 Agent：读 [AGENTS.md](./AGENTS.md)。
- Claude Code：读 [CLAUDE.md](./CLAUDE.md)，并按其中要求继续读取 `AGENTS.md`。
- 领域规则：读 [CONTEXT.md](./CONTEXT.md)。
- 架构决策：读 [docs/adr](./docs/adr/)。

Agent 必须遵守幻觉防控规则：涉及事实必须给来源，不确定就标「待核实」，用户纠错时先核对再修正。

## License

本项目使用 GNU Affero General Public License v3.0。见 [LICENSE.txt](./LICENSE.txt)。
