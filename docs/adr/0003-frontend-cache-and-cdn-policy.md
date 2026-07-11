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
- Actions 只上传正式 WebApp 入口 `wwwroot/index.html` 到又拍云 CDN。
- 部署后自动清除 CDN 对应路径缓存。

`wwwroot/v2/` 已删除且不再使用；`frontend/src/` 不是正式前端源码目录。微信小程序正式源码为 `miniprogram/src/`。

### 前端请求缓存
- `request.ts` 封装的 `uni.request()` 对 GET 请求做 60s 内存缓存 + 请求去重。
- 仪表盘关键数据有 1h localStorage 缓存。

### 主题兼容
- WebApp 和微信小程序只展示“曜石流光”和“雾光银蓝”两个主题。
- 旧主题存储值只在读取时映射到上述两种，不再恢复旧主题 UI；主题切换只改变视觉变量，不触发业务数据重算。
- 浅色主题发布前必须检查手机宽度布局、白字落在浅色背景、状态色和金额文字对比度。

### 图标与界面术语
- WebApp 主操作图标使用固定版本 `https://code.iconify.design/3/3.1.1/iconify.min.js` 提供的 Lucide 集合；发布时它随 `index.html` 引用刷新，不新增独立静态资源路径。
- 微信小程序不依赖运行时图标 CDN；底部导航使用项目内可离线构建的单色图形标记，避免彩色表情在不同系统字体中的视觉差异。
- 资产可见性统一命名为“资产可见、金额遮罩、收益遮罩、隐私保护”。这些设置只影响当前设备的展示，不能改变 OCR、收益、`DailyArchive` 或首页 summary。

## 影响

- 修改 OCR 导入流程时，需要同步考虑缓存清理逻辑。
- 修改缓存 TTL 时，需要权衡实时性和 API 压力。
- CDN 部署后用户可能有短暂的旧版本窗口（CDN 缓存清除延迟）。

## 验收规则

- OCR 导入后刷新首页，立即看到最新确认金额。
- CDN 部署后访问 `guzhicdn.21212121.xyz` 拿到最新资源。
- `/api/fund/*` 响应头包含 `no-cache` 指令。
- WebApp 和小程序在默认/浅色主题下，底部导航、资产显示控件和关键金额文字均可清晰识别。
