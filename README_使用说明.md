# gpt-two 版本：港股股票优化 + 股票数据长期持久化补丁 v2

## 基准版本

本补丁按你截图里的 `gpt-two` 分支做。

我核对到这个版本的 `AppDbContext.cs` 已经包含这些股票长期持久化表：

- `StockHoldings`
- `StockWatchItems`
- `StockOcrImportBatches`
- `StockOcrImportItems`
- `StockQuoteSnapshots`
- `StockKLineCaches`

所以这次不是重新加一套股票表，而是在现有股票体系上修港股识别、行情、走势和唯一索引。

## 改动文件

### 后端

覆盖：

- `Services/StockQuoteService.cs`
- `Controllers/StockController.cs`
- `Models/AppDbContext.cs`

脚本微调：

- `Services/StockOcrParserService.cs`
  - 只把股票代码识别从 6 位改成 5~6 位，支持港股 `01810`

### 前端

推荐运行：

- `推荐使用脚本/ApplyFrontendFix.ps1`

它只改：

1. 盈亏分析“今天”按钮：跳回今天所在月份并刷新数据
2. 手机端盈亏日历：修复周六、周日收益数字贴边/溢出

也提供了一个完整的：

- `可选覆盖到项目/wwwroot/index.html`

如果你本地 `index.html` 是当前会话里的同一版，可以直接覆盖；否则优先用脚本。

## 推荐使用方式

在项目根目录执行：

```powershell
.\推荐使用脚本\ApplyBackendPatch.ps1
.\推荐使用脚本\ApplyFrontendFix.ps1
dotnet build
```

如果数据库里已经有错误旧数据，例如：

```text
小米集团-W / SZ / 001810
```

再执行：

```text
数据库脚本/20260505_fix_hk_stock_code.sql
```

## 验证接口

```http
GET /api/stock/search?keyword=小米
GET /api/stock/quote?code=01810
GET /api/stock/klines?code=01810&period=day
```

正确目标：

```text
小米集团-W -> HK / 01810
走势 secid -> 116.01810
```

不应该再出现：

```text
SZ / 001810
```

## 说明

不要再用我之前那个旧补丁里的 `AppDbContext.cs` 覆盖当前项目；旧补丁是按 master 做的，会删掉你 gpt-two 分支里的 `OcrCorrections`、`UserInsightSnapshots` 等 DbSet，导致大量编译错误。