using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using System.IO.Compression;
using System.Text.Json;
using 估值助手.Models;
using 估值助手.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
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

builder.Services.AddMemoryCache();
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

builder.Services.AddHostedService<FundScraperService>();
builder.Services.AddHostedService<NavSettlementService>();

var app = builder.Build();

app.UseCors("AllowAll");
app.UseResponseCompression();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();

    var runtimeSql = new[]
    {
        "ALTER TABLE MyFunds ADD COLUMN LastSettledProfit DOUBLE NOT NULL DEFAULT 0;",
        "ALTER TABLE MyFunds ADD COLUMN LastSettledRate DOUBLE NOT NULL DEFAULT 0;",
        "ALTER TABLE MyFunds ADD COLUMN LastTradeDate VARCHAR(20);",
        "ALTER TABLE MyFunds ADD COLUMN LastAddAmount DOUBLE NOT NULL DEFAULT 0;",
        "ALTER TABLE MyFunds ADD COLUMN RealizedProfit DOUBLE NOT NULL DEFAULT 0;",
        "ALTER TABLE FundRecords ADD COLUMN ActualRate DOUBLE NOT NULL DEFAULT 0;",
        "ALTER TABLE FundRecords ADD COLUMN DiffRate DOUBLE NOT NULL DEFAULT 0;",
        "CREATE INDEX IX_FundRecord_Code_Time ON FundRecords (FundCode, FetchTime);",
        "CREATE INDEX IX_DailyArchive_User_Date_Code ON DailyArchives (Username, RecordDate, FundCode);",
        "CREATE INDEX IX_DailyArchive_User_Code_Date ON DailyArchives (Username, FundCode, RecordDate);",
        "ALTER TABLE Users ADD COLUMN AvatarDataUrl LONGTEXT;"
    };

    foreach (var sql in runtimeSql)
    {
        try { dbContext.Database.ExecuteSqlRaw(sql); }
        catch { }
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
            ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
            return;
        }

        const int durationInSeconds = 60 * 60 * 24 * 7;
        ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
    }
});

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();
