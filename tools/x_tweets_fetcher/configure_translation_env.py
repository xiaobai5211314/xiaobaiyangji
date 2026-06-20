#!/usr/bin/env python3
import argparse
import json
import os
import sys
import tempfile
from pathlib import Path
from typing import Mapping

try:
    from .export_x_cookie import merge_env_content
except ImportError:
    from export_x_cookie import merge_env_content


REQUIRED_SECRET_KEYS = (
    "TRANSLATE_TENCENT_SECRET_ID",
    "TRANSLATE_TENCENT_SECRET_KEY",
)


def update_translation_env(env_path: Path, credentials: Mapping[str, str]) -> None:
    missing = [key for key in REQUIRED_SECRET_KEYS if not str(credentials.get(key) or "").strip()]
    if missing:
        raise ValueError("missing required Tencent translation credentials")

    updates = {
        "TRANSLATE_PROVIDER": "tencent",
        "TRANSLATE_TARGET_LANG": "zh-CN",
        "TRANSLATE_CACHE_ENABLED": "true",
        "TRANSLATE_TENCENT_SOURCE_LANG": "en",
        "TRANSLATE_TENCENT_REGION": "ap-guangzhou",
        "TRANSLATE_TENCENT_SECRET_ID": str(credentials["TRANSLATE_TENCENT_SECRET_ID"]).strip(),
        "TRANSLATE_TENCENT_SECRET_KEY": str(credentials["TRANSLATE_TENCENT_SECRET_KEY"]).strip(),
    }

    existing = env_path.read_text(encoding="utf-8") if env_path.exists() else ""
    merged = merge_env_content(existing, updates)
    env_path.parent.mkdir(parents=True, exist_ok=True)

    fd, temp_name = tempfile.mkstemp(prefix=".influencer.env.", dir=str(env_path.parent))
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(merged)
            handle.flush()
            os.fsync(handle.fileno())
        os.chmod(temp_name, 0o600)
        os.replace(temp_name, env_path)
        os.chmod(env_path, 0o600)
    except Exception:
        try:
            os.unlink(temp_name)
        except OSError:
            pass
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description="Update private Tencent translation settings from stdin JSON.")
    parser.add_argument("env_file", type=Path)
    args = parser.parse_args()

    payload = json.load(sys.stdin)
    if not isinstance(payload, dict):
        raise ValueError("translation credential payload must be an object")
    update_translation_env(args.env_file, payload)
    print("Tencent translation private configuration updated.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
