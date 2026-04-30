using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
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

// CORS：先全部放开，方便你现在排查 CDN / App / WebView 跨域问题。
// 后面稳定后，可以把 AllowAnyOrigin 改成 WithOrigins("https://guzhicdn.21212121.xyz", "https://guzhi.21212121.xyz")
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
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
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024;
    options.ValueLengthLimit = 5 * 1024 * 1024;
    options.MultipartHeadersLengthLimit = 128 * 1024;
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    // 宝塔 / Nginx / CDN 反代场景下，先不限制代理来源，方便排错。
    // 正式生产建议改成明确的 KnownProxies / KnownNetworks。
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddMemoryCache();

builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHostedService<FundScraperService>();
builder.Services.AddHostedService<NavSettlementService>();

var app = builder.Build();

app.UseForwardedHeaders();

app.UseResponseCompression();

// CORS 必须在 MapControllers / MapGet / MapPost 之前。
// 这里也放在 StaticFiles 前，避免静态文件跨域调试时没有 CORS 头。
app.UseCors("AllowAll");

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
        "ALTER TABLE Users ADD COLUMN AvatarDataUrl LONGTEXT;",
        "CREATE INDEX IX_FundRecord_Code_Time ON FundRecords (FundCode, FetchTime);",
        "CREATE INDEX IX_DailyArchive_User_Date_Code ON DailyArchives (Username, RecordDate, FundCode);",
        "CREATE INDEX IX_DailyArchive_User_Code_Date ON DailyArchives (Username, FundCode, RecordDate);"
    };

    foreach (var sql in runtimeSql)
    {
        try
        {
            dbContext.Database.ExecuteSqlRaw(sql);
        }
        catch
        {
            // 忽略重复字段、重复索引等启动时兼容错误。
        }
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
            ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache,no-store,must-revalidate";
            ctx.Context.Response.Headers[HeaderNames.Pragma] = "no-cache";
            ctx.Context.Response.Headers[HeaderNames.Expires] = "0";
            return;
        }

        const int durationInSeconds = 60 * 60 * 24 * 7;
        ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
    }
});

// 健康检查：用来判断请求是否真正打到 ASP.NET Core。
// 浏览器访问 /api/ping 应该返回 JSON。
app.MapGet("/api/ping", () =>
{
    return Results.Ok(new
    {
        success = true,
        message = "估值助手 API 正常",
        time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
    });
});

// profile v2
app.MapGet("/api/auth/profile-v2", async (string username, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest("缺少账号");

    username = username.Trim();

    var user = await db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(u => u.Username == username);

    if (user == null)
        return Results.NotFound("账号不存在");

    return Results.Ok(new
    {
        username = user.Username,
        avatarDataUrl = user.AvatarDataUrl ?? string.Empty
    });
});

// profile v3
app.MapGet("/api/auth/profile-v3", async (string username, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest("缺少账号");

    username = username.Trim();

    var user = await db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(u => u.Username == username);

    if (user == null)
        return Results.NotFound("账号不存在");

    return Results.Ok(new
    {
        username = user.Username,
        avatarDataUrl = user.AvatarDataUrl ?? string.Empty
    });
});

// avatar file v2
app.MapPost("/api/auth/avatar-file-v2", async (HttpRequest request, AppDbContext db) =>
{
    return await SaveAvatarFileAsync(request, db);
});

// avatar file v3
app.MapPost("/api/auth/avatar-file-v3", async (HttpRequest request, AppDbContext db) =>
{
    return await SaveAvatarFileAsync(request, db);
});

// avatar json v3：兜底接口。如果 multipart 被 App/WebView/网关处理异常，前端可以用 JSON 传 dataUrl。
app.MapPost("/api/auth/avatar-json-v3", async (HttpRequest request, AppDbContext db) =>
{
    var payload = await JsonSerializer.DeserializeAsync<AvatarJsonRequest>(
        request.Body,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

    if (payload == null)
        return Results.BadRequest("请求体为空");

    var username = (payload.Username ?? string.Empty).Trim();
    var avatarDataUrl = payload.AvatarDataUrl ?? string.Empty;

    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest("缺少账号");

    if (string.IsNullOrWhiteSpace(avatarDataUrl))
        return Results.BadRequest("头像不能为空");

    if (!avatarDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("头像格式不正确");

    if (avatarDataUrl.Length > 1_500_000)
        return Results.BadRequest("头像保存后过大，请换一张更小的图片");

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);

    if (user == null)
        return Results.NotFound("账号不存在");

    user.AvatarDataUrl = avatarDataUrl;
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true,
        avatarDataUrl = user.AvatarDataUrl
    });
});

// clear avatar v2
app.MapPost("/api/auth/avatar/clear-v2", async (HttpRequest request, AppDbContext db) =>
{
    return await ClearAvatarAsync(request, db);
});

// clear avatar v3
app.MapPost("/api/auth/avatar/clear-v3", async (HttpRequest request, AppDbContext db) =>
{
    return await ClearAvatarAsync(request, db);
});

app.MapControllers();

app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();

static async Task<IResult> SaveAvatarFileAsync(HttpRequest request, AppDbContext db)
{
    if (!request.HasFormContentType)
        return Results.BadRequest("请使用 multipart/form-data 上传");

    var form = await request.ReadFormAsync();

    var username = form["username"].ToString().Trim();
    var file = form.Files["avatarFile"];

    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest("缺少账号");

    if (file == null || file.Length == 0)
        return Results.BadRequest("头像不能为空");

    if (file.Length > 3_000_000)
        return Results.BadRequest("头像文件过大，请换一张更小的图片");

    if (string.IsNullOrWhiteSpace(file.ContentType) ||
        !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("头像格式不正确");
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);

    if (user == null)
        return Results.NotFound("账号不存在");

    await using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    var bytes = ms.ToArray();
    var mime = string.IsNullOrWhiteSpace(file.ContentType)
        ? "image/jpeg"
        : file.ContentType;

    var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

    if (dataUrl.Length > 1_500_000)
        return Results.BadRequest("头像保存后过大，请换一张更小的图片");

    user.AvatarDataUrl = dataUrl;
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true,
        avatarDataUrl = user.AvatarDataUrl
    });
}

static async Task<IResult> ClearAvatarAsync(HttpRequest request, AppDbContext db)
{
    if (!request.HasFormContentType)
        return Results.BadRequest("请使用表单提交");

    var form = await request.ReadFormAsync();
    var username = form["username"].ToString().Trim();

    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest("缺少账号");

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);

    if (user == null)
        return Results.NotFound("账号不存在");

    user.AvatarDataUrl = string.Empty;
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true
    });
}

public sealed class AvatarJsonRequest
{
    public string? Username { get; set; }
    public string? AvatarDataUrl { get; set; }
}