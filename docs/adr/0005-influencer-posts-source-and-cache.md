# 白毛股神推文数据源、缓存与翻译边界

## 状态

Accepted

## 背景

项目需要在 WebApp 和微信小程序中展示 `@aleabitoreddit` 的公开推文，并保留中文翻译。

用户提供的现场结果显示，当前账号调用 X 官方 API 曾返回 `402 CreditsDepleted`。该结果未在本次任务中使用账号凭据重新验证，标记为**待核实**，不能扩展为所有账号或所有时间均不可用的结论。

## 决策

1. 当前个人自用链路使用 `twscrape` 和已登录 cookie 抓取固定账号，不由前端直接访问 X。
2. cookie 只允许保存在服务器 `/www/wwwroot/小白养基/.secrets/influencer.env`，不得进入 appsettings、日志、文档、Git 或前端。
3. sidecar 把推文和已抓取回复写入 `/var/lib/xiaobaiyangji/influencer-posts.json`，不为该功能新增数据库表或 migration。
4. ASP.NET Core 的 `GET /api/influencer-posts/latest` 只读 JSON 缓存，按 `createdAt` 降序并最多返回 20 条。
5. 前端只调用后端接口；正式入口为 `wwwroot/index.html` 和 `miniprogram/src/`。
6. 翻译在 sidecar 完成。推文和回复的缓存字段均使用 `translatedText`、`translatedAt`、`translationProvider`、`translationStatus`。
7. 已有成功译文在原文未变化且启用翻译缓存时直接复用；父推文译文命中缓存时，未翻译的回复仍继续翻译；翻译未配置或失败时保留英文原文。
8. WebApp 的“回”和“查看回复”进入详情视图，详情视图展示推文与回复的译文、原文和原文链接。
9. 抓取失败不覆盖旧缓存；翻译失败不阻止新推文缓存落盘。

## 隔离边界

该功能不得依赖或修改基金收益计算、OCR 导入、`DailyArchive`、盈亏日历、首页 summary、持仓金额和收益率公式。推文缓存缺失或不可读时，接口返回空状态，基金首页继续独立工作。

## 依据

- 抓取与原子缓存实现：`tools/x_tweets_fetcher/fetch_posts.py`
- 后端只读实现：`Services/InfluencerPostsCacheService.cs`
- API 边界：`Controllers/InfluencerPostsController.cs`
- 正式前端入口约束：`CLAUDE.md`、`CONTEXT.md`
- `402 CreditsDepleted`：用户提供的账号现场结果，**待核实**

## 影响

- cookie 过期时需要用户重新手动登录 X 并在服务器本地更新私有环境文件。
- JSON 缓存是页面可用性的降级边界；外部抓取和翻译服务不进入页面请求链路。
- 若未来改用其他数据源或数据库，需要新增 ADR，不得直接改变本决策边界。
