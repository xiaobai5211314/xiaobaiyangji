#!/usr/bin/env python3
"""
AKShare Sidecar — 板块资金流数据服务

监听 127.0.0.1:8765，提供 /sector-fund-flow 接口，
内部调用 akshare.stock_sector_fund_flow_rank 返回 JSON。

启动：
    pip install akshare flask pandas
    python tools/akshare_server.py

接口：
    GET /sector-fund-flow?indicator=今日&sector_type=行业资金流
    GET /sector-fund-flow?indicator=今日&sector_type=概念资金流
    GET /health
"""

import json
import sys
import traceback
from datetime import datetime

from flask import Flask, Response, jsonify, request

app = Flask(__name__)

# ── AKShare import (延迟导入，启动时如果 akshare 未安装也不会立即崩溃) ──────────
ak = None

def ensure_akshare():
    global ak
    if ak is None:
        import akshare as _ak
        ak = _ak

# ── 字段映射 ─────────────────────────────────────────────────────────────────
# akshare 返回的 DataFrame 列名 → JSON 字段名
FIELD_MAP = {
    "名称":    "name",
    "今日涨跌幅": "rate",
    "主力净流入-净额": "mainNet",
    "主力净流入-净占比": "mainRatio",
    "超大单净流入-净额": "superNet",
    "超大单净流入-净占比": "superRatio",
    "大单净流入-净额": "bigNet",
    "大单净流入-净占比": "bigRatio",
    "中单净流入-净额": "middleNet",
    "中单净流入-净占比": "middleRatio",
    "小单净流入-净额": "smallNet",
    "小单净流入-净占比": "smallRatio",
    "主力净流入最大股": "topStock",
}


def safe_float(v, default=0.0):
    try:
        if v is None or (isinstance(v, float) and v != v):  # NaN check
            return default
        return float(v)
    except (ValueError, TypeError):
        return default


def df_to_rows(df):
    """将 DataFrame 转为 JSON-friendly 的 dict list"""
    rows = []
    for _, row in df.iterrows():
        name = str(row.get("名称", "")).strip()
        if not name:
            continue
        item = {}
        for cn_key, en_key in FIELD_MAP.items():
            val = row.get(cn_key)
            if en_key in ("name", "topStock"):
                item[en_key] = str(val) if val is not None else ""
            else:
                item[en_key] = safe_float(val)
        rows.append(item)
    return rows


# ── 路由 ─────────────────────────────────────────────────────────────────────

@app.route("/health")
def health():
    return jsonify({"status": "ok", "timestamp": datetime.now().isoformat(), "akshare_loaded": ak is not None})


@app.route("/sector-fund-flow")
def sector_fund_flow():
    indicator = request.args.get("indicator", "今日")
    sector_type = request.args.get("sector_type", "行业资金流")

    # 参数校验
    valid_indicators = {"今日", "5日", "10日"}
    valid_types = {"行业资金流", "概念资金流", "地域资金流"}
    if indicator not in valid_indicators:
        return jsonify({"error": f"invalid indicator: {indicator}", "valid": list(valid_indicators)}), 400
    if sector_type not in valid_types:
        return jsonify({"error": f"invalid sector_type: {sector_type}", "valid": list(valid_types)}), 400

    try:
        ensure_akshare()
        df = ak.stock_sector_fund_flow_rank(indicator=indicator, sector_type=sector_type)
        rows = df_to_rows(df)
        return Response(
            json.dumps({"rows": rows, "count": len(rows)}, ensure_ascii=False),
            status=200,
            content_type="application/json; charset=utf-8",
        )
    except Exception as e:
        traceback.print_exc()
        return jsonify({"error": str(e), "count": 0, "rows": []}), 500


# ── 主入口 ───────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    host = "127.0.0.1"
    port = 8765
    print(f"[AKShare Sidecar] Starting on http://{host}:{port}")
    print(f"[AKShare Sidecar] Endpoints:")
    print(f"  GET /health")
    print(f"  GET /sector-fund-flow?indicator=今日&sector_type=行业资金流")
    app.run(host=host, port=port, debug=False, use_reloader=False)
