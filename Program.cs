using Microsoft.EntityFrameworkCore;
using 估值助手.Models;
using 估值助手.Services;
Console.OutputEncoding = System.Text.Encoding.UTF8;  // ← 加这行
var builder = WebApplication.CreateBuilder(args);

// 1. 注册 SQLite 数据库（与 AppDbContext.OnConfiguring 里一致，用 fund_data.db）
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 2. 注册 Controller（FundController 需要这个）
builder.Services.AddControllers();

// 3. 注册后台抓取服务
builder.Services.AddHostedService<FundScraperService>();

var app = builder.Build();

// 4. 自动建表，首次运行时创建 fund_data.db 和相关表
// 自动在 MySQL 中创建对应的数据库和表
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // 如果宝塔里没有这些表，它会自动创建 User表 和 基金表
    dbContext.Database.EnsureCreated();
}

// 5. 静态文件（wwwroot/index.html）
app.UseStaticFiles();

// 6. 路由
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();