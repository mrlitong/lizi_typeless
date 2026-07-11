#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
modelscope="$project_root/.venv/bin/modelscope"

if [[ ! -x "$modelscope" ]]; then
  echo "Run scripts/setup-inference.sh first." >&2
  exit 1
fi

mkdir -p "$project_root/models"
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY \
  -u http_proxy -u https_proxy -u all_proxy \
  "$modelscope" download --model Qwen/Qwen3-ASR-1.7B \
  --local_dir "$project_root/models/Qwen3-ASR-1.7B"
env -u HTTP_PROXY -u HTTPS_PROXY -u ALL_PROXY \
  -u http_proxy -u https_proxy -u all_proxy \
  "$modelscope" download --model Qwen/Qwen3-1.7B \
  --local_dir "$project_root/models/Qwen3-1.7B"
