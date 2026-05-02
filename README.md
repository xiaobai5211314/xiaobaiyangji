# 估值助手优化版说明

## 已完成

- OCR 密钥从源码移除，改为 `BaiduOcr` 配置读取。
- 登录密码改用 `PasswordHasher<User>`；老 SHA256 用户登录成功后会自动迁移到新哈希。
- CORS 从 `AllowAnyOrigin` 改为 `AllowedOrigins` 指定域名。
- 删除 WeatherForecast 模板文件。
- OCR 导入拆成预览与确认：`import-ocr-preview` / `import-ocr-confirm`。
- 启动时不再执行裸 `ALTER TABLE`，改为 `Database.Migrate()`。
- 新增 `PortfolioSettlementService`，加仓、减仓、结算公式可独立测试。
- `FundScraperService` 与夜间净值服务改用 `IHttpClientFactory`。
- 新增估值可信度、回本模拟字段。
- 新增接口：`portfolio-exposure`、`daily-report`、`news-impact-timeline`。
- 新增 OCR 纠错学习表 `OcrCorrections`。
- 新增 xUnit 测试项目 `估值助手.Tests`。

## 配置方式

不要把 OCR 密钥写入源码。生产环境使用环境变量：

```bash
export BaiduOcr__ApiKey="你的 API Key"
export BaiduOcr__SecretKey="你的 Secret Key"
```

本地开发可以使用：

```bash
dotnet user-secrets set "BaiduOcr:ApiKey" "你的 API Key"
dotnet user-secrets set "BaiduOcr:SecretKey" "你的 Secret Key"
```

## 迁移说明

新库可以直接运行，程序启动时会执行 `Database.Migrate()`。

如果你已有生产数据库且之前是 `EnsureCreated + 启动时 ALTER TABLE`，建议先备份数据库，然后创建 EF Migration baseline，避免重复创建索引。当前包内迁移适合新库或你确认迁移历史为空的环境。

## 验证命令

```bash
dotnet restore 估值助手.csproj
dotnet build 估值助手.csproj
dotnet test 估值助手.Tests/估值助手.Tests.csproj
```
