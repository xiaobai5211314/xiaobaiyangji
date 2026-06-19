using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using System.IO.Compression;
using System.Text.Json;
using 小白养基.Models;
using 小白养基.Services;
using StackExchange.Redis;

Console.OutputEncoding = System.Text.Encoding.UTF8;
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

var configuredOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? new[]
    {
        "https://guzhi.21212121.xyz",
        "http://localhost:5000",
        "https://localhost:5001"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        policy.WithOrigins(configuredOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/javascript",
        "application/json",
        "text/css",
        "text/html",
        "image/svg+xml"
    });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5 * 1024 * 1024;
    options.ValueLengthLimit = 2 * 1024 * 1024;
    options.MultipartHeadersLengthLimit = 64 * 1024;
});

builder.Services.Configure<BaiduOcrOptions>(builder.Configuration.GetSection("BaiduOcr"));
builder.Services.AddSingleton<IBaiduOcrService, BaiduOcrService>();
builder.Services.AddScoped<PortfolioSettlementService>();
builder.Services.AddScoped<DailyArchiveService>();
builder.Services.AddScoped<IStockQuoteService, EastmoneyStockQuoteService>();
builder.Services.AddScoped<StockOcrParserService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<MarketCacheService>();
builder.Services.AddScoped<InfluencerPostsCacheService>();

builder.Services.AddHttpClient("FundGz", client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
});

builder.Services.AddHttpClient("EastMoney", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestVersion = new Version(1, 1);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "http://fundf10.eastmoney.com/");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
});

builder.Services.AddHttpClient("EastMoneyQuote", client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestVersion = System.Net.HttpVersion.Version11;
    client.DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact;
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://quote.eastmoney.com/");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
}).ConfigurePrimaryHttpMessageHandler(() =>
{
    return new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.Zero,
        UseCookies = false,
    };
});

builder.Services.AddHttpClient("WeChatMiniProgram", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse("localhost:6379");
    options.AbortOnConnectFail = false;
    options.ConnectTimeout = 1500;
    options.SyncTimeout = 1500;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddMemoryCache();
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

builder.Services.AddHostedService<FundScraperService>();
builder.Services.AddHostedService<NavSettlementService>();
builder.Services.AddHostedService<DailySettlementService>();

var app = builder.Build();

app.UseCors("ConfiguredOrigins");
app.UseResponseCompression();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        dbContext.Database.Migrate();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[警告] 数据库迁移失败，服务继续启动: {ex.Message}");
    }
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.PhysicalPath ?? string.Empty;
        if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate, max-age=0";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
            return;
        }

        const int durationInSeconds = 60 * 60 * 24 * 7;
        ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
    }
});

// 全局：所有 /api/fund/* 和 /api/stock/* 持仓相关接口禁用缓存
app.UseWhen(ctx => (ctx.Request.Path.Value ?? "").StartsWith("/api/fund/", StringComparison.OrdinalIgnoreCase)
                 || (ctx.Request.Path.Value ?? "").StartsWith("/api/stock/", StringComparison.OrdinalIgnoreCase),
    branch => branch.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            return Task.CompletedTask;
        });
        await next();
    }));
app.MapGet("/api/health", () => Results.Ok(new
{
    success = true,
    status = "ok",
    time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
}));
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();
