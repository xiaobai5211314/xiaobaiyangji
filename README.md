# 估值助手：头像登录恢复修复版

替换文件：
- index.html
- AuthController.cs
- User.cs
- AppDbContext.cs

数据库先执行：

```sql
UPDATE Users SET AvatarDataUrl = '' WHERE AvatarDataUrl IS NULL;
```

后端重启后测试：

```bash
curl -i -X POST http://127.0.0.1:7084/api/auth/login \
  -F "username=dabai521" \
  -F "password=dabai521"

curl -i "http://127.0.0.1:7084/api/auth/profile-v3?username=dabai521"
```
