import tempfile
import unittest
from pathlib import Path

from tools.x_tweets_fetcher.fetch_posts import (
    TranslationConfig,
    merge_posts,
    prepare_storage,
    translate_missing_posts,
)


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


if __name__ == "__main__":
    unittest.main()
