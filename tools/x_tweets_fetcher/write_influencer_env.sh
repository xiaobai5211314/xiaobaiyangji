#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd -- "$script_dir/../.." && pwd)"
secrets_dir="$project_root/.secrets"
target_file="$secrets_dir/influencer.env"

if [[ -z "${X_COOKIE:-}" ]]; then
  echo "X_COOKIE is required." >&2
  exit 2
fi

quote_env() {
  printf "%s" "$1" | sed "s/'/'\\''/g"
}

mkdir -p "$secrets_dir"
chmod 700 "$secrets_dir"

tmp_file="$(mktemp "$secrets_dir/.influencer.env.XXXXXX")"
{
  printf "X_COOKIE='%s'\n" "$(quote_env "$X_COOKIE")"
  printf "INFLUENCER_TARGET_HANDLE=%s\n" "${INFLUENCER_TARGET_HANDLE:-aleabitoreddit}"
  printf "INFLUENCER_SYNC_INTERVAL_MINUTES=%s\n" "${INFLUENCER_SYNC_INTERVAL_MINUTES:-30}"
  printf "INFLUENCER_POSTS_CACHE_PATH=%s\n" "${INFLUENCER_POSTS_CACHE_PATH:-/var/lib/xiaobaiyangji/influencer-posts.json}"
  printf "INFLUENCER_POSTS_MAX_STORE=%s\n" "${INFLUENCER_POSTS_MAX_STORE:-100}"
  printf "INFLUENCER_POSTS_MAX_DISPLAY=%s\n" "${INFLUENCER_POSTS_MAX_DISPLAY:-10}"
  printf "TWS_TELEMETRY=%s\n" "${TWS_TELEMETRY:-0}"
} > "$tmp_file"

chmod 600 "$tmp_file"
mv "$tmp_file" "$target_file"
chmod 600 "$target_file"

echo "写入成功: $target_file"
echo "X_COOKIE_LENGTH=${#X_COOKIE}"
