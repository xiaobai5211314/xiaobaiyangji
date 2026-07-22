# CHANGELOG

## 2026-07-22

- 资讯(7x24快讯)后端定时预热，闭环「打开即热」：在 `SectorRadarWarmupService` 同一预热周期里顺带自 ping `GET {SelfBaseUrl}/api/fund/news?mode=global&force=true`，复用 `GetNews` 的 `NewsV3` 多级缓存重建逻辑；仅预热公开 global 模式(username 空)，holding 模式依赖 username 由前端 30min localStorage 兜底。前置：因 news 原不在 JWT 免鉴白名单，自 ping 无 token 会被中间件返回 401，故在 `Program.cs` 白名单加 `path.StartsWith("/api/fund/news")`（与 sectors/global-indices 并列，对前端零副作用——控制器无 [Authorize]，带不带 token 返回一致）。稳态由 6h 内存 stale 兜底不撞 503，单次失败不崩溃下周期重试。主理人逐行核对 diff + 复跑 build 0 错误，QA 静态复核 NoOne 通过。（`Program.cs`、`Services/SectorRadarWarmupService.cs`）

- 前端缓存增强（板块 localStorage 顺延 + 资讯页静默刷新）：纯前端 `wwwroot/index.html` 改动，零后端。板块：`SECTOR_CACHE_DURATION` 180000→600000（内存复用 3min→10min）、`sector_fast_cache_v3` 磁盘快取窗口 30min→1h，刷新/重开更长窗口零等待。资讯：新增 `fetchNews` 的 `refresh` 选项，开页路径改为 `fetchNews(newsMode, false, {background:true, refresh:true})`——先瞬间显示本地旧数据（内存 ≤10min / 磁盘 ≤30min，`NEWS_CACHE_DURATION` 2min→10min、磁盘 15min→30min），再于后台静默拉新、就绪即更新，全程不转圈；手动 🔄(force) 与切模式/开面板路径保持原前台行为，`warmNewsCache()` 仍空操作保首屏速度。QA 静态复核 NoOne 通过。（`wwwroot/index.html`）

- 大盘指数（全球大盘雷达）定时预热 + 资讯面板移动端布局修复：
  - 大盘指数预热：复用 `SectorRadarWarmupService`，在同一预热周期里顺带自 ping `GET {SelfBaseUrl}/api/fund/global-indices?force=true`，100% 复用该端点的 Redis(`api:fund:global-indices:v1`)+DB(`global_indices_1y_v2`) 多级缓存重建逻辑，公开接口无需 token、`force=true` 确保真正重建；与板块雷达一并启动即预热 + 周期预热，打开即热、零等待。复用同一命名 HttpClient `SectorWarmup`、同款容错（200 刷新/503 跳过/异常不崩溃）。`Services/SectorRadarWarmupService.cs` 改动、零新文件。
  - 资讯移动端遮挡修复：根因是单文件含 light/dark 两套主题各自 media query 块，原 light 768px 块缺 `.news-page-layout` 单栏覆盖、且两套 900px 块均缺失，导致 light 主题手机端及横屏/宽屏手机(769–900px)下右侧「资讯影响时间线」被双栏 Grid(`minmax(0,1fr) minmax(280px,.42fr)`)挤压遮挡。改动：light 900px/768px 及 dark 900px 三处 media query 补 `.news-page-layout { grid-template-columns: 1fr; }`，基础规则补 `overflow:hidden` 防溢出；现两套主题 ≤900px 与 ≤768px 均折叠单栏，右栏不再遮挡。`dotnet build` 0 错误，回归测试全 PASS。（`Services/SectorRadarWarmupService.cs`、`wwwroot/index.html`）

- 新增板块基金雷达「定时预热」托管服务，让板块页打开即最新、零等待（像官网一样快）：根因是板块数据此前只在「有人打开且缓存过期」时才重建，过期后下一用户要等全市场扫描。新增 `SectorRadarWarmupService`（BackgroundService）按交易时段（约 2 分钟）/非交易时段（约 30 分钟）周期自 ping 本进程公开端点 `GET {SelfBaseUrl}/api/fund/sectors?force=true`，100% 复用 `GetSectors` 的构建 + Redis/内存/DB 缓存写回逻辑，零改动控制器；`force=true` 确保真正重建而非命中缓存。周期判定 `SectorRadarScheduleHelper.GetWarmupInterval` 与 `FundController.GetExternalDataFreshTtl` 对齐（交易时段 2min、其余 30min），抽成可注入时间的纯函数便于单测。安全垫：稳态下由 6 小时内存 stale（`FundSectorRadarV7_Stale`）兜底，不会撞 503；503 "refreshing" 视为并发刷新中、本周期跳过；单次失败不崩溃、下周期重试。监听地址经 nginx `proxy_pass http://127.0.0.1:7084` 确认生产 Kestrel 即 HTTP 127.0.0.1:7084，故 `SelfBaseUrl` 缺省 `http://127.0.0.1:7084`、`appsettings(.Production).json` 显式写入；该端点已在 JWT 免鉴白名单，自 ping 无需 token。`Program.cs` 注册命名 HttpClient `SectorWarmup`（Timeout 180s）+ `AddHostedService<SectorRadarWarmupService>()`。`dotnet build` 0 错误，新增回归测试（交易/非交易/周末/边界 11:35/12:54）全 PASS。（`Services/SectorRadarWarmupService.cs`、`Services/SectorRadarScheduleHelper.cs`、`Program.cs`、`appsettings.json`、`appsettings.Production.json`、`tests/PortfolioAccounting.Tests/Program.cs`）

- 修正 2026-07-20 盈亏日历 TOTAL 收益率显示 −4.32% 的数据损坏：该日 017968（华富科技动能混合C）的归档行被错误写入了 TOTAL 汇总值（assets/profit/rate 与 TOTAL 行字节级相同），导致 TOTAL 行也展示为单只基金的错误数值，日历整体显示为 −4.32%。根因：07-20 当天该用户有一笔约 ¥3,379 的 017968 加仓，而系统当时缺少 07-20 官方净值（外部净值源未返回），结算回退写出损坏数据。以东方财富真实净值（07-17=1.3021、07-20=1.2459、07-21=1.3051，07-20→07-21 的 +4.75% 与未损坏的 07-21 归档行 dailyRate 完全吻合，验证来源可靠）结合系统 07-21 结算隐含的 07-20 基数（43752.61）重算：017968 07-20 → assets 43752.61、dailyProfit −1821.18、dailyRate −4.00%（含加仓成本基数口径）；TOTAL 07-20 → assets 99725.07、dailyProfit −354.86、dailyRate −0.35%。其余 5 只基金行未改动。经 `save-archive` 接口写入（source=client-preview），与 07-21 真实归档（assets 102485.66、dailyProfit 2760.6）勾稽一致（99725.07 + 2760.6 ≈ 102485.66）。说明：属历史数据修正，无代码变更；`get-archives` 对该日现返回正确 −0.35%。

- 新增 DailyArchive 不变式守卫，从代码层面根治「单基金行被错写成 TOTAL 汇总值」的数据损坏（即 07-20 那类 −4.32% 假收益率的根因，避免再发生）：新增 `DailyArchiveService.SanitizeArchiveRows` —— 多基金组合下，若某基金行 `Assets` 约等于 TOTAL `Assets`（抄写指纹），判定为损坏并剔除，并用剩余有效基金重算 TOTAL（`source=guard-recomputed-total`），保证 TOTAL.Assets 恒等于 Σ基金Assets，绝不落库伪造收益率；单基金组合视为合法不触发。守卫已接入两个落库入口：`BuildArchiveRows`（结算路径，`Services/DailySettlementService.cs`）与 `SaveArchive`（客户端预览路径，`Controllers/FundController.cs`）。新增回归测试（`tests/PortfolioAccounting.Tests`）：多基金抄写指纹→剔除 1 行并正确重算 TOTAL；单基金组合→不误伤。`dotnet build` 0 错误 0 警告，回归测试全 PASS。另：对生产库该账号全历史（537 条）扫描，除已修正的 07-20 外无同类损坏。

- 新增 DailyArchive 数据损坏不变式守卫，从代码层面根治「单基金行被错写成 TOTAL 汇总值」类问题（即 07-20 −4.32% 损坏的根因类别，杜绝再发生）：新增 `DailyArchiveService.SanitizeArchiveRows`——多基金组合下，若某基金行 `Assets` ≈ TOTAL `Assets`（抄写指纹），判定为损坏并剔除该坏行，并用剩余有效基金重算 TOTAL 使其 = Σ基金Assets（保持内部一致，绝不落库伪造的 −4.32% 之类）；单基金组合（仅 1 只基金）视为合法、不触发守卫。该守卫在 `DailySettlementService.BuildArchiveRows`（结算落库前）与 `FundController.SaveArchive`（客户端预览落库前）两处写入路径统一接入。回归测试新增用例 A（6 基金含损坏 TOTAL → 剔除 1 行、TOTAL 重算为剩余 5 只之和、source=guard-recomputed-total）与用例 B（单基金组合不触发、不误伤）。对生产库 dabai521 全历史 537 条归档做扫描，除已修复的 07-20 外未发现其他同类损坏。（`Services/DailyArchiveService.cs`、`Services/DailySettlementService.cs`、`Controllers/FundController.cs`、`tests/PortfolioAccounting.Tests/Program.cs`）

- 安全加固（堵漏网写入路径 + 净值缺失显式标记 + 生产 JWT 密钥）：
  - 将不变式守卫 `SanitizeArchiveRows` 下沉到 `Controllers/FundController.cs` 的 `UpsertDailyArchivesAsync`（所有写入的汇聚点），覆盖上次漏网的 `BuildArchiveRowsFromCurrentHoldings` 前台结算路径；`SettleDaily`/`SaveArchive`/OCR 归档经此汇聚点自动净化，零漏网。
  - `settle-daily`（`BuildArchiveRowsFromCurrentHoldings` 与 `DailySettlementService.BuildArchiveRows`）在基金官方净值缺失时写 `Source="nav-missing"` 标记行（不计入 TOTAL），前端当日明细显示「净值缺失」，不再静默跳过导致该基金当日记录消失。
  - 配套：`SanitizeArchiveRows` 损坏检测条件加 `TOTAL.Assets > 0.01` 守护（避免全部净值缺失 TOTAL=0 时误删 nav-missing 行）；`DailyArchiveService.UpsertAsync` 允许 `nav-missing` 显式标记行落库（仍拦截其他假 0 数据）。
  - `appsettings.Production.json` 新增 `Auth:TokenSecret` 真实强随机密钥，消除占位符漏洞（旧 token 部署后失效，请重新登录）。
  - 回归测试新增用例 C（守卫经写入路径生效：多基金一只损坏→剔除+重算 TOTAL）、用例 D/D2（nav-missing 不被误删、TOTAL=0 全缺失不误删）。`dotnet build` 0 错误，测试全 PASS。
  - 注意：本批次改动触发 CI 后端+前端部署，上线后守卫在生产生效；生产 JWT 密钥明文进入 git 历史（项目既有 Production 配置随仓库部署模式），若需更严格建议改用环境变量注入并滚动密钥。

## 2026-07-21（5）

- 板块「方向」列改为显示真实连续涨跌天数（连涨 N 天 / 连跌 N 天）。此前 `SectorSummaryDto.StreakDays` 只存 ±1/0 的当日方向标记，前端板块行情列表（L5510）只显示"连涨/连跌"纯文字、雷达弹窗（L5951）虽显示"N天"但数据错（恒为 0/1）。根因：`GetSectors` 是公开接口（前端不带 token、Redis 共享缓存），无法按 per-user 持仓反推（原方案 B 不可行），故采用公开口径——基于东方财富基金历史净值计算所有用户一致的连续天数。新增 `SectorNavDay` record、`FetchFundNavHistoryAsync`（调 `api.fund.eastmoney.com/f10/lsjz`，带 `Referer: https://fundf10.eastmoney.com/`）、`FetchFundNavHistoryCachedAsync`（IMemoryCache 缓存 8h + `SemaphoreSlim(12)` + 4s 超时）、`ComputeSectorStreakDays`（按日期对齐求每日均值、从最新日往前数连续同号天数：连涨为正、连跌为负、≈0 或历史<2 天为 0）；`BuildSectorRadarPayloadAsync` 汇总循环替换原 ±1/0 为真实计算，每板块限前 20 只匹配基金、跨板块基金去重。前端 L5510 由纯文字改为与 L5951 完全一致的 `{{ item.streakDays>0?'+':'' }}{{ item.streakDays || 0 }}天`，两处风格统一。公开接口/鉴权、`Rate` 字段（当日涨跌幅）、Redis+内存多级缓存均不变；历史净值接口失败/限流仅导致该列显示 0 天，不阻塞板块列表主流程。`dotnet build` 0 错误 0 警告，QA 验证 NoOne 全 PASS。（`Controllers/FundController.cs`、`wwwroot/index.html`）

## 2026-07-21（4）

- 修复浅色主题下「板块基金」弹窗文字看不清：弹窗标题 `#38bdf8`、盘中估值时间 `live-estimate #38bdf8`、最新净值时间 `latest-nav #f59e0b`、表头/行分隔线 `#334155`/`rgba(255,255,255,...)`、「板块基金雷达」弹窗背景 `#1e293b` 等多处硬编码亮色/深色，在浅色主题白底上对比度极低（标题与估值时间尤其明显）。根因是 4b74842 那次只覆盖了 `#f59e0b` 一处、漏掉 `#38bdf8`，导致标题与估值时间仍走浅青色。统一改用主题 token：`--accent-blue`（浅色=#007aff 深蓝）、`--td-color-info`（=#0066cc）、`--td-color-warning`（=#855600）、`--card-border`、`--card-bg`，浅色主题下全部解析为深/可见值，对比度 ≥ 4:1；深色主题（曜石流光）下这些 token 仍解析为浅色，视觉与修改前一致无损。仅改板块基金弹窗内联样式取值，未触动 JS 逻辑。（`wwwroot/index.html`）

## 2026-07-21（3）

- 修复首页「今日收益率」两处显示不一致：顶部 Summary 卡片的「今日确认收益率」与日总收益曲线区的「今日收益率」数值对不上（差约 2.1 个百分点）。根因是 `GetTodayPerformanceCurveAsync` 今日收益率的分母 `totalPrincipal` 原来优先使用 `DailyArchive TOTAL.Assets`（可能含待确认买入），与 Summary 采用的确认持仓口径不一致。改为统一使用 `quotedPrincipal`（由各基金确认持仓金额之和构成，已排除待确认买入），符合 ADR 0001「收益率分母必须使用确认持仓金额」规则。同步删除该方法内仅服务于该 fallback 分支、改动后已无引用的 `DailyArchive TOTAL` 查询块。仅影响 today 周期，历史周期（7d/1m/3m/1y）走独立路径不受影响。（`Controllers/FundController.cs`）

## 2026-07-21（2）

- 修复场外涨幅榜「网络错误」：`fetchMarketRanking` 原用裸 `fetch('/api/fund/market-ranking?...')` 相对路径，从 CDN 域名 `guzhicdn.21212121.xyz` 打开时请求打到 CDN 自身（无后端）→ 404 → 解析失败 → 一直转圈/报错。改为 `apiFetch(url)`（自动拼 `API_BASE='https://guzhi.21212121.xyz'`），CDN 下也能正确请求源站。（`wwwroot/index.html`）
- 修复浅色主题文字对比度：`.radar-badge` / `.performance-status.win` / `.metric-up` / `.performance-status.loss` / `.metric-down` 等 CSS 类硬编码 `#ff4d4f` / `#10b981`，而原浅色主题覆盖规则只匹配内联 `style=` 属性（匹配不到 CSS 类），导致白底下红绿色文字发虚。新增 `:root[data-theme="light"]` 类选择器覆盖，使用深色值 `#b91c2b` / `#006b46`，仅浅色主题生效。（`wwwroot/index.html`）
- 恢复完整字体回退链：`--font-display` / `--font-body` 及 body 的 `font-family` 补回 `-apple-system, BlinkMacSystemFont, "Segoe UI"`，兼容 Mac/iOS 系统字体（Windows 上自动忽略，无副作用）。（`wwwroot/index.html`）
- 说明：涨幅榜 `dailyRate` 来自东方财富全市场数据，与持仓页 `todayRate`（后端按持仓计算）数据来源不同属正常现象，并非 bug；CDN 旧版 HTML 缓存可能导致 guzhicdn 与 guzhi 显示不一致，本次推送前端会触发又拍云缓存刷新。

## 2026-07-21

- 修复今日收益全为 0：天天基金估值接口 `fundgz.1234567.com.cn` 已永久下线（301 重定向到 `fund.eastmoney.com/notfound.html`），导致 `FundScraperService` 抓不到估值数据。改用新浪基金接口 `hq.sinajs.cn/list=fu_{code}` 作为数据源。
- 新增 `SinaFundQuote` 记录类型与 `FundScraperService.TryFetchSinaQuoteAsync` 静态方法，统一处理新浪 GB18030 编码响应与字段解析（基金名、估算涨跌幅、估算时间）。
- `FundGz` HttpClient 增加 `Referer: https://finance.sina.com.cn` 请求头，满足新浪接口的防盗链要求。
- 同步替换 `FundController` 三处天天基金调用：`AddFund`（添加基金获取基金名）、`AutoSettle`（自动清算获取估算涨跌幅）、`FetchFundQuoteAsync`（主题基金估值展示）。
- 字段映射：新浪响应 `[0]基金名 [1]时间 [2]估算净值 [6]估算涨跌幅% [7]日期` 对应原 `name / gztime / gszzl`。
- 收紧主力资金分类：`IsIndustryCapitalFlowRow` 原逻辑"未在白名单的默认通过"导致"保险II"/"XX服务"等非标准行业名混入。改为默认拒绝，新增申万二级"II"/"Ⅲ"后缀去尾匹配白名单逻辑（如"保险II"→"保险"匹配白名单）。
- 新增场外基金全市场涨跌幅排行榜 API `GET /api/fund/market-ranking`：支持 `order=desc|asc`（升降序）、`type=all|equity|mixed|bond|index|qdii|lof|fof`（基金类型）、`limit`（每页数量，10-200）、`page`（页码）。数据源东方财富 `rankhandler.aspx`，返回基金代码、名称、单位净值、当日涨跌幅、近1周/1月/3月/6月/1年/今年以来涨幅。
- 补充主力资金全角罗马数字处理：东财行业名"保险Ⅱ"使用全角"Ⅱ"（U+2161）而非半角"II"，`IsIndustryCapitalFlowRow` 新增全角"Ⅱ/Ⅲ"去尾匹配逻辑，确保"保险Ⅱ"等申万二级行业也能正确归入行业。
- `market-ranking` 接口加入认证白名单（`Program.cs`），无需登录即可访问，与 `capital-flow` 保持一致。
- 前端板块行情页新增「场外涨幅榜」模式：在板块基金雷达页「领涨/领跌/全部主题」旁增加「场外涨幅榜」按钮，直接列出全市场场外基金涨跌幅（不按板块分类，避免混合基因无行业关键词被漏掉）；支持类型筛选（全部/股票/混合/债券/指数/QDII/LOF/FOF）、当日涨幅降序/升序切换、基金名/代码搜索。数据来源东方财富全市场榜单，覆盖市场上全部场外基金，不再依赖板块分类池。

## 2026-07-17

- 修复手工加仓跨过预计确认日后从页面消失：`PendingTradeStatus` 现在独立控制交易生命周期，`PendingConfirmDate` / `FirstProfitDate` 只控制收益参与时间；实际结转、OCR 明确归零或取消前，待确认金额继续显示并阻止重复登记。
- 修复有效历史档案覆盖首页时把待确认金额强制清零：当前展示恢复为 `档案确认金额 + 仍在途买入金额`，2026-07-16 的确认金额 `99060.50` 元加待确认 `5000.00` 元继续显示为 `104060.50` 元。
- 修复 WebApp 加仓按钮看似无响应：后端所有失败响应统一返回 JSON，前端兼容 JSON/文本错误，弹窗内持续展示失败原因、已有待确认记录和提交状态，并阻止重复点击。
- 新增首个收益日防重复回归：待确认交易可从预计首个收益日起参加收益，但当前展示金额只加一次本金；正式归档和确认成本仍排除未实际结转的本金。

## 2026-07-16

- 修复待确认买入导致持有收益偏差：当前展示金额仍包含待确认本金，但单基金持有收益、首页汇总和 `DailyArchive.TotalProfit` 统一改用确认持仓金额与确认成本；未确认 `purchase_amount` 成本只扣除待确认本金一次，平台/OCR 已确认成本不重复扣除。
- 修复 WebApp 与微信小程序“回本所需涨幅”误用包含待确认资金的展示金额作分母；优先使用后端确认口径，前端回退也只使用确认持仓金额。
- 新增 2026-07-16 生产形状回归：确认持仓 `99060.50` 元、待确认买入 `5000.00` 元、账户总金额 `104060.50` 元、持有收益 `-11643.70` 元、持有收益率 `-10.52%`。

## 2026-07-15

- 修复晚间官方净值已更新但 WebApp 仍停留在日内旧 OCR 的问题：可靠份额下，`份额 × 官方净值` 与滚动金额相差不超过 `0.01` 元时继续保留平台两位显示；差额超过 `0.01` 元时判定滚动基数异常并自动回正，定时清算、手动净值同步和首页接口使用同一口径。
- 修复官方净值确认后持有收益仍冻结在旧 OCR 的问题：正式净值出现后按“当前金额 - 成本 + 已实现收益 + 平台校准差”重新滚动持有收益；2026-07-15 回归样本对齐蚂蚁基金总金额 `99035.59`、昨日收益 `593.69`、持有收益 `-11668.61`。
- 扩展主题基金雷达：统一主题目录并新增航天航空、卫星产业、低空经济等独立主题；保留东方财富基金类型，明细支持完整分页、搜索以及指数/主动基金筛选，主动基金包含混合型和股票型产品。
- 修正主题基金涨幅口径：仅把北京时间当日 `fundgz` 标记为盘中估值，回退数据明确显示最近净值涨跌或历史估值参考；主题均值采用指数与主动基金平衡抽样，避免 ETF 排名偏置。
- 修正资金板块术语：`/api/fund/capital-flow` 及两端界面统一为“股票行业主力资金”，不再把行业资金表述为概念资金或单只场外基金申赎。
- 修复未卖出基金被误标“已落袋”：资产详情 OCR 的平台持有收益口径差改存 `PlatformHoldingAdjustment`，`RealizedProfit` 只由确认卖出更新；无新鲜 OCR 时的持有收益仍按“当前金额 - 成本 + 已实现收益 + 平台校准差”滚动。
- 新增回归覆盖：未卖出基金保持 `RealizedProfit=0`、真实部分卖出独立累计已实现收益、全部卖出时清除持有收益校准差。

## 2026-07-13

- 核验并锁定交易日 15:00 前加仓的待确认口径：待确认买入可进入当前展示金额，但不进入当天收益、收益率分母、有效份额或 `DailyArchive`；新增匿名化端到端账务回归验算。
- 补充普通开放式基金 T 日截止时间及 T+1 预计确认的权威来源；特殊基金和销售平台仍以实际确认结果为准。

## 2026-07-11

- 修复微信内置 WebView 中资产显示弹窗切换后不关闭、选中态滞后的问题：入口和弹窗选项改用静态 Lucide SVG，避免 Iconify 在运行时改写 Vue 管理的动态节点；弹窗保留稳定 DOM，并增加同步关闭遮罩的兼容兜底。
- 修复 CDN 与直连站点可能版本分叉：又拍云工作流的刷新失败不再忽略，发布后会轮询普通 CDN 地址并校验页面 SHA-256；未同步即标记部署失败。
- 修复 WebApp 持仓骨架屏可能长期停留：`/api/fund/today` 增加 15 秒超时，失败时退出加载态、保留已有数据并展示可见的重试入口；资产显示切换改为立即本机生效并提示所选模式。
- 修复 PC 端导航栏位置异常：ChatGPT 优化时将 PC 端 `.bottom-tabbar` 从 `position: fixed; bottom: 20px;` 误改为 `position: sticky; top: 12px;`，导致导航栏跑到页面顶部。恢复为底部固定定位，同时恢复 `#app` 的 `padding-bottom: 96px` 防止内容被导航栏遮挡。
- 修复 Iconify 图标可能干扰按钮点击：Iconify 脚本在按钮内部 span 中插入的 SVG 元素可能拦截点击事件，导致页面"卡死"、不能切换。添加 `pointer-events: none` 规则确保 SVG 不阻断父元素的点击事件。
- 修复弹窗无法关闭导致页面卡死：`historyModal`、`archiveModal`、`editForm`、`addModal`、`reduceModal` 的遮罩层缺少 `@click.self` 关闭逻辑，用户点击遮罩无法关闭弹窗。为所有未关闭遮罩的弹窗添加点击遮罩关闭功能。
- 恢复 `body` 的 `overflow-x: hidden`，防止 PC 端出现不必要的水平滚动条。

## 2026-07-11（ChatGPT 优化）

- 修复账户总金额与"市场状态"互相遮挡：状态改为账户卡片顶部的紧凑状态条，金额独占完整宽度；WebApp 各视口保留底部固定导航，并扩大页面底部安全滚动空间。
- 完成两端布局防裁切整理：WebApp 统一弹窗视口边界、工具栏换行、底栏 `border-box` 和标签收缩；小程序页头、首页操作区、日历工具栏、弹窗与底栏标签支持窄屏及系统字体放大。
- 新增 `docs/reviews/2026-07-11-responsive-layout-audit.md`，记录 5 个 WebApp 页面、12 类弹窗、4 档视口和小程序正式构建的验收结果。
- 界面专业化整理：WebApp 主导航、截图导入、资产摘要和账户操作改用统一 Lucide 线性图标；微信小程序底部导航改为单色图形标记，移除彩色表情图标。
- 统一资产显示术语：两端将“睁眼/闭眼模式”改为“资产可见、金额遮罩、收益遮罩、隐私保护”，并明确每种模式遮罩的字段范围；WebApp 同步重做为可访问的选择控件。
- 统一导航与业务文案：底部导航使用“持仓、行情、资讯、复盘、观点”，小程序将“昨日总市值”“盈利/亏损 TOP”“总持仓”调整为更准确的“昨日持仓基准”“收益领先/回撤关注”“当前持仓”；仅改展示文案，不改变金额或收益计算。
- 两个主题的设置文案改为“曜石流光：深色行情工作台”和“雾光银蓝：清晰的系统信息层级”。
- 修正晚间 OCR 金额口径：只要 `OcrSnapshotDate` 属于北京时间当前自然日，盘中估值和官方净值分支都直接使用 OCR 平台当前金额，不再重复叠加当日收益；跨过 00:00 后旧 OCR 自动退出当前展示优先级。
- 修正跨自然日收益状态：周末、节假日和交易日 09:30 前只承接上一交易日金额与累计收益，今日收益、今日收益率、单基金今日涨跌幅及今日收益曲线归零，不再把上一交易日 `DailyArchive` 标成 `official_today`。
- 修正浅色主题可读性：WebApp 清除遗留深色半透明控件，小程序根文本改用主题变量，并统一浅色主题的正文、收益红绿、警告和状态色；两端仍只保留“曜石流光”和“雾光银蓝”两个主题。
- 新增 OCR 金额、待确认金额、周末、工作日 09:29/09:30 和自然日归零回归测试；同步重建微信小程序正式产物。

## 2026-07-10

- 修复晚间 OCR 导入后总持仓金额收益重复计算：`estimate_today`（盘中估值状态）分支中，有新鲜 OCR 快照时不再把盘中估算收益叠加到 OCR 当前金额上。晚间 OCR 截图的当前金额已是平台当天最终值（含当日收益），叠加盘中估算收益会导致收益被计算两遍。盘中估算收益仍正常计算，仅用于"今日盘中估算"临时展示，不进入账户总金额。
- 性能审查修复（见 `docs/reviews/2026-07-10-performance-and-correctness-audit.md`）：移除 `MarketCacheService` 热读取路径的缓存命中统计写库；`FundScraperService` 只抓取有效持仓并按基金代码去重；`NavSettlementService` 只结算仍有持仓的基金；删除 `settle-nightly` 和 `auto-settle` 旧估值清算入口；删除基金级资金流接口和两端展示。

## 2026-07-01

- 基金 OCR 导入规则补充：常规蚂蚁持仓截图不含份额/成本价时，系统自动用官方净值反推份额，并用 `当前金额 - 持有收益` 校准成本金额。
- 基金资产详情页 OCR 增强：识别 `持仓成本价`、`持有份额` 和详情页收益日期；详情页给出精确字段时按 `成本价 × 份额` 校准成本金额。该版本曾用 `RealizedProfit` 承接平台持有收益口径差，已于 2026-07-15 改为独立的 `PlatformHoldingAdjustment`。资产详情页逐只导入时，只有同日所有当前持仓都确认后才生成 `TOTAL` 汇总。
- 首页和盈亏日历口径补充：同日有新鲜蚂蚁 OCR 时，账户金额、昨日收益、持有收益和 `DailyArchive` 归档继续以 OCR 金额为准，自动反推份额不能覆盖蚂蚁截图金额。
- `DailyArchive` 写入说明补充：入库金额和收益率统一标准化为 2 位小数，盈亏日历展示以数据库归档行为准。
- 修复官方净值清算后持仓金额可能比蚂蚁基金多 0.01 级别的问题：展示金额按上一有效金额加已确认收益滚动，不再被 `份额 × 今日净值` 的分位误差覆盖。

## 2026-06-27

- 新增 JWT 认证：所有 `/api/` 端点必须携带有效 token，未登录返回 401。
- `TokenService`：HMAC-SHA256 签名，token 有效期 720 小时。
- 登录/注册/微信登录接口返回 `token` 字段。
- 小程序：`request.ts` 自动附加 `Authorization: Bearer` header，登录后 token 持久化到本地存储。
- WebApp：新增 `LoginView.vue` 登录页，路由导航守卫，未登录自动跳转；401 时清除本地 token 并跳转登录。
- 公开端点（不受认证保护）：`/api/auth/login`、`/api/auth/register`、`/api/auth/wechat-login`、`/api/health`。
- 修正服务器 `DailyArchives` 数据库 2026 年 6 月盈亏数据（用户 dabai521），对齐蚂蚁基金标准：06-03、06-17、06-23 的 `DailyProfit` 已修正，月累计从 -10931.58 更正为 -10934.17。
- 小程序盈亏日历 `.day-profit` 样式修复：去掉 `transform: scale(0.86)`，缩小字号至 14rpx，添加 `overflow: visible`，防止圆圈内金额被截断。
- `.gitignore` 新增 `.codegraph/`。

## 2026-06-26

- 修复基金首页总持仓在官方净值确认后仍停留在旧 OCR 金额的问题。
- 官方净值结算后，单基金 `HoldAmount` 会随确认收益滚动；无新鲜 OCR 时，首页账户总金额按单基金当前市值合计展示。
- 收盘后当天已有 `DailyArchive` 档案时，首页优先使用档案金额，避免份额乘净值造成 0.01 元级别的四舍五入偏差。
- `DailyArchive` 的 `official-nav-pending` 档案改为使用滚动后的当前金额，并按当前金额与成本重算持有收益，避免多日未 OCR 时收益累计偏离。
- 更新收益/OCR 口径文档：OCR 是最高优先校准源，不是每天唯一的当前金额来源。
