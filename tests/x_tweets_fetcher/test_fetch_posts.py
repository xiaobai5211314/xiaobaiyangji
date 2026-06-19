import tempfile
import unittest
from pathlib import Path

from tools.x_tweets_fetcher.fetch_posts import prepare_storage


class PrepareStorageTests(unittest.TestCase):
    def test_creates_cache_and_database_parent_directories(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            cache_path = root / "cache" / "influencer-posts.json"
            db_path = root / "database" / "twscrape-accounts.db"

            prepare_storage(cache_path, db_path)

            self.assertTrue(cache_path.parent.is_dir())
            self.assertTrue(db_path.parent.is_dir())


if __name__ == "__main__":
    unittest.main()
