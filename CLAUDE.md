# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指引。

## 项目概述

"小白养基" 是一个基金和股票投资管理应用，包含：
- **后端**：仓库根目录下的 ASP.NET Core 8.0 Web API (C#)
- **微信小程序**：正式源码仅为 `miniprogram/src/`，使用 uni-app（Vue 3 + TypeScript + Vite）构建
- **WebApp**：正式源码仅为 `wwwroot/index.html`（单文件），通过又拍云 CDN 分发

`wwwroot/v2/` 已删除且不再使用；`frontend/src/` 不是正式前端源码目录。Agent 修改前端时只能检查和修改 `miniprogram/src/`、`wwwroot/index.html`，不得恢复 `wwwroot/v2/`，不得把 `frontend/src/` 当作正式入口。

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

### 微信小程序（在 `miniprogram/` 目录执行）
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
| `Controllers/` | ASP.NET Core API 控制器，包括 Auth、Fund、Stock、InfluencerPosts 等接口 |
| `Models/` | EF Core 实体、API DTO 与 `AppDbContext`（通过 Pomelo 连接 MySQL） |
| `Services/` | HostedService、基金/股票/OCR/归档服务以及推文缓存只读服务 |
| `Migrations/` | EF Core 数据库迁移 |

### 前端结构
| 路径 | 用途 |
|------|------|
| `miniprogram/src/pages/` | 微信小程序正式页面源码，包括 home、sector、news、analysis、tweets 等页面 |
| `miniprogram/src/services/api/` | 微信小程序类型化 API 客户端模块 |
| `miniprogram/src/services/request.ts` | HTTP 抽象层：类型化 `uni.request()` 封装，含 GET 缓存（60s TTL）、请求去重、降级数据、错误提示去重 |
| `miniprogram/src/stores/` | Vue 3 响应式 store（无 Vuex/Pinia）：`session.ts`、`theme.ts` |
| `miniprogram/src/utils/fundMetrics.ts` | 核心客户端业务逻辑：`buildPortfolioMetrics()`、行业分类、置信度评分、敞口分析 |
| `miniprogram/src/styles/` | SCSS 设计系统，含 4 套 CSS 自定义属性主题（neon/vivid/light/warm） |
| `wwwroot/index.html` | WebApp 唯一正式源码与入口 |

### 关键架构模式

**认证**：无 JWT/Token。客户端每次请求通过 `username` 参数标识用户。微信登录生成 `wx_` 前缀的合成用户名。密码使用 ASP.NET Identity 的 `IPasswordHasher<User>` 哈希。

**多层缓存**（4 层）：
1. `IMemoryCache` 进程内缓存（行情 30s TTL，市场数据 60s TTL）
2. Redis（`localhost:6379`）
3. MySQL `MarketDataCache` 表（持久化缓存，含 fresh/stale TTL）
4. 前端内存缓存 + localStorage（GET 请求 60s 缓存，仪表盘数据 1h 缓存）

**后台服务**：`FundScraperService` 每 60 秒抓取东方财富基金估值；`NavSettlementService` 在每天 17:00-02:00 期间每 5 分钟执行一次 T+1 净值结算；`DailySettlementService` 按北京时间归档，蚂蚁 OCR 确认数据写正式档案，缺少 OCR 时用官方净值写 `official-nav-pending` 待确认档案，后续由蚂蚁确认数据覆盖。盘中估值不得进入正式日历。

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
- `appsettings.example.json` — 配置模板（含 `ConnectionStrings`、`AllowedOrigins`、`BaiduOcr`、`WeChatMiniProgram`）
- `miniprogram/src/services/config.ts` — API 基础 URL（`https://guzhi.21212121.xyz`）和 CDN URL（`https://guzhicdn.21212121.xyz`）

## CI/CD

- **后端部署**：推送 `.cs`/`.csproj`/`appsettings*.json`/`wwwroot/**` 到 `master`/`gpt-two`/`wechatapp` → GitHub Actions 构建，通过 SSH 部署到服务器，健康检查 `/api/health`
- **WebApp CDN 部署**：推送 `wwwroot/**` → 上传 `wwwroot/index.html` 到又拍云 CDN（`guzhicdn.21212121.xyz`），清除缓存
- **服务器**：systemd 服务运行在 7084 端口，Nginx 反向代理 `guzhi.21212121.xyz`

## Agent 技能配置

### 问题跟踪器

使用 GitHub Issues（仓库 `xiaobai5211314/xiaobaiyangji`）。详见 `docs/agents/issue-tracker.md`。

### 分类标签

五个标准标签：`needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human`、`wontfix`。详见 `docs/agents/triage-labels.md`。

### 领域文档

单上下文模式——`CONTEXT.md` + `docs/adr/` 位于仓库根目录。详见 `docs/agents/domain.md`。

## 财务/收益口径强约束

- 任何涉及基金金额、收益、收益率的修改，必须先确认数据来源（见 `CONTEXT.md` 和 `docs/adr/0001-...`）。
- 蚂蚁 OCR 确认数据是正式金额事实源。
- 盘中估值只能作为临时估算，不得写入正式历史日历（见 `docs/adr/0002-...`）。
- 昨日收益不得从当前金额或持有收益中重复扣除。
- 首页 summary 和盈亏日历可以读取不同表/接口，但展示口径必须一致。
- 修改收益逻辑必须给出至少一个手工验算例子。
- 禁止为了让页面数字看起来接近而改公式。
- 不确定时先提出问题，不要直接猜业务口径。

## 重要约定

- 小程序页面使用 `<script setup lang="ts">` + Vue 3 Composition API；单文件 WebApp 继续使用 `wwwroot/index.html` 内现有 Vue 写法
- 导航使用自定义 `AppTabBar` 组件 + `uni.reLaunch()`（替换页面栈），非原生 tabBar
- 正式前端只有 `miniprogram/src/` 和 `wwwroot/index.html`；`frontend/src/` 不是正式入口，`wwwroot/v2/` 已删除且禁止恢复
- 底部导航固定为 5 个板块：持仓、板块、资讯、盈亏、推文；“白毛股神推文”必须位于独立“推文”页，不得放在持仓页底部
- WebApp 推文页的“回”和“查看回复”进入详情视图；详情视图必须保留推文与回复的中文译文、英文原文，并让原文链接可点击打开
- `pages/home/index` 必须是 `pages.json` 中的 `pages[0]`（由 `check:pages-order` 脚本强制校验）
- 状态管理使用原生 `reactive()` + `computed()`，无 Vuex、无 Pinia
- 项目未配置标准测试框架（xUnit/NUnit）；`tests/PortfolioAccounting.Tests/` 是控制台应用，用手动 `throw` 断言测试 PortfolioAccounting 和 DailyArchiveService
- 项目未配置 ESLint 或 Prettier
- `project.private.config.json`（微信开发者工具私有配置）已 gitignore，并由 `clean:private-config` 脚本从 dist 中清除

## 推文 sidecar 与敏感信息

- 固定目标账号为 `@aleabitoreddit`；sidecar 将推文和已抓取回复写入 JSON 缓存，ASP.NET Core API 只读缓存，前端不得直连 X 或外部翻译服务。
- 服务器私有环境文件仅允许位于 `/www/wwwroot/小白养基/.secrets/influencer.env`，并由 `.gitignore` 排除。
- 禁止读取、打印、提交该私有环境文件内容；禁止把 cookie 或翻译服务密钥写入 `appsettings.json`、`appsettings.example.json`、文档、日志或 GitHub。
- 抓取或翻译失败只能降级为旧缓存或英文原文，不得影响基金首页、收益计算、OCR、`DailyArchive` 或首页 summary。
- 部署与排障见 `docs/deploy/influencer-posts-sidecar.md`；架构决策见 `docs/adr/0005-influencer-posts-source-and-cache.md`。


# Karpathy 编码准则

将常见 LLM 编码错误降到最低。与项目特定指令合并使用。

**权衡：** 这些准则偏向谨慎而非速度。简单任务请自行判断。

## 1. 先思考再编码

**不假设。不隐藏困惑。暴露权衡。**

实现之前：
- 明确说明你的假设。不确定时提问。
- 存在多种解读时，全部呈现——不要默默选一个。
- 有更简单的方案时，说出来。该推回就推回。
- 有不清楚的地方，停下来。指出哪里困惑。提问。

## 2. 简单优先

**能解决问题的最少代码。不投机。**

- 不做超出需求的功能。
- 不为一次性代码做抽象。
- 不为未被要求的"灵活性"或"可配置性"写代码。
- 不为不可能的场景写错误处理。
- 如果 50 行能解决，不要写 200 行。

问自己："高级工程师会觉得这过度复杂了吗？" 如果是，简化。

## 3. 精准修改

**只碰必须碰的。只清理自己造成的。**

编辑已有代码时：
- 不"顺手改进"相邻代码、注释或格式。
- 不重构没坏的东西。
- 匹配已有风格，即使你会写得不同。
- 发现无关的死代码，提一下——不要删掉。

你的改动产生孤立代码时：
- 删除你自己的改动使其变得无用的导入/变量/函数。
- 不删除已有的死代码，除非被要求。

检验标准：每一行改动都应该能直接追溯到用户的请求。

## 4. 目标驱动执行

**定义成功标准。循环直到验证通过。**

将任务转化为可验证的目标：
- "添加验证" → "为无效输入写测试，然后让它们通过"
- "修复 bug" → "写一个复现测试，然后让它通过"
- "重构 X" → "确保重构前后测试都通过"

多步任务要列出简要计划：
```
1. [步骤] → 验证：[检查]
2. [步骤] → 验证：[检查]
3. [步骤] → 验证：[检查]
```

强成功标准让你能独立循环。弱标准（"让它跑起来"）需要不断澄清。

---

**这些准则有效的标志：** diff 中不必要的改动更少，因过度复杂而重写的次数更少，澄清问题在实现之前提出而非在犯错之后。
