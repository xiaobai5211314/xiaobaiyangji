# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What is this project?

小白养基 (Beginner's Fund Manager) — a personal investment portfolio tracker for Chinese mutual funds (基金) and stocks (股票). Users manage holdings via a WeChat Mini Program (frontend) backed by an ASP.NET 8 API (backend) with MySQL + Redis. A web frontend is also served from `wwwroot/`.

---

## Build & Run Commands

**Backend (ASP.NET 8):**
```bash
dotnet restore
dotnet build
dotnet run
dotnet publish -c Release
```
App auto-migrates the database on startup and listens on ports 5000 (HTTP) / 5001 (HTTPS).

**Frontend (miniprogram — Vue 3 + uni-app + TypeScript):**
```bash
cd miniprogram
npm install
npm run dev:mp-weixin       # Dev build for WeChat DevTools
npm run build:mp-weixin     # Production build
npm run typecheck            # TypeScript checking only
```

**Local infrastructure:**
```bash
docker-compose -f docker-compose.local.yml up -d   # MySQL 8.0 (port 3306) + Redis 7 (port 6379)
```
Local DB credentials: database `guzhi`, user `guzhi`, password `guzhi`.

---

## Architecture

```
WeChat Mini Program (miniprogram/)
  Vue 3 + uni-app + TypeScript + Vite
        │  HTTPS
        ▼
ASP.NET 8 Web API (Controllers/)
  Controllers: Auth, Fund, Stock, UserUiState,
               ProductInsights, InsightSnapshots,
               ValuationCalibration
  Services:
    FundScraperService     — background, scrapes fund estimates every 60s from 3 sources with fallback
    NavSettlementService   — background, settles official NAVs 17:00–02:00 CST every 5min
    PortfolioSettlementService — core P&L engine (effective base amounts, realized profit, archives)
    EastmoneyStockQuoteService — real-time stock quotes with 3-source fallback (EastMoney → Tencent → Sina)
    BaiduOcrService        — Baidu AI OCR for brokerage screenshot parsing
    StockOcrParserService  — spatial heuristics to parse OCR text blocks into stock candidates
        │
        ▼
MySQL 8.0 (EF Core / Pomelo) + Redis 7 (StackExchange.Redis)
```

### Key backend conventions

- **FundController.cs** is the largest file (~4600 lines). Fund business logic lives there and in `PortfolioSettlementService`.
- All external data APIs use a **3-source fallback** pattern (primary EastMoney, then Tencent/Sina).
- Named `HttpClient` factories (`FundGz`, `EastMoney`, `EastMoneyQuote`, `TencentQuote`, `SinaQuote`, `WeChatMiniProgram`) are registered in `Program.cs` with tailored timeouts and headers.
- User auth supports both traditional username/password (with legacy SHA256→ASP.NET Identity migration) and WeChat `jscode2session`.
- `DailyArchive` is the historical performance snapshot — one row per fund per day, plus a `TOTAL` row per user per day.

### Miniprogram frontend (miniprogram/)

- Pages: `home`, `sector`, `news`, `analysis`, `login`, `profile`, `index-detail`
- API service layer: `miniprogram/src/services/api/{fund,stock,auth,analysis,news,sector}.ts`
- Request wrapper (`miniprogram/src/services/request.ts`): 60s GET cache, in-flight dedup, loading indicators, fallback data
- Global dark theme — navigation bar `#111827`, background `#0f172a`
- API base: `https://guzhi.21212121.xyz`; CDN: `https://guzhicdn.21212121.xyz`

### Data model (15 tables in AppDbContext)

Key entities: `FundData` (scraped estimates), `MyFundConfig` (user holdings), `DailyArchive` (daily snapshots), `User` (auth), `StockHolding`, `StockWatchItem`, `StockQuoteSnapshot`, `StockKLineCache`, `FundValuationEstimate`, `FundValuationCalibration`, `UserUiState`, `UserInsightSnapshot`, `OcrCorrection`, `StockOcrImportBatch/Item`.

EF Core migrations are in `Migrations/`. The app auto-applies migrations at startup.

### Configuration

- `appsettings.json` — minimal defaults (logging + local MySQL)
- `appsettings.example.json` — full config shape (reference only)
- `appsettings.Production.json` — gitignored, used in production
- Required secrets: `BaiduOcr:ApiKey/SecretKey`, `WeChatMiniProgram:AppId/AppSecret`, `ConnectionStrings:DefaultConnection`
