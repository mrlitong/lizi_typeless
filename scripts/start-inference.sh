#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -x "$project_root/.venv-vllm/bin/python" ]]; then
  python="$project_root/.venv-vllm/bin/python"
  backend="vllm"
else
  python="$project_root/.venv/bin/python"
  backend="transformers"
fi

if [[ ! -x "$python" ]]; then
  echo "Run scripts/setup-inference.sh first." >&2
  exit 1
fi

mkdir -p "$project_root/inference/logs"
exec >>"$project_root/inference/logs/service.log" 2>&1
export HF_HUB_OFFLINE=1
export TRANSFORMERS_OFFLINE=1
export LIZI_TYPELESS_ASR_BACKEND="$backend"
exec "$python" -m lizi_typeless_inference
