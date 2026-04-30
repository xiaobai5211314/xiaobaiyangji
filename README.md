本轮修复：资讯 + 主力资金恢复

问题原因：
上一包为修基金板块时，FundController.cs 替换成了精简版，保留了 /api/fund/sectors 和 /api/fund/sector-funds，但漏掉了前端正在调用的：
- GET /api/fund/news
- GET /api/fund/holding-news
- GET /api/fund/capital-flow
因此资讯页会显示“资讯同步失败”，板块页底部主力资金流入/流出为空。

本包处理：
1. index.html 保留上一版：固定 API 域名 https://guzhi.21212121.xyz，并保留四页布局、数据库头像恢复、基金板块页面。
2. FundController.cs 改为完整合并版：恢复 news / holding-news / capital-flow，同时保留基金板块扫描接口。
3. AuthController.cs / User.cs / AppDbContext.cs 保留头像数据库恢复修复。

替换路径：
- wwwroot/index.html 或 CDN 的 index.html
- Controllers/FundController.cs
- Controllers/AuthController.cs
- Models/User.cs
- Models/AppDbContext.cs
- Program.cs 如你当前 Program.cs 已正常，可不替换；如服务启动或 CORS 有问题再替换。

部署后测试：
curl -i "http://127.0.0.1:7084/api/fund/news?username=dabai521&mode=global&limit=5&force=true"
curl -i "http://127.0.0.1:7084/api/fund/capital-flow?limit=10&force=true"
curl -i "http://127.0.0.1:7084/api/fund/sectors?force=true"

如果前端仍空：
1. 确认后端已重启。
2. CDN 刷新 index.html。
3. 清理旧缓存：
localStorage.removeItem('capital_flow_cache_v1');
localStorage.removeItem('fund_sector_fast_cache_v4');
