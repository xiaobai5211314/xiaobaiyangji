# 前端缓存与 CDN 刷新规则

## 状态

Accepted

## 背景

项目有 4 层缓存：
1. `IMemoryCache` 进程内缓存（行情 30s TTL，市场数据 60s TTL）
2. Redis
3. MySQL `MarketDataCache` 表
4. 前端内存缓存 + localStorage（GET 请求 60s TTL，仪表盘数据 1h TTL）

加上又拍云 CDN 缓存，OCR 导入成功后如果缓存未清理，首页会显示旧数据。

## 决策

### 后端缓存控制
- `/api/fund/*` 和 `/api/stock/*` 路径的响应强制带 `no-cache` 头。
- `index.html` 不缓存（`no-cache`）。
- `wwwroot/` 下其他静态资源设置 7 天 `max-age`。

### OCR 导入后缓存清理
- OCR 导入成功后，必须清除相关的 `IMemoryCache` 和 Redis 缓存。
- 确保下次请求首页时拿到最新确认金额。

### CDN 部署
- 推送 `wwwroot/**` 到 `master`/`gpt-two`/`wechatapp` 触发 GitHub Actions。
- Actions 上传 `index.html` 和 `wwwroot/v2/` 到又拍云 CDN。
- 部署后自动清除 CDN 对应路径缓存。

### 前端请求缓存
- `request.ts` 封装的 `uni.request()` 对 GET 请求做 60s 内存缓存 + 请求去重。
- 仪表盘关键数据有 1h localStorage 缓存。

## 影响

- 修改 OCR 导入流程时，需要同步考虑缓存清理逻辑。
- 修改缓存 TTL 时，需要权衡实时性和 API 压力。
- CDN 部署后用户可能有短暂的旧版本窗口（CDN 缓存清除延迟）。

## 验收规则

- OCR 导入后刷新首页，立即看到最新确认金额。
- CDN 部署后访问 `guzhicdn.21212121.xyz` 拿到最新资源。
- `/api/fund/*` 响应头包含 `no-cache` 指令。
