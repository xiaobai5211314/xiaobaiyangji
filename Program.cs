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
builder.Services.AddScoped<IStockQuoteService, EastmoneyStockQuoteService>();
builder.Services.AddScoped<StockOcrParserService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddHttpClient("FundGz", client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
});

builder.Services.AddHttpClient("EastMoney", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestVersion = new Version(1, 1);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "http://fundf10.eastmoney.com/");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
});

builder.Services.AddHttpClient("EastMoneyQuote", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestVersion = new Version(1, 1);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://quote.eastmoney.com/");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

builder.Services.AddHttpClient("TencentQuote", client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestVersion = new Version(1, 1);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://gu.qq.com/");
});

builder.Services.AddHttpClient("SinaQuote", client =>
{
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestVersion = new Version(1, 1);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://finance.sina.com.cn/");
});

builder.Services.AddHttpClient("WeChatMiniProgram", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
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

var app = builder.Build();

app.UseCors("ConfiguredOrigins");
app.UseResponseCompression();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.PhysicalPath ?? string.Empty;
        if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
            return;
        }

        const int durationInSeconds = 60 * 60 * 24 * 7;
        ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
    }
});
app.MapGet("/api/health", () => Results.Ok(new
{
    success = true,
    status = "ok",
    time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
}));

app.MapGet("/api/debug/version", (IWebHostEnvironment env) =>
{
    string gitCommit = "unknown";
    try
    {
        var gitDir = Path.Combine(env.ContentRootPath, ".git");
        if (Directory.Exists(gitDir))
        {
            var headFile = Path.Combine(gitDir, "HEAD");
            if (File.Exists(headFile))
            {
                var head = File.ReadAllText(headFile).Trim();
                if (head.StartsWith("ref: "))
                {
                    var refPath = Path.Combine(gitDir, head.Substring(5));
                    if (File.Exists(refPath))
                        gitCommit = File.ReadAllText(refPath).Trim()[..12];
                }
                else if (head.Length >= 12)
                {
                    gitCommit = head[..12];
                }
            }
        }
    }
    catch { }

    return Results.Ok(new
    {
        app = "xiaobaiyangji",
        buildVersion = "2026-06-02-debug-cache-v1",
        buildTime = System.IO.File.GetLastWriteTime(typeof(Program).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss"),
        gitCommit,
        serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        environment = env.EnvironmentName
    });
});
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();
