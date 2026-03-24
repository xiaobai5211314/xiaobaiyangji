using Microsoft.EntityFrameworkCore;
using 估值助手.Models;
using 估值助手.Services;
Console.OutputEncoding = System.Text.Encoding.UTF8;  // ← 加这行
var builder = WebApplication.CreateBuilder(args);

// 1. 注册 SQLite 数据库（与 AppDbContext.OnConfiguring 里一致，用 fund_data.db）
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=fund_data.db"));

// 2. 注册 Controller（FundController 需要这个）
builder.Services.AddControllers();

// 3. 注册后台抓取服务
builder.Services.AddHostedService<FundScraperService>();

var app = builder.Build();

// 4. 自动建表，首次运行时创建 fund_data.db 和相关表
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.EnsureDeleted(); // ?????? 新增这行：强行核爆物理硬盘上的旧数据库！
    db.Database.EnsureCreated(); // ?????? 重新生成包含 MyFunds 新表的干净数据库！
}

// 5. 静态文件（wwwroot/index.html）
app.UseStaticFiles();

// 6. 路由
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run();