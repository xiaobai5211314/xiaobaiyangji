using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using 估值助手.Models;
using 估值助手.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var builder = WebApplication.CreateBuilder(args);

// 1. 注册 数据库
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 🌟🌟🌟 新增：注册跨域服务 (CORS) 🌟🌟🌟
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()   // 允许任何人访问
              .AllowAnyMethod()   // 允许 GET/POST 等
              .AllowAnyHeader();  // 允许任何请求头
    });
});

// 2. 注册 Controller
builder.Services.AddControllers();

// 3. 注册后台抓取服务
builder.Services.AddHostedService<FundScraperService>();
builder.Services.AddHostedService<NavSettlementService>();
// 申请开启内存缓存弹药库
builder.Services.AddMemoryCache();
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
var app = builder.Build();

// 🌟🌟🌟 新增：启用跨域中间件 (必须放在 app.MapControllers() 前面) 🌟🌟🌟
app.UseCors("AllowAll");

// 4. 自动建表
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
}

// 5. 静态文件
app.UseStaticFiles();

// 6. 路由
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();