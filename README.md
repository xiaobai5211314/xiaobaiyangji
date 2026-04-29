本轮修改点：
1. 用户头像改为保存到 Users.AvatarDataUrl 数据库字段，不再只存在 localStorage。
2. 新增 AuthController：GET /api/auth/profile、POST /api/auth/avatar、POST /api/auth/avatar/clear。
3. Program.cs 启动时自动补 Users.AvatarDataUrl LONGTEXT 字段。
4. 首页去掉重复的刷新按钮和“估值助手/用户名”大标题；首页只保留头像入口和截图导入。
5. PC 端底部四页导航加宽、弱化突兀感；移动端保留紧凑底部导航。
6. 移动端盈亏明细右侧数字增加安全边距，避免曲面屏或浏览器边缘裁切。
7. 全局字体改为 Microsoft YaHei / 微软雅黑 优先。
8. 板块页新增主力资金流入模块，后端新增 GET /api/fund/capital-flow。

重点替换：
- index.html
- User.cs
- AuthController.cs
- Program.cs
- FundController.cs

注意：
- 数据库自动 ALTER TABLE 适合你当前快速迭代；正式生产建议后续迁移到 EF Core migrations。
- 主力资金数据来自东方财富板块资金流向公开页面/接口链路，仅供参考，不构成投资建议。
