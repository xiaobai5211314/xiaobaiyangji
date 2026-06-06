# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指引。

## 项目概述

"小白养基" 是一个基金和股票投资管理应用，包含：
- **后端**：仓库根目录下的 ASP.NET Core 8.0 Web API (C#)
- **前端**：`miniprogram/` 目录下的微信小程序，使用 uni-app（Vue 3 + TypeScript + Vite）构建
- **静态 SPA**：`wwwroot/index.html`，通过又拍云 CDN 分发

前后端在同一仓库中，.NET 项目文件显式排除了 `miniprogram/` 目录。

## 构建与开发命令

### 后端（在仓库根目录执行）
```
dotnet restore              # 恢复 NuGet 包
dotnet build                # 构建
dotnet run                  # 本地开发服务器 http://localhost:5194
dotnet publish -c Release -o ./publish  # 生产构建
dotnet ef migrations add <name>         # 创建 EF 迁移（工具版本：dotnet-ef 10.0.5）
dotnet ef database update               # 应用迁移
```
迁移也会在应用启动时通过 `dbContext.Database.Migrate()` 自动执行。

本地 MySQL + Redis：
```
docker compose -f docker-compose.local.yml up -d
```

### 前端（在 `miniprogram/` 目录执行）
```
npm run dev:mp-weixin       # 微信开发者工具开发构建
npm run build:mp-weixin     # 生产构建
npm run typecheck           # vue-tsc --noEmit 类型检查
npm run check:pages-order   # 校验 pages[0] 必须是 pages/home/index
```
构建产物输出到 `miniprogram/dist/build/mp-weixin/`（导入微信开发者工具使用）。

## 架构

### 后端结构
| 目录 | 用途 |
|------|------|
| `Controllers/` | 7 个 API 控制器：Auth、Fund（~30 个端点）、Stock、UserUiState、ProductInsights、ValuationCalibration、InsightSnapshots |
| `Models/` | 17 个 EF Core 实体 + `AppDbContext`（通过 Pomelo 连接 MySQL） |
| `Services/` | 后台服务（FundScraperService、NavSettlementService）、BaiduOcrService、StockOcrParserService、MarketCacheService、StockQuoteService |
| `Migrations/` | EF Core 数据库迁移 |

### 前端结构
| 路径 | 用途 |
|------|------|
| `miniprogram/src/pages/` | 7 个页面：home（主仪表盘，~2800 行）、sector、news、analysis、login、profile、index-detail |
| `miniprogram/src/services/api/` | 6 个类型化 API 客户端模块（fund、stock、auth、uiState、insights、snapshots） |
| `miniprogram/src/services/request.ts` | HTTP 抽象层：类型化 `uni.request()` 封装，含 GET 缓存（60s TTL）、请求去重、降级数据、错误提示去重 |
| `miniprogram/src/stores/` | Vue 3 响应式 store（无 Vuex/Pinia）：`session.ts`、`theme.ts` |
| `miniprogram/src/utils/fundMetrics.ts` | 核心客户端业务逻辑：`buildPortfolioMetrics()`、行业分类、置信度评分、敞口分析 |
| `miniprogram/src/styles/` | SCSS 设计系统，含 4 套 CSS 自定义属性主题（neon/vivid/light/warm） |

### 关键架构模式

**认证**：无 JWT/Token。客户端每次请求通过 `username` 参数标识用户。微信登录生成 `wx_` 前缀的合成用户名。密码使用 ASP.NET Identity 的 `IPasswordHasher<User>` 哈希。

**多层缓存**（4 层）：
1. `IMemoryCache` 进程内缓存（行情 30s TTL，市场数据 60s TTL）
2. Redis（`localhost:6379`）
3. MySQL `MarketDataCache` 表（持久化缓存，含 fresh/stale TTL）
4. 前端内存缓存 + localStorage（GET 请求 60s 缓存，仪表盘数据 1h 缓存）

**后台服务**：`FundScraperService` 每 60 秒抓取东方财富基金估值；`NavSettlementService` 在每天 17:00-02:00 期间每 5 分钟执行一次 T+1 净值结算。

**OCR 优先录入**：基金和股票模块均有完整的截图导入流程。图片发送至百度 OCR API，`StockOcrParserService` 通过坐标/文本解析提取持仓，用户预览确认后导入。

**防御性字段访问**：前端接口大量使用可选字段。工具函数（`firstKnownNumber()`、`firstKnown()`、`stockPickNumber()`）尝试多种字段名别名（camelCase 和 PascalCase），以兼容 API 响应的不一致。

**外部数据源**：东方财富 API 是基金净值、股票行情、K 线、行业板块、资金流向和新闻的主要数据源。股票行情级联 3 个来源：东方财富 → 腾讯（`qt.gtimg.cn`） → 新浪（`hq.sinajs.cn`）。

### 命名 HttpClient 实例（在 Program.cs 中配置）
- `"FundGz"` — 6 秒超时，基金估值抓取
- `"EastMoney"` — 30 秒超时，净值结算
- `"EastMoneyQuote"` — 12 秒超时，股票行情（含自定义 referer 头）
- `"WeChatMiniProgram"` — 30 秒超时，微信 jscode2session

## 配置文件

- `appsettings.json` — 基础配置（连接字符串、日志）
- `appsettings.Production.json` — 已 gitignore，包含生产环境密钥（MySQL、百度 OCR 密钥、微信 AppId/Secret）
- `appsettings.example.json` — 配置模板（占位符值）
- `miniprogram/src/services/config.ts` — API 基础 URL（`https://guzhi.21212121.xyz`）

## CI/CD

- **后端部署**：推送 `.cs`/`.csproj`/`appsettings*.json`/`wwwroot/**` 到 `master`/`gpt-two`/`wechatapp` → GitHub Actions 构建，通过 SSH 部署到服务器，健康检查 `/api/health`
- **前端 CDN 部署**：推送 `wwwroot/index.html` → 上传到又拍云 CDN（`guzhicdn.21212121.xyz`），清除缓存
- **服务器**：systemd 服务运行在 7084 端口，Nginx 反向代理 `guzhi.21212121.xyz`

## 重要约定

- 前端页面统一使用 `<script setup lang="ts">` + Vue 3 Composition API
- 导航使用自定义 `AppTabBar` 组件 + `uni.reLaunch()`（替换页面栈），非原生 tabBar
- `pages/home/index` 必须是 `pages.json` 中的 `pages[0]`（由 `check:pages-order` 脚本强制校验）
- 状态管理使用原生 `reactive()` + `computed()`，无 Vuex、无 Pinia
- 项目未配置测试框架
- 项目未配置 ESLint 或 Prettier
- `project.private.config.json`（微信开发者工具私有配置）已 gitignore，并由 `clean:private-config` 脚本从 dist 中清除
