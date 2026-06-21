# X tweets sidecar

Fetches the latest posts from one fixed X account into a JSON cache consumed by the ASP.NET Core API. Translation runs in this sidecar and never in the frontend.

## Scope

- Target handle defaults to `aleabitoreddit`.
- The sidecar writes standard JSON, not JSONL or a database table.
- For the displayed range, the sidecar also caches recent replies so the WebApp can show translated replies and the original reply text in the tweet detail view.
- Fetch failures preserve the previous cache.
- Successful cached translations are reused while the source text is unchanged; uncached replies are still translated even when the parent post translation is already cached.
- Do not commit cookies, tokens, private environment files, or the generated twscrape database.

## Install and run

~~~bash
cd /www/wwwroot/小白养基/tools/x_tweets_fetcher
python3 -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt
python fetch_posts.py
~~~

System environment variables have priority. Otherwise the sidecar reads the server-local file at `<project-root>/.secrets/influencer.env`. Never print or copy that file into a command, appsettings, documentation, logs, or Git.

Relevant non-secret settings:

~~~text
INFLUENCER_TARGET_HANDLE
INFLUENCER_POSTS_CACHE_PATH
INFLUENCER_POSTS_MAX_STORE
INFLUENCER_POSTS_MAX_DISPLAY
TWSCRAPE_DB_PATH
TRANSLATE_PROVIDER
TRANSLATE_TARGET_LANG
TRANSLATE_CACHE_ENABLED
TRANSLATE_MAX_CHARS_PER_POST
TRANSLATE_CUSTOM_ENDPOINT
TRANSLATE_TENCENT_SECRET_ID
TRANSLATE_TENCENT_SECRET_KEY
TRANSLATE_TENCENT_SOURCE_LANG
TRANSLATE_TENCENT_REGION
~~~

The default translation provider is `none`; the page then displays the English original. See `docs/deploy/influencer-posts-sidecar.md` for private configuration, Firefox login, timer setup and troubleshooting.
