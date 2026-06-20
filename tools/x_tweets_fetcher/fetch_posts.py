#!/usr/bin/env python3
import asyncio
import hashlib
import hmac
import json
import os
import sys
import tempfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
import ssl
from urllib import request as urllib_request

DEFAULT_HANDLE = "aleabitoreddit"
DEFAULT_CACHE_PATH = "/var/lib/xiaobaiyangji/influencer-posts.json"
DEFAULT_MAX_STORE = 100
DEFAULT_FETCH_LIMIT = 20
DEFAULT_MAX_DISPLAY = 20
ACCOUNT_NAME = "xiaobai_influencer_reader"
TRANSLATION_FIELDS = (
    "translatedText",
    "translatedAt",
    "translationProvider",
    "translationStatus",
)


@dataclass(frozen=True)
class TranslationConfig:
    provider: str  # "none" | "custom" | "openai" | "libretranslate" | "tencent"
    target_lang: str
    cache_enabled: bool
    max_chars: int
    endpoint: str = ""
    api_key: str = ""
    model: str = ""
    secret_key: str = ""
    source_lang: str = "en"
    region: str = "ap-guangzhou"


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


def env_bool(name: str, default: bool) -> bool:
    raw = (os.getenv(name) or "").strip().lower()
    if not raw:
        return default
    return raw in {"1", "true", "yes", "on"}


def translation_config_from_env() -> TranslationConfig:
    provider = (os.getenv("TRANSLATE_PROVIDER") or "none").strip().lower() or "none"
    target_lang = (os.getenv("TRANSLATE_TARGET_LANG") or "zh-CN").strip() or "zh-CN"
    cache_enabled = env_bool("TRANSLATE_CACHE_ENABLED", True)
    max_chars = env_int("TRANSLATE_MAX_CHARS_PER_POST", 4000, 1, 20000)

    if provider in ("custom", "libretranslate"):
        endpoint = os.environ.get("TRANSLATE_CUSTOM_ENDPOINT", "").strip()
        api_key = os.environ.get("TRANSLATE_CUSTOM_API_KEY", "").strip()
        model = ""
    elif provider == "openai":
        endpoint = os.environ.get("TRANSLATE_OPENAI_ENDPOINT", "").strip()
        api_key = os.environ.get("TRANSLATE_OPENAI_API_KEY", os.environ.get("OPENAI_API_KEY", "")).strip()
        model = os.environ.get("TRANSLATE_OPENAI_MODEL", "").strip()
    elif provider == "tencent":
        endpoint = "https://tmt.tencentcloudapi.com"
        api_key = os.environ.get("TRANSLATE_TENCENT_SECRET_ID", "").strip()
        model = ""
    else:
        endpoint = ""
        api_key = ""
        model = ""

    return TranslationConfig(
        provider=provider,
        target_lang=target_lang,
        cache_enabled=cache_enabled,
        max_chars=max_chars,
        endpoint=endpoint,
        api_key=api_key,
        model=model,
        secret_key=(os.environ.get("TRANSLATE_TENCENT_SECRET_KEY", "").strip() if provider == "tencent" else ""),
        source_lang=(os.environ.get("TRANSLATE_TENCENT_SOURCE_LANG", "en").strip() or "en"),
        region=(os.environ.get("TRANSLATE_TENCENT_REGION", "ap-guangzhou").strip() or "ap-guangzhou"),
    )


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
        print("cookie configuration is missing or incomplete.", file=sys.stderr)
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
    created_text = normalize_datetime(getattr(tweet, "date", None))

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
        "replies": [],
        "mediaUrls": media_urls(tweet),
        "source": "twscrape",
        "translatedText": "",
        "translatedAt": None,
        "translationProvider": "none",
        "translationStatus": "skipped",
    }


def normalize_datetime(value: Any) -> str:
    if isinstance(value, datetime):
        timestamp = value if value.tzinfo is not None else value.replace(tzinfo=timezone.utc)
        return timestamp.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")
    return str(value or "")


def normalize_reply(tweet: Any, target_handle: str) -> dict[str, Any]:
    external_id = str(getattr(tweet, "id", "") or getattr(tweet, "id_str", ""))
    user = getattr(tweet, "user", None)
    return {
        "id": external_id,
        "text": str(getattr(tweet, "rawContent", "") or getattr(tweet, "text", "") or ""),
        "translatedText": "",
        "translatedAt": None,
        "translationProvider": "none",
        "translationStatus": "skipped",
        "createdAt": normalize_datetime(
            getattr(tweet, "date", None) or getattr(tweet, "createdAt", None)
        ),
        "authorName": str(getattr(user, "displayname", "") or ""),
        "authorUsername": str(getattr(user, "username", "") or ""),
        "likeCount": int(getattr(tweet, "likeCount", 0) or 0),
        "url": f"https://x.com/{target_handle}/status/{external_id}",
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
    for item in existing:
        external_id = str(item.get("externalId") or "").strip()
        if not external_id:
            continue
        merged[external_id] = dict(item)

    for item in incoming:
        external_id = str(item.get("externalId") or "").strip()
        if not external_id:
            continue
        previous = merged.get(external_id, {})
        combined = {**previous, **item}
        if str(previous.get("text") or "") == str(item.get("text") or ""):
            for field in TRANSLATION_FIELDS:
                if previous.get(field) not in (None, ""):
                    combined[field] = previous[field]
        # 如果旧推文有 replies 且新推文没有抓到，保留旧的
        if previous.get("replies") and not item.get("replies"):
            item["replies"] = previous["replies"]
        merged[external_id] = combined

    return sorted(
        merged.values(),
        key=lambda item: parse_time(item.get("createdAt")),
        reverse=True,
    )[:max_store]


def http_post_json(url: str, payload: dict[str, object], headers: dict[str, str]) -> dict[str, object]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    request = urllib_request.Request(
        url,
        data=body,
        headers={"Content-Type": "application/json", "Accept": "application/json", **headers},
        method="POST",
    )
    with urllib_request.urlopen(request, timeout=30) as response:
        result = json.loads(response.read().decode("utf-8"))
    if not isinstance(result, dict):
        raise ValueError("translation response must be a JSON object")
    return result


def http_post_json_bytes(url: str, body: bytes, headers: dict[str, str]) -> dict[str, object]:
    request = urllib_request.Request(url, data=body, headers=headers, method="POST")
    with urllib_request.urlopen(request, timeout=30, context=ssl.create_default_context()) as response:
        result = json.loads(response.read().decode("utf-8"))
    if not isinstance(result, dict):
        raise ValueError("translation response must be a JSON object")
    return result


def translated_text_from_response(provider: str, response: dict[str, object]) -> str:
    if provider in {"custom", "libretranslate"}:
        return str(response.get("translatedText") or "").strip()

    if provider == "openai":
        choices = response.get("choices")
        if isinstance(choices, list) and choices and isinstance(choices[0], dict):
            message = choices[0].get("message")
            if isinstance(message, dict):
                content = message.get("content")
                if isinstance(content, str):
                    return content.strip()

    return ""


def _sign_tc3(secret_key: str, date: str, service: str, string_to_sign: str) -> str:
    """TC3-HMAC-SHA256 签名"""
    def _hmac_sha256(key, msg):
        return hmac.new(key, msg.encode("utf-8"), hashlib.sha256).digest()
    k_date = _hmac_sha256(("TC3" + secret_key).encode("utf-8"), date)
    k_service = _hmac_sha256(k_date, service)
    k_signing = _hmac_sha256(k_service, "tc3_request")
    return hmac.new(k_signing, string_to_sign.encode("utf-8"), hashlib.sha256).hexdigest()


def _tencent_language_code(value: str, default: str) -> str:
    normalized = (value or default).strip()
    aliases = {
        "en-us": "en",
        "en-gb": "en",
        "zh-cn": "zh",
        "zh-hans": "zh",
        "zh-tw": "zh-TW",
        "zh-hant": "zh-TW",
    }
    return aliases.get(normalized.lower(), normalized)


def build_tencent_request(
    text: str,
    config: TranslationConfig,
    now: datetime | None = None,
) -> tuple[str, bytes, dict[str, str]]:
    endpoint = "tmt.tencentcloudapi.com"
    service = "tmt"
    action = "TextTranslate"
    version = "2018-03-21"
    algorithm = "TC3-HMAC-SHA256"

    secret_id = config.api_key
    secret_key = config.secret_key
    if not secret_id:
        raise RuntimeError("TRANSLATE_TENCENT_SECRET_ID not set")
    if not secret_key:
        raise RuntimeError("TRANSLATE_TENCENT_SECRET_KEY not set")

    payload = json.dumps({
        "SourceText": text,
        "Source": _tencent_language_code(config.source_lang, "en"),
        "Target": _tencent_language_code(config.target_lang, "zh"),
        "ProjectId": 0,
    }, ensure_ascii=False, separators=(",", ":"))

    current_time = now or datetime.now(timezone.utc)
    if current_time.tzinfo is None:
        current_time = current_time.replace(tzinfo=timezone.utc)
    timestamp = int(current_time.timestamp())
    date = datetime.fromtimestamp(timestamp, timezone.utc).strftime("%Y-%m-%d")

    # 1. 规范请求串
    http_request_method = "POST"
    canonical_uri = "/"
    canonical_querystring = ""
    ct = "application/json; charset=utf-8"
    canonical_headers = f"content-type:{ct}\nhost:{endpoint}\nx-tc-action:{action.lower()}\n"
    signed_headers = "content-type;host;x-tc-action"
    hashed_request_payload = hashlib.sha256(payload.encode("utf-8")).hexdigest()
    canonical_request = (
        f"{http_request_method}\n{canonical_uri}\n{canonical_querystring}\n"
        f"{canonical_headers}\n{signed_headers}\n{hashed_request_payload}"
    )

    # 2. 拼接待签名字符串
    credential_scope = f"{date}/{service}/tc3_request"
    hashed_canonical_request = hashlib.sha256(canonical_request.encode("utf-8")).hexdigest()
    string_to_sign = f"{algorithm}\n{timestamp}\n{credential_scope}\n{hashed_canonical_request}"

    # 3. 计算签名
    signature = _sign_tc3(secret_key, date, service, string_to_sign)

    # 4. 拼接 Authorization
    authorization = (
        f"{algorithm} "
        f"Credential={secret_id}/{credential_scope}, "
        f"SignedHeaders={signed_headers}, "
        f"Signature={signature}"
    )

    headers = {
        "Authorization": authorization,
        "Content-Type": ct,
        "Host": endpoint,
        "X-TC-Action": action,
        "X-TC-Timestamp": str(timestamp),
        "X-TC-Version": version,
        "X-TC-Region": config.region,
    }

    return f"https://{endpoint}", payload.encode("utf-8"), headers


def _tencent_translate(
    text: str,
    config: TranslationConfig,
    post_json_bytes=http_post_json_bytes,
) -> str:
    """调用腾讯云文本翻译 TextTranslate。"""
    url, payload, headers = build_tencent_request(text, config)
    body = post_json_bytes(url, payload, headers)
    response = body.get("Response")
    if not isinstance(response, dict):
        raise ValueError("Tencent TMT response is missing Response")
    error = response.get("Error")
    if isinstance(error, dict):
        error_code = str(error.get("Code") or "unknown")
        raise RuntimeError(f"Tencent TMT request failed: {error_code}")
    translated = str(response.get("TargetText") or "").strip()
    if not translated:
        raise ValueError("Tencent TMT response is missing TargetText")
    return translated


def request_translation(
    text: str,
    config: TranslationConfig,
    post_json=http_post_json,
    tencent_post_json=http_post_json_bytes,
) -> str:
    if config.provider not in ("none", "openai", "tencent") and not config.endpoint:
        raise ValueError("translation endpoint is not configured")

    headers: dict[str, str] = {}
    if config.api_key:
        headers["Authorization"] = f"Bearer {config.api_key}"

    if config.provider == "custom":
        payload: dict[str, object] = {"text": text, "targetLang": config.target_lang}
    elif config.provider == "libretranslate":
        payload = {"q": text, "source": "auto", "target": config.target_lang, "format": "text"}
        if config.api_key:
            payload["api_key"] = config.api_key
    elif config.provider == "openai":
        if not config.model:
            raise ValueError("translation model is not configured")
        payload = {
            "model": config.model,
            "messages": [
                {
                    "role": "system",
                    "content": f"Translate the user's text to {config.target_lang}. Return only the translation.",
                },
                {"role": "user", "content": text},
            ],
            "temperature": 0,
        }
    elif config.provider == "tencent":
        return _tencent_translate(text, config, tencent_post_json)
    else:
        raise ValueError("translation provider is not supported")

    translated = translated_text_from_response(config.provider, post_json(config.endpoint, payload, headers))
    if not translated:
        raise ValueError("translation response is empty")
    return translated


def translate_missing_posts(
    posts: list[dict[str, Any]],
    config: TranslationConfig,
    post_json=http_post_json,
    tencent_post_json=http_post_json_bytes,
) -> None:
    for item in posts:
        cached_translation = str(item.get("translatedText") or "").strip()
        if config.cache_enabled and cached_translation and item.get("translationStatus") == "success":
            continue

        item["translatedText"] = ""
        item["translatedAt"] = None
        item["translationProvider"] = config.provider

        text = str(item.get("text") or "").strip()
        if config.provider == "none" or not text:
            item["translationStatus"] = "skipped"
            continue

        try:
            item["translatedText"] = request_translation(
                text[: config.max_chars], config, post_json, tencent_post_json
            )
            item["translatedAt"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
            item["translationStatus"] = "success"
        except Exception:
            item["translationStatus"] = "failed"

        # 翻译 replies（如果 provider 不是 none）
        if config.provider != "none":
            for reply in item.get("replies", []) or []:
                reply_text = str(reply.get("text") or "").strip()
                if not reply_text:
                    reply["translationStatus"] = "skipped"
                    continue
                cached_reply = str(reply.get("translatedText") or "").strip()
                if config.cache_enabled and cached_reply and reply.get("translationStatus") == "success":
                    continue
                reply["translatedText"] = ""
                reply["translatedAt"] = None
                reply["translationProvider"] = config.provider
                try:
                    reply["translatedText"] = request_translation(
                        reply_text[:config.max_chars], config, post_json, tencent_post_json
                    )
                    reply["translatedAt"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
                    reply["translationStatus"] = "success"
                except Exception:
                    reply["translationStatus"] = "failed"


async def fetch_replies_for_posts(
    posts: list[dict],
    api,
    handle: str,
    max_replies_per_post: int = 5,
    timeout_seconds: float = 15.0,
) -> None:
    """
    为每条推文抓取最近回复。失败或超时不影响主推文展示。
    """
    if not posts:
        return

    async def fetch_one_reply_chain(post: dict) -> None:
        try:
            tweet_id = str(post.get("externalId") or post.get("id") or "").strip()
            if not tweet_id:
                return

            # 用 twscrape 搜索该推文的 conversation_id 来获取回复
            replies_raw = []
            try:
                async with asyncio.timeout(timeout_seconds):
                    # twscrape search 按 conversation_id 查找回复
                    query = f"conversation_id:{tweet_id}"
                    async for reply_tweet in api.search(query, limit=max_replies_per_post):
                        # 跳过原推自身
                        rid = str(getattr(reply_tweet, 'id', ''))
                        if rid == tweet_id:
                            continue
                        replies_raw.append(reply_tweet)
                        if len(replies_raw) >= max_replies_per_post:
                            break
            except (asyncio.TimeoutError, Exception):
                pass  # 超时或限流，保留已抓到的

            if not replies_raw:
                return

            replies = []
            for rt in replies_raw:
                try:
                    replies.append(normalize_reply(rt, handle))
                except Exception:
                    continue

            if replies:
                post["replies"] = replies

        except Exception:
            pass  # 任何异常都不影响主流程

    # 并发抓取，但限制并发数
    semaphore = asyncio.Semaphore(3)

    async def limited_fetch(post: dict) -> None:
        async with semaphore:
            await fetch_one_reply_chain(post)

    tasks = [limited_fetch(p) for p in posts]
    await asyncio.gather(*tasks, return_exceptions=True)


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


def prepare_storage(cache_path: Path, db_path: Path) -> None:
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    db_path.parent.mkdir(parents=True, exist_ok=True)


async def fetch_posts() -> int:
    cookie = read_cookie()
    if cookie is None:
        return 2

    from twscrape import API, gather

    handle = (os.getenv("INFLUENCER_TARGET_HANDLE") or DEFAULT_HANDLE).strip().lstrip("@") or DEFAULT_HANDLE
    cache_path = Path(os.getenv("INFLUENCER_POSTS_CACHE_PATH") or DEFAULT_CACHE_PATH)
    max_store = env_int("INFLUENCER_POSTS_MAX_STORE", DEFAULT_MAX_STORE, 1, 500)
    fetch_limit = env_int("INFLUENCER_POSTS_FETCH_LIMIT", DEFAULT_FETCH_LIMIT, 1, 100)
    max_display = env_int("INFLUENCER_POSTS_MAX_DISPLAY", DEFAULT_MAX_DISPLAY, 1, 20)
    db_path = Path(os.getenv("TWSCRAPE_DB_PATH") or str(cache_path.with_name("twscrape-accounts.db")))

    prepare_storage(cache_path, db_path)
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
    # 抓取评论/回复（仅对前 max_display 条）
    await fetch_replies_for_posts(
        items[:max_display], api, handle,
        max_replies_per_post=5, timeout_seconds=15.0,
    )
    translate_missing_posts(items[:max_display], translation_config_from_env())
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
