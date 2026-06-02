# AKShare Sidecar 部署指南

## 概述

`tools/akshare_server.py` 是一个轻量级 Flask 服务，为 `/api/fund/capital-flow` 提供 AKShare 板块资金流数据。

当 push2.eastmoney.com（主源）不可用时，后端会自动 fallback 到此服务。

## 安装依赖

```bash
pip install akshare flask pandas
```

## 启动

```bash
# 直接运行
python tools/akshare_server.py

# 后台运行（Linux/macOS）
nohup python tools/akshare_server.py > akshare.log 2>&1 &
```

服务默认监听 `http://127.0.0.1:8765`。

## 接口

```
GET /health
GET /sector-fund-flow?indicator=今日&sector_type=行业资金流
GET /sector-fund-flow?indicator=今日&sector_type=概念资金流
```

`indicator` 可选值：`今日`、`5日`、`10日`
`sector_type` 可选值：`行业资金流`、`概念资金流`、`地域资金流`

## 验证

```bash
curl http://127.0.0.1:8765/health
curl "http://127.0.0.1:8765/sector-fund-flow?indicator=今日&sector_type=行业资金流"
```

## Fallback 行为

如果 AKShare 服务未启动或返回错误，后端会自动 fallback：

```
push2 行业 → push2 概念 → AKShare 行业 → AKShare 概念 → 本地持仓估算
```

不会影响其他功能（/api/fund/today、OCR、股票模块等）。

## 生产部署方式

### systemd（Linux）

创建 `/etc/systemd/system/akshare-sidecar.service`：

```ini
[Unit]
Description=AKShare Sidecar Service
After=network.target

[Service]
Type=simple
User=www-data
WorkingDirectory=/path/to/小白养基
ExecStart=/usr/bin/python3 tools/akshare_server.py
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable akshare-sidecar
sudo systemctl start akshare-sidecar
sudo systemctl status akshare-sidecar
```

### pm2

```bash
pm2 start tools/akshare_server.py --name akshare-sidecar --interpreter python3
pm2 save
pm2 startup
```

### Docker

```dockerfile
FROM python:3.11-slim
WORKDIR /app
COPY tools/akshare_server.py .
RUN pip install --no-cache-dir akshare flask pandas
EXPOSE 8765
CMD ["python", "akshare_server.py"]
```

```bash
docker build -t akshare-sidecar -f Dockerfile.akshare .
docker run -d --name akshare-sidecar --network host akshare-sidecar
```

> 使用 `--network host` 让容器直接使用宿主机网络，以便 C# 后端通过 `127.0.0.1:8765` 访问。
