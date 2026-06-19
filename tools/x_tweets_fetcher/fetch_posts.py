#!/usr/bin/env python3
import asyncio
import json
import os
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

DEFAULT_HANDLE = "aleabitoreddit"
DEFAULT_CACHE_PATH = "/var/lib/xiaobaiyangji/influencer-posts.json"
DEFAULT_MAX_STORE = 100
DEFAULT_FETCH_LIMIT = 20
ACCOUNT_NAME = "xiaobai_influencer_reader"


def project_root() -> Path:
    return Path(__file__).resolve().parents[2]


def strip_env_quotes(value: str) -> str:
    text = value.strip()
    if len(text) >= 2 and text[0] == text[-1] and text[0] in ("'", '"'):
        return text[1:-1]
    return text


def load_private_env() -> None:
    env_path = project_root() / ".secrets" / "influencer.env"
    if not env_path.exists():
        return

    for raw_line in env_path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue

        key, value = line.split("=", 1)
        key = key.strip()
        if key and os.getenv(key) is None:
            os.environ[key] = strip_env_quotes(value)


load_private_env()
os.environ.setdefault("TWS_TELEMETRY", "0")


def env_int(name: str, default: int, minimum: int = 1, maximum: int = 500) -> int:
    raw = os.getenv(name)
    if not raw:
        return default
    try:
        value = int(raw)
    except ValueError:
        return default
    return max(minimum, min(maximum, value))


def cookie_value(cookie: str, name: str) -> str:
    prefix = f"{name}="
    for part in cookie.split(";"):
        item = part.strip()
        if item.startswith(prefix):
            return item[len(prefix) :]
    return ""


def read_cookie() -> str | None:
    cookie = (os.getenv("X_COOKIE") or "").strip()
    if not cookie or not cookie_value(cookie, "auth_token") or not cookie_value(cookie, "ct0"):
        print("X_COOKIE is required and must include auth_token and ct0.", file=sys.stderr)
        return None
    return cookie


async def ensure_cookie_account(api: Any, cookie: str) -> None:
    existing = await api.pool.get_account(ACCOUNT_NAME)
    if existing is None:
        await api.pool.add_account_cookies(ACCOUNT_NAME, cookie)
        return

    current_auth = (existing.cookies or {}).get("auth_token", "")
    current_ct0 = (existing.cookies or {}).get("ct0", "")
    if current_auth != cookie_value(cookie, "auth_token") or current_ct0 != cookie_value(cookie, "ct0"):
        await api.pool.delete_accounts([ACCOUNT_NAME])
        await api.pool.add_account_cookies(ACCOUNT_NAME, cookie)


def media_urls(tweet: Any) -> list[str]:
    media = getattr(tweet, "media", None)
    if media is None:
        return []

    urls: list[str] = []
    for photo in getattr(media, "photos", []) or []:
        url = getattr(photo, "url", "")
        if url:
            urls.append(url)
    for video in getattr(media, "videos", []) or []:
        thumb = getattr(video, "thumbnailUrl", "")
        if thumb:
            urls.append(thumb)
    for animated in getattr(media, "animated", []) or []:
        thumb = getattr(animated, "thumbnailUrl", "")
        if thumb:
            urls.append(thumb)

    return list(dict.fromkeys(urls))


def normalize_post(tweet: Any, target_handle: str) -> dict[str, Any]:
    external_id = str(getattr(tweet, "id_str", "") or getattr(tweet, "id", ""))
    created_at = getattr(tweet, "date", None)
    if isinstance(created_at, datetime):
        created_text = created_at.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")
    else:
        created_text = str(created_at or "")

    user = getattr(tweet, "user", None)
    author_handle = str(getattr(user, "username", "") or target_handle).lstrip("@")
    author_name = str(getattr(user, "displayname", "") or author_handle)

    return {
        "id": f"x:{external_id}",
        "externalId": external_id,
        "authorName": author_name,
        "authorHandle": author_handle,
        "text": str(getattr(tweet, "rawContent", "") or ""),
        "createdAt": created_text,
        "url": f"https://x.com/{target_handle}/status/{external_id}",
        "likeCount": int(getattr(tweet, "likeCount", 0) or 0),
        "retweetCount": int(getattr(tweet, "retweetCount", 0) or 0),
        "replyCount": int(getattr(tweet, "replyCount", 0) or 0),
        "quoteCount": int(getattr(tweet, "quoteCount", 0) or 0),
        "mediaUrls": media_urls(tweet),
        "source": "twscrape",
    }


def parse_time(value: Any) -> str:
    text = str(value or "")
    if not text:
        return ""
    try:
        return datetime.fromisoformat(text.replace("Z", "+00:00")).astimezone(timezone.utc).isoformat()
    except ValueError:
        return text


def load_existing(cache_path: Path) -> list[dict[str, Any]]:
    if not cache_path.exists():
        return []
    try:
        data = json.loads(cache_path.read_text(encoding="utf-8"))
    except Exception:
        return []

    if isinstance(data, list):
        return [x for x in data if isinstance(x, dict)]
    if isinstance(data, dict) and isinstance(data.get("items"), list):
        return [x for x in data["items"] if isinstance(x, dict)]
    return []


def merge_posts(existing: list[dict[str, Any]], incoming: list[dict[str, Any]], max_store: int) -> list[dict[str, Any]]:
    merged: dict[str, dict[str, Any]] = {}
    for item in existing + incoming:
        external_id = str(item.get("externalId") or "").strip()
        if not external_id:
            continue
        merged[external_id] = item

    return sorted(
        merged.values(),
        key=lambda item: parse_time(item.get("createdAt")),
        reverse=True,
    )[:max_store]


def atomic_write_json(cache_path: Path, payload: dict[str, Any]) -> None:
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp_name = tempfile.mkstemp(prefix=".influencer-posts.", suffix=".tmp", dir=str(cache_path.parent))
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            json.dump(payload, handle, ensure_ascii=False, indent=2)
            handle.write("\n")
        os.replace(tmp_name, cache_path)
    except Exception:
        try:
            os.unlink(tmp_name)
        except OSError:
            pass
        raise


async def fetch_posts() -> int:
    cookie = read_cookie()
    if cookie is None:
        return 2

    from twscrape import API, gather

    handle = (os.getenv("INFLUENCER_TARGET_HANDLE") or DEFAULT_HANDLE).strip().lstrip("@") or DEFAULT_HANDLE
    cache_path = Path(os.getenv("INFLUENCER_POSTS_CACHE_PATH") or DEFAULT_CACHE_PATH)
    max_store = env_int("INFLUENCER_POSTS_MAX_STORE", DEFAULT_MAX_STORE, 1, 500)
    fetch_limit = env_int("INFLUENCER_POSTS_FETCH_LIMIT", DEFAULT_FETCH_LIMIT, 1, 100)
    db_path = Path(os.getenv("TWSCRAPE_DB_PATH") or str(cache_path.with_name("twscrape-accounts.db")))

    api = API(str(db_path), raise_when_no_account=True)
    await ensure_cookie_account(api, cookie)

    user = await api.user_by_login(handle)
    if user is None:
        raise RuntimeError(f"target handle not found: {handle}")

    tweets = await gather(api.user_tweets(user.id, limit=fetch_limit))
    incoming = [normalize_post(tweet, handle) for tweet in tweets]
    incoming = [item for item in incoming if item["externalId"] and item["createdAt"] and item["text"]]

    existing = load_existing(cache_path)
    items = merge_posts(existing, incoming, max_store)
    payload = {
        "targetHandle": handle,
        "fetchedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "source": "twscrape",
        "items": items,
    }
    atomic_write_json(cache_path, payload)
    print(f"saved {len(items)} cached posts for @{handle} to {cache_path}")
    return 0


def main() -> int:
    try:
        return asyncio.run(fetch_posts())
    except SystemExit as exc:
        return int(exc.code or 0)
    except Exception as exc:
        print(f"fetch failed; old cache preserved: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
