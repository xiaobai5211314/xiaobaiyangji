# AGENTS.md - 小白养基 Agent 工作规则

本文件给 Codex、OpenAI Agent 以及其他通用代码代理读取。Claude Code 的入口文件是 `CLAUDE.md`，但两者应保持同一套项目规则。

如果本文件与用户当前指令冲突，以用户当前指令为准。如果本文件与代码、`CONTEXT.md` 或 ADR 冲突，先核对事实，不要猜。

## 0. 幻觉防控强制指令

1. 所有涉及事实、数据、案例、平台规则、时间、人物、外部服务行为的内容，必须标注明确可核查来源。
2. 无权威依据或无法确认准确性的内容，直接标注「待核实」。
3. 用户指出内容错误时，立刻核对并修正，禁止抬杠、固守错误观点或编造理由。
4. 不懂就说「我无法准确回答该问题」，不要硬撑。
5. 代码和文档里的“来源”优先使用本仓库文件、实际命令输出、官方文档。不要把第三方网页、生成内容或缓存文件当成项目事实源。

## 1. 项目一句话

“小白养基”是基金和股票投资管理应用。后端是 ASP.NET Core 8.0，数据库和缓存使用 MySQL + Redis；正式微信小程序前端在 `miniprogram/src/`；正式 WebApp 是单文件 `wwwroot/index.html`。

事实来源：`CONTEXT.md`、`小白养基.csproj`、`miniprogram/package.json`、`Program.cs`。

## 2. 每次开始任务先看什么

按任务选择最小必要上下文：

- 通用项目规则：先读本文件。
- 涉及业务口径、金额、收益、OCR、推文：读 `CONTEXT.md`。
- 涉及架构、数据来源、缓存、OCR 契约、推文 sidecar：读 `docs/adr/` 下相关 ADR。
- 涉及部署：读 `.github/workflows/` 和 `docs/deploy/` 中对应文件。
- 涉及小程序：读 `miniprogram/src/` 中目标页面、服务和相关工具。
- 涉及 WebApp：只读和修改 `wwwroot/index.html`。
- 涉及后端：读目标 `Controllers/`、`Services/`、`Models/` 文件和相关测试。

不要一上来全仓库乱改。先定位，后执行。

## 3. 不可踩的硬边界

### 正式前端入口

- 微信小程序正式源码：`miniprogram/src/`。
- WebApp 正式源码：`wwwroot/index.html`。
- `frontend/src/` 不是正式前端入口，除非用户明确要求，否则不要把它当正式页面改。
- `wwwroot/v2/` 已删除且不再使用，不要恢复。

事实来源：`CONTEXT.md`、`docs/adr/0003-frontend-cache-and-cdn-policy.md`、`.github/workflows/upyun-deploy.yml`。

### 财务和收益口径

任何涉及基金金额、收益、收益率、首页 summary、盈亏日历、`DailyArchive` 的修改，都必须先读：

- `CONTEXT.md`
- `docs/adr/0001-fund-accounting-source-of-truth.md`
- `docs/adr/0002-daily-archive-confirmation-policy.md`
- `docs/adr/0004-ocr-import-data-contract.md`

硬规则：

- 蚂蚁 OCR 快照是平台展示事实源。
- 当前金额必须与确认持仓金额拆开：`平台当前金额 = 已确认持仓金额 + 买入待确认金额`。
- `DailyArchive` 正式档案只能写入已确认持仓金额和已确认收益。
- 盘中估值只能临时展示，不得进入正式历史日历。
- 昨日收益只能用于“昨日确认收益”和“盈亏日历”当日收益记录。
- 禁止用 `当前金额 - 昨日收益` 反推确认金额。
- 禁止用 `持有收益 - 昨日收益` 反推持有收益。
- 数字对不上时先查数据来源，不要为了“看起来接近”改公式。

修改收益逻辑后，必须给出至少一个手工验算例子。

### OCR 边界

- 基金 OCR 契约由 `docs/adr/0004-ocr-import-data-contract.md` 约束。
- 股票截图解析由 `StockOcrParserService` 负责。
- 不要把基金 OCR 契约错误绑定到股票 OCR parser。
- P0 字段“当前金额”缺失时，基金截图应视为无效。
- P1 字段缺失时按 ADR 降级，不要写入正式档案。

### 推文 sidecar 和敏感信息

推文功能固定账号为 `@aleabitoreddit`，由 sidecar 抓取并写 JSON 缓存；前端只调用后端接口，不直连 X 或翻译服务。

敏感信息边界：

- 服务器私有环境文件：`/www/wwwroot/小白养基/.secrets/influencer.env`。
- 推文缓存：`/var/lib/xiaobaiyangji/influencer-posts.json`。
- 禁止读取、打印、复制、提交 `.secrets/influencer.env` 内容。
- 禁止输出 `X_COOKIE`、`auth_token`、`ct0`、腾讯云 SecretId、SecretKey 或任何 token/key。
- 检查私有文件存在性时，只能用 `test -f`、`Test-Path`、`stat` 等不显示内容的方式。
- 抓取或翻译失败不得影响基金首页、收益计算、OCR、`DailyArchive`、盈亏日历或首页 summary。

事实来源：`docs/adr/0005-influencer-posts-source-and-cache.md`、`docs/deploy/influencer-posts-sidecar.md`、`.gitignore`、`.dockerignore`。

## 4. 常用命令

### 后端

```powershell
dotnet restore
dotnet build
dotnet run
dotnet publish -c Release -o ./publish
```

本地 MySQL + Redis：

```powershell
docker compose -f docker-compose.local.yml up -d
```

### 后端测试

本仓库的测试是控制台测试项目，不是标准 xUnit/NUnit 项目。常用：

```powershell
dotnet run --project tests/PortfolioAccounting.Tests/PortfolioAccounting.Tests.csproj
dotnet run --project tests/InfluencerPosts.Tests/InfluencerPosts.Tests.csproj
```

### 微信小程序

```powershell
cd miniprogram
npm run typecheck
npm run check:pages-order
npm run build:mp-weixin
```

事实来源：`miniprogram/package.json`。

## 5. 部署和分支

- 当前本地仓库写作时位于 `master`，并且 `master...origin/master [ahead 1]`。这是当次命令输出，不保证未来不变，执行前必须重新核对。
- GitHub Actions 后端部署工作流在 `master`、`gpt-two`、`wechatapp` 的相关路径 push 时触发。
- 又拍云 WebApp 部署工作流在 `master`、`gpt-two`、`wechatapp` 的 `wwwroot/**` 变化时触发。
- 删除本地或远程分支前，先确认分支是否已合并，尤其不要误删默认分支。
- 分支清理说明见 `docs/agents/git-keep-single-main-branch.md`。

事实来源：`git status --short --branch`、`.github/workflows/deploy-backend.yml`、`.github/workflows/upyun-deploy.yml`。

## 6. 工作方式

- 先说计划，再做有风险的修改。
- 保持修改小而准，不顺手重构。
- 用户说“只 review”“不要改业务逻辑”“不要读/打印密钥”时，逐字遵守。
- 使用 `rg` / `rg --files` 搜索。
- 本地文件编辑优先用补丁工具，不用临时脚本胡乱改文件。
- 不要用 `git reset --hard`、`git checkout --` 等破坏性命令，除非用户明确要求。
- 不要自动提交、推送、部署，除非用户明确要求。
- Windows PowerShell 环境下执行命令时，注意路径包含中文和空格，优先使用 `-LiteralPath`。
- 遇到不确定业务规则时停止并问，不要凭感觉补产品逻辑。

## 7. 验证标准

根据改动范围选择验证：

- C# 后端改动：至少运行 `dotnet build`；涉及收益/归档时运行对应控制台测试并给手工验算。
- 小程序改动：运行 `npm run typecheck` 和 `npm run check:pages-order`，必要时 `npm run build:mp-weixin`。
- WebApp 改动：检查 `wwwroot/index.html` 中相关交互，必要时做浏览器或静态检查。
- 推文 sidecar 改动：运行相关 Python/控制台测试，检查缓存和 API 行为，但不输出密钥或 cookie。
- 文档改动：检查链接、路径、命令是否来自当前仓库事实；不确定处标「待核实」。

如果无法运行验证，最终回复必须说明原因和未验证风险。

## 8. 回答用户时

- 先给结论，再给必要证据。
- 涉及事实时附来源：文件路径、命令输出、官方文档或“待核实”。
- 区分“我已验证”“我从文件推断”“待核实”。
- 如果用户纠错，马上复核并更新，不争辩。
