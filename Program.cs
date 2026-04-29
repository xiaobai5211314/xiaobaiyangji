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


// 头像 v2 最小 API 端点：用于规避旧服务器 Controller 路由未同步导致的 404。
// 注意：这些端点使用不同 URL，不会和 AuthController 的旧端点冲突。
app.MapGet("/api/auth/profile-v2", async (string username, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username)) return Results.BadRequest("缺少账号");
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
    if (user == null) return Results.NotFound("账号不存在");
    return Results.Ok(new { username = user.Username, avatarDataUrl = user.AvatarDataUrl ?? "" });
});

app.MapPost("/api/auth/avatar-file-v2", async (HttpRequest request, AppDbContext db) =>
{
    if (!request.HasFormContentType) return Results.BadRequest("请使用 multipart/form-data 上传");
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    var file = form.Files["avatarFile"];

    if (string.IsNullOrWhiteSpace(username)) return Results.BadRequest("缺少账号");
    if (file == null || file.Length == 0) return Results.BadRequest("头像不能为空");
    if (file.Length > 2_000_000) return Results.BadRequest("头像文件过大，请换一张更小的图片");
    if (string.IsNullOrWhiteSpace(file.ContentType) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("头像格式不正确");

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user == null) return Results.NotFound("账号不存在");

    await using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var bytes = ms.ToArray();
    var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;
    var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    if (dataUrl.Length > 1_200_000) return Results.BadRequest("头像保存后过大，请换一张更小的图片");

    user.AvatarDataUrl = dataUrl;
    await db.SaveChangesAsync();
    return Results.Ok(new { success = true, avatarDataUrl = user.AvatarDataUrl });
});

app.MapPost("/api/auth/avatar/clear-v2", async (HttpRequest request, AppDbContext db) =>
{
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();
    if (string.IsNullOrWhiteSpace(username)) return Results.BadRequest("缺少账号");
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user == null) return Results.NotFound("账号不存在");
    user.AvatarDataUrl = string.Empty;
    await db.SaveChangesAsync();
    return Results.Ok(new { success = true });
});

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();
