# 股票模块数据库持久化版

## 已实现

- 首页“持仓”新增 `基金 / 股票` 切换，默认基金。
- 股票页支持：
  - 股票持仓列表
  - 股票自选列表
  - 搜索股票代码 / 名称
  - 加入自选
  - 转为持有
  - 查看走势
  - 分K / 时K / 日K / 月K / 年K 切换
  - OCR 股票持仓截图预览确认后写库
- 数据库持久化：
  - `StockHoldings` 股票持仓
  - `StockWatchItems` 自选股票
  - `StockOcrImportBatches` OCR 批次
  - `StockOcrImportItems` OCR 明细
  - `StockQuoteSnapshots` 行情快照
  - `StockKLineCaches` K 线缓存

## 复制文件

把这些文件复制到项目对应目录：

```text
Controllers/StockController.cs
Services/StockQuoteService.cs
Services/StockOcrParserService.cs
Models/AppDbContext.cs
Models/StockHolding.cs
Models/StockWatchItem.cs
Models/StockOcrImportBatch.cs
Models/StockOcrImportItem.cs
Models/StockQuoteSnapshot.cs
Models/StockKLineCache.cs
Migrations/202605030004_AddPersistentStockModule.cs
wwwroot/index.html
```

`Models/AppDbContext.cs` 是完整替换版，已经包含之前的基金估值校准表和新增股票表。

## 修改 Program.cs

如果你不直接替换 Program.cs，需要在服务注册区域加上：

```csharp
builder.Services.AddScoped<IStockQuoteService, EastmoneyStockQuoteService>();
builder.Services.AddScoped<StockOcrParserService>();
```

建议放在：

```csharp
builder.Services.AddScoped<PortfolioSettlementService>();
```

后面。

也可以用补丁：

```bash
git apply --check patches/Program-stock-module.patch
git apply patches/Program-stock-module.patch
```

## 数据库

优先使用 EF Core Migration。若线上自动迁移失败，可备份数据库后执行：

```text
persistent_stock_module_fallback.sql
```

## 验证接口

部署后在服务器执行：

```bash
curl -sS "http://127.0.0.1:7084/api/stock/dashboard?username=dabai521" | head -c 500
curl -sS "http://127.0.0.1:7084/api/stock/search?keyword=000001" | head -c 500
curl -sS "http://127.0.0.1:7084/api/stock/klines?code=000001&period=day" | head -c 500
```

都返回 JSON，说明后端接口可用。

## 注意

- 东方财富网页接口不是正式授权数据接口，稳定性和字段兼容性需要线上验证；如果后续要商业化，建议换成正式行情数据源。
- OCR 对同花顺/券商截图支持“有股票代码时优先解析”。如果截图没有代码，系统会给出名称候选，需要你搜索确认代码后再写库。
- 走势图只是观察用途，不提供交易下单。
