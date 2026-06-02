# CLAUDE.md（中文版）

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指引。

## 项目简介

小白养基 — 一个个人基金和股票投资组合追踪工具。用户通过微信小程序（前端）管理持仓，后端为 ASP.NET 8 API，使用 MySQL + Redis。`wwwroot/` 下同时提供 Web 前端。

---

## 构建与运行命令

**后端（ASP.NET 8）：**
```bash
dotnet restore
dotnet build
dotnet run
dotnet publish -c Release
```
应用启动时自动迁移数据库，监听端口 5000（HTTP）/ 5001（HTTPS）。

**前端（小程序 — Vue 3 + uni-app + TypeScript）：**
```bash
cd miniprogram
npm install
npm run dev:mp-weixin       # 开发构建，用于微信开发者工具
npm run build:mp-weixin     # 生产构建
npm run typecheck            # 仅 TypeScript 类型检查
```

**本地基础设施：**
```bash
docker-compose -f docker-compose.local.yml up -d   # MySQL 8.0 (端口 3306) + Redis 7 (端口 6379)
```
本地数据库凭据：数据库 `guzhi`，用户 `guzhi`，密码 `guzhi`。

---

## 架构

```
微信小程序 (miniprogram/)
  Vue 3 + uni-app + TypeScript + Vite
        │  HTTPS
        ▼
ASP.NET 8 Web API (Controllers/)
  Controllers: Auth, Fund, Stock, UserUiState,
               ProductInsights, InsightSnapshots,
               ValuationCalibration
  Services:
    FundScraperService         — 后台服务，每60秒从3个数据源抓取基金实时估值（带降级）
    NavSettlementService       — 后台服务，每天17:00–02:00每5分钟结算一次官方净值
    PortfolioSettlementService — 核心盈亏计算引擎（有效本金、已实现收益、每日归档）
    EastmoneyStockQuoteService — 实时股票行情，3个数据源降级（东方财富 → 腾讯 → 新浪）
    BaiduOcrService            — 百度AI OCR，用于解析券商持仓截图
    StockOcrParserService      — 基于空间位置的启发式算法，将OCR文本块解析为股票候选
        │
        ▼
MySQL 8.0 (EF Core / Pomelo) + Redis 7 (StackExchange.Redis)
```

### 后端关键约定

- **FundController.cs** 是最大的文件（约4600行），基金业务逻辑主要在此文件和 `PortfolioSettlementService` 中。
- 所有外部数据接口均采用 **3数据源降级** 模式（主用东方财富，备用腾讯/新浪）。
- 命名 `HttpClient` 工厂（`FundGz`、`EastMoney`、`EastMoneyQuote`、`TencentQuote`、`SinaQuote`、`WeChatMiniProgram`）在 `Program.cs` 中注册，各自配置了不同的超时时间和请求头。
- 用户认证同时支持传统用户名/密码（含旧版SHA256→ASP.NET Identity迁移）和微信 `jscode2session` 登录。
- `DailyArchive` 是历史业绩快照——每天每只基金一行记录，加上每用户每天一行 `TOTAL` 汇总。

### 小程序前端 (miniprogram/)

- 页面：`home`、`sector`、`news`、`analysis`、`login`、`profile`、`index-detail`
- API 服务层：`miniprogram/src/services/api/{fund,stock,auth,analysis,news,sector}.ts`
- 请求封装（`miniprogram/src/services/request.ts`）：GET 响应60秒缓存、重复请求去重、加载状态管理、降级数据支持
- 全局暗色主题 — 导航栏 `#111827`，背景 `#0f172a`
- API 基地址：`https://guzhi.21212121.xyz`；CDN：`https://guzhicdn.21212121.xyz`

### 数据模型（AppDbContext 共15张表）

核心实体：`FundData`（抓取的估值）、`MyFundConfig`（用户持仓）、`DailyArchive`（每日快照）、`User`（用户认证）、`StockHolding`、`StockWatchItem`、`StockQuoteSnapshot`、`StockKLineCache`、`FundValuationEstimate`、`FundValuationCalibration`、`UserUiState`、`UserInsightSnapshot`、`OcrCorrection`、`StockOcrImportBatch/Item`。

EF Core 迁移文件位于 `Migrations/`。应用启动时自动执行迁移。

### 配置文件

- `appsettings.json` — 最小默认配置（日志 + 本地 MySQL）
- `appsettings.example.json` — 完整配置结构（仅作参考）
- `appsettings.Production.json` — 已加入 .gitignore，生产环境使用
- 必需的敏感配置：`BaiduOcr:ApiKey/SecretKey`、`WeChatMiniProgram:AppId/AppSecret`、`ConnectionStrings:DefaultConnection`
