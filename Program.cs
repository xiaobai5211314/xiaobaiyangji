using Microsoft.EntityFrameworkCore;
using 估值助手.Models;
using 估值助手.Services;
Console.OutputEncoding = System.Text.Encoding.UTF8;  //  加这行
var builder = WebApplication.CreateBuilder(args);

// 1. 注册 SQLite 数据库（与 AppDbContext.OnConfiguring 里一致，用 fund_data.db）
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 2. 注册 Controller（FundController 需要这个）
builder.Services.AddControllers();

// 3. 注册后台抓取服务
builder.Services.AddHostedService<FundScraperService>();

// 👉 加上这行：注册夜间清算服务 (晚上的会计)
builder.Services.AddHostedService<NavSettlementService>();

var app = builder.Build();

// 4. 自动建表，首次运行时创建 fund_data.db 和相关表
// 自动在 MySQL 中创建对应的数据库和表
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // 如果宝塔里没有这些表，它会自动创建 User表和基金表
    dbContext.Database.EnsureCreated();
}

// 5. 静态文件（wwwroot/index.html）
app.UseStaticFiles();

// 6. 路由
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/index.html"));
using (var scope = app.Services.CreateScope())
{
    // 假设你的 FundController 里 GetAllFundsAsync 是静态方法或者可以通过服务调用
    // 为了简单，我们直接在启动时触发一次字典初始化
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated(); //
}
app.Run();