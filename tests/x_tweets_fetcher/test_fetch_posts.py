import json
import tempfile
import unittest
from datetime import datetime, timezone
from unittest.mock import patch
from pathlib import Path

from tools.x_tweets_fetcher.fetch_posts import (
    TranslationConfig,
    build_tencent_request,
    merge_posts,
    prepare_storage,
    request_translation,
    translation_config_from_env,
    translate_missing_posts,
)
from tools.x_tweets_fetcher.export_x_cookie import merge_env_content
from tools.x_tweets_fetcher.configure_translation_env import update_translation_env


class PrepareStorageTests(unittest.TestCase):
    def test_creates_cache_and_database_parent_directories(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            cache_path = root / "cache" / "influencer-posts.json"
            db_path = root / "database" / "twscrape-accounts.db"

            prepare_storage(cache_path, db_path)

            self.assertTrue(cache_path.parent.is_dir())
            self.assertTrue(db_path.parent.is_dir())


class TranslationCacheTests(unittest.TestCase):
    def test_tencent_config_uses_dedicated_secret_id_and_secret_key(self) -> None:
        env = {
            "TRANSLATE_PROVIDER": "tencent",
            "TRANSLATE_TENCENT_SECRET_ID": "test-secret-id",
            "TRANSLATE_TENCENT_SECRET_KEY": "test-secret-key",
            "TRANSLATE_TENCENT_SOURCE_LANG": "en",
            "TRANSLATE_TENCENT_REGION": "ap-guangzhou",
            "TRANSLATE_CUSTOM_API_KEY": "must-not-be-used",
        }

        with patch.dict("os.environ", env, clear=True):
            config = translation_config_from_env()

        self.assertEqual("tencent", config.provider)
        self.assertEqual("test-secret-id", config.api_key)
        self.assertEqual("test-secret-key", config.secret_key)
        self.assertEqual("en", config.source_lang)
        self.assertEqual("ap-guangzhou", config.region)

    def test_tencent_request_uses_documented_text_translate_payload_and_headers(self) -> None:
        config = TranslationConfig(
            provider="tencent",
            target_lang="zh-CN",
            cache_enabled=True,
            max_chars=4000,
            api_key="test-secret-id",
            secret_key="test-secret-key",
            source_lang="en-US",
            region="ap-guangzhou",
        )
        now = datetime(2026, 6, 20, 4, 0, 0, tzinfo=timezone.utc)

        url, body, headers = build_tencent_request("hello", config, now)

        self.assertEqual("https://tmt.tencentcloudapi.com", url)
        self.assertEqual(
            {"SourceText": "hello", "Source": "en", "Target": "zh", "ProjectId": 0},
            json.loads(body.decode("utf-8")),
        )
        self.assertEqual("TextTranslate", headers["X-TC-Action"])
        self.assertEqual("2018-03-21", headers["X-TC-Version"])
        self.assertEqual("ap-guangzhou", headers["X-TC-Region"])
        self.assertIn("Credential=test-secret-id/", headers["Authorization"])
        self.assertNotIn("test-secret-key", headers["Authorization"])

    def test_tencent_response_returns_target_text_without_live_network(self) -> None:
        config = TranslationConfig(
            provider="tencent",
            target_lang="zh-CN",
            cache_enabled=True,
            max_chars=4000,
            api_key="test-secret-id",
            secret_key="test-secret-key",
        )

        def fake_tencent_post(url: str, body: bytes, headers: dict[str, str]) -> dict[str, object]:
            return {"Response": {"TargetText": "你好", "RequestId": "test-request-id"}}

        translated = request_translation(
            "hello",
            config,
            tencent_post_json=fake_tencent_post,
        )

        self.assertEqual("你好", translated)

    def test_merge_preserves_successful_cached_translation(self) -> None:
        existing = [{
            "externalId": "123",
            "text": "same text",
            "createdAt": "2026-06-19T08:00:00Z",
            "translatedText": "已有译文",
            "translatedAt": "2026-06-19T08:01:00Z",
            "translationProvider": "custom",
            "translationStatus": "success",
        }]
        incoming = [{
            "externalId": "123",
            "text": "same text",
            "createdAt": "2026-06-19T08:00:00Z",
        }]

        merged = merge_posts(existing, incoming, 20)

        self.assertEqual("same text", merged[0]["text"])
        self.assertEqual("已有译文", merged[0]["translatedText"])
        self.assertEqual("success", merged[0]["translationStatus"])

    def test_none_provider_marks_untranslated_post_skipped(self) -> None:
        posts = [{"externalId": "123", "text": "hello"}]
        config = TranslationConfig(provider="none", target_lang="zh-CN", cache_enabled=True, max_chars=4000)

        translate_missing_posts(posts, config)

        self.assertEqual("", posts[0]["translatedText"])
        self.assertEqual("none", posts[0]["translationProvider"])
        self.assertEqual("skipped", posts[0]["translationStatus"])

    def test_custom_provider_translates_only_posts_without_cached_translation(self) -> None:
        posts = [
            {"externalId": "123", "text": "hello"},
            {
                "externalId": "456",
                "text": "cached",
                "translatedText": "缓存译文",
                "translatedAt": "2026-06-19T08:01:00Z",
                "translationProvider": "custom",
                "translationStatus": "success",
            },
        ]
        config = TranslationConfig(
            provider="custom",
            target_lang="zh-CN",
            cache_enabled=True,
            max_chars=4000,
            endpoint="https://translator.invalid/translate",
            api_key="test-only",
        )
        requests: list[dict[str, object]] = []

        def fake_post(url: str, payload: dict[str, object], headers: dict[str, str]) -> dict[str, object]:
            requests.append({"url": url, "payload": payload, "headers": headers})
            return {"translatedText": "你好"}

        translate_missing_posts(posts, config, fake_post)

        self.assertEqual(1, len(requests))
        self.assertEqual("你好", posts[0]["translatedText"])
        self.assertEqual("success", posts[0]["translationStatus"])
        self.assertEqual("缓存译文", posts[1]["translatedText"])

    def test_translation_failure_keeps_original_text(self) -> None:
        posts = [{"externalId": "123", "text": "keep me"}]
        config = TranslationConfig(
            provider="custom",
            target_lang="zh-CN",
            cache_enabled=True,
            max_chars=4000,
            endpoint="https://translator.invalid/translate",
        )

        def failing_post(url: str, payload: dict[str, object], headers: dict[str, str]) -> dict[str, object]:
            raise RuntimeError("offline")

        translate_missing_posts(posts, config, failing_post)

        self.assertEqual("keep me", posts[0]["text"])
        self.assertEqual("", posts[0]["translatedText"])
        self.assertEqual("failed", posts[0]["translationStatus"])


class InfluencerEnvTests(unittest.TestCase):
    def test_cookie_export_preserves_tencent_translation_configuration(self) -> None:
        existing = """# local configuration
X_COOKIE=old-cookie
TRANSLATE_PROVIDER=tencent
TRANSLATE_TENCENT_SECRET_ID=test-secret-id
TRANSLATE_TENCENT_SECRET_KEY=test-secret-key
"""

        merged = merge_env_content(existing, {"X_COOKIE": "auth_token=new; ct0=new"})

        self.assertIn("X_COOKIE='auth_token=new; ct0=new'", merged)
        self.assertIn("TRANSLATE_PROVIDER=tencent", merged)
        self.assertIn("TRANSLATE_TENCENT_SECRET_ID=test-secret-id", merged)
        self.assertIn("TRANSLATE_TENCENT_SECRET_KEY=test-secret-key", merged)
        self.assertNotIn("X_COOKIE=old-cookie", merged)

    def test_translation_deploy_preserves_cookie_and_writes_tencent_settings(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            env_path = Path(temp_dir) / "influencer.env"
            env_path.write_text(
                "X_COOKIE='auth_token=keep; ct0=keep'\nINFLUENCER_POSTS_MAX_DISPLAY=20\n",
                encoding="utf-8",
            )

            update_translation_env(
                env_path,
                {
                    "TRANSLATE_TENCENT_SECRET_ID": "test-secret-id",
                    "TRANSLATE_TENCENT_SECRET_KEY": "test-secret-key",
                },
            )

            content = env_path.read_text(encoding="utf-8")
            self.assertIn("X_COOKIE='auth_token=keep; ct0=keep'", content)
            self.assertIn("INFLUENCER_POSTS_MAX_DISPLAY=20", content)
            self.assertIn("TRANSLATE_PROVIDER='tencent'", content)
            self.assertIn("TRANSLATE_TARGET_LANG='zh-CN'", content)
            self.assertIn("TRANSLATE_TENCENT_SOURCE_LANG='en'", content)
            self.assertIn("TRANSLATE_TENCENT_REGION='ap-guangzhou'", content)
            self.assertIn("TRANSLATE_TENCENT_SECRET_ID='test-secret-id'", content)
            self.assertIn("TRANSLATE_TENCENT_SECRET_KEY='test-secret-key'", content)


if __name__ == "__main__":
    unittest.main()
