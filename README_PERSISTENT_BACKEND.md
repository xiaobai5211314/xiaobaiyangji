# 估值助手：持久化闭环版后端

## 本包目标

把前端体验版里的以下能力从 localStorage 升级为数据库持久化：

1. 估值误差校准 / 盘中估值修正
2. 隐私模式、盈亏页偏好、回本模拟输入金额等 UI 状态
3. 首页战报、可信度、回本榜、赛道暴露等产品化面板快照
4. OCR 纠错表安全迁移
5. OCR 错误真实原因返回

## 文件放置

把本包中的文件复制到项目对应目录：

```text
Controllers/ProductInsightsController.cs
Controllers/ValuationCalibrationController.cs
Controllers/UserUiStateController.cs
Controllers/InsightSnapshotsController.cs
Models/FundValuationEstimate.cs
Models/FundValuationCalibration.cs
Models/UserUiState.cs
Models/UserInsightSnapshot.cs
Migrations/202605030002_AddOcrCorrectionsSafetyNet.cs
Migrations/202605030003_AddPersistentUiAndValuationCalibration.cs
Services/BaiduOcrService.cs
wwwroot/index.html
```

`Services/BaiduOcrService.cs` 和 `wwwroot/index.html` 是替换文件。

## 必须修改 AppDbContext.cs

按这个补丁修改：

```text
patches/AppDbContext-persistent-ui-and-calibration.patch
```

新增 4 个 DbSet 和 6 个索引配置。

## 建议同时修改 deploy-backend.yml

按这个补丁修改：

```text
patches/deploy-backend-preserve-production-config.patch
```

它会在部署时保护服务器上的：

```text
appsettings.Production.json
```

避免 OCR Key、数据库密码、AllowedOrigins 被覆盖。

## 新增后端接口

### 估值误差校准

```http
GET  /api/fund/valuation-calibration/current?username=dabai521
GET  /api/fund/valuation-calibration/samples?username=dabai521&fundCode=012349
POST /api/fund/valuation-calibration/intraday-snapshot
POST /api/fund/valuation-calibration/settle
DELETE /api/fund/valuation-calibration/clear?username=dabai521&fundCode=012349
```

### UI 状态持久化

```http
GET  /api/fund/ui-state/all?username=dabai521
GET  /api/fund/ui-state?username=dabai521&key=privacy_mode
POST /api/fund/ui-state
DELETE /api/fund/ui-state?username=dabai521&key=privacy_mode
```

### 产品化面板快照

```http
GET  /api/fund/insight-snapshots/latest?username=dabai521&type=dashboard
POST /api/fund/insight-snapshots
```

## 数据库迁移

优先使用 EF Core Migration。

如果你线上还没有自动应用迁移，可以使用：

```text
persistent_ui_and_calibration_fallback.sql
```

执行前先备份数据库。

## 部署后验证

```bash
curl -sS "http://127.0.0.1:7084/api/fund/valuation-calibration/current?username=dabai521" | head -c 500
curl -sS "http://127.0.0.1:7084/api/fund/ui-state/all?username=dabai521" | head -c 500
curl -sS "http://127.0.0.1:7084/api/fund/insights/dashboard?username=dabai521" | head -c 500
```

前端打开后，开市期间会向 `intraday-snapshot` 写入盘中估值；盘后真实净值确认后会向 `settle` 写入误差样本。下一次盘中请求会从 `current` 读取 correctionOffset 修正盘中估值。

## 注意

误差校准是统计修正，不是精确反推基金经理调仓。单日误差可能来自仓位变化、现金比例、港股/QDII时间差、指数跟踪误差、申赎、费用和估值源误差。
