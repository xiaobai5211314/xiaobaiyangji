# X tweets sidecar

Fetches the latest posts from one fixed X account into a JSON cache consumed by the ASP.NET Core API.

## Scope

- Target handle defaults to `aleabitoreddit`.
- This script is a sidecar. It must not be imported into the C# application.
- It writes a standard JSON file, not JSONL.
- Fetch failures preserve the previous cache.
- Do not commit cookies, tokens, or the generated `twscrape-accounts.db`.

## Install

~~~bash
cd /www/wwwroot/小白养基/tools/x_tweets_fetcher
python3 -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt
~~~

## Environment

System environment variables have priority. If they are not present, the sidecar reads:

~~~text
<project-root>/.secrets/influencer.env
~~~

Create it with:

~~~bash
X_COOKIE='auth_token=xxx; ct0=yyy' bash ./write_influencer_env.sh
~~~

The private env file format is:

~~~bash
X_COOKIE='auth_token=xxx; ct0=yyy'
INFLUENCER_TARGET_HANDLE=aleabitoreddit
INFLUENCER_SYNC_INTERVAL_MINUTES=30
INFLUENCER_POSTS_CACHE_PATH=/var/lib/xiaobaiyangji/influencer-posts.json
INFLUENCER_POSTS_MAX_STORE=100
INFLUENCER_POSTS_MAX_DISPLAY=10
TWS_TELEMETRY=0
~~~

Optional:

~~~bash
export TWSCRAPE_DB_PATH=/var/lib/xiaobaiyangji/twscrape-accounts.db
~~~

The `TWSCRAPE_DB_PATH` file contains account session data. Keep it private with the same care as `X_COOKIE`.

## Run once

~~~bash
. .venv/bin/activate
python fetch_posts.py
~~~

## Cron example

Do not run this more frequently than needed. The first version uses 30 minutes.

~~~cron
*/30 * * * * cd /www/wwwroot/小白养基/tools/x_tweets_fetcher && .venv/bin/python fetch_posts.py >> /var/log/xiaobaiyangji-influencer-posts.log 2>&1
~~~

Keep the real cookie only in <project-root>/.secrets/influencer.env or system environment variables.
