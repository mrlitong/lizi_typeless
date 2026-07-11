#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
environment="$project_root/.venv-vllm"

python3 -m venv "$environment"
"$environment/bin/python" -m pip install --upgrade pip

# qwen-asr declares Gradio as a hard dependency even though only its demo CLI imports it.
# Gradio 5 conflicts with vLLM's Pydantic requirement, so install the runtime-only set.
"$environment/bin/python" -m pip install "vllm==0.14.0"
"$environment/bin/python" -m pip install \
  "accelerate==1.12.0" \
  "fastapi==0.139.0" \
  "flask" \
  "httpx>=0.28,<1" \
  "librosa" \
  "modelscope>=1.33,<2" \
  "nagisa==0.2.11" \
  "pytest>=9,<10" \
  "pytest-cov>=7,<8" \
  "python-multipart>=0.0.22,<1" \
  "pytz" \
  "qwen-omni-utils" \
  "soundfile" \
  "sox" \
  "soxr>=1.0,<2" \
  "soynlp==0.0.493" \
  "transformers==4.57.6" \
  "uvicorn[standard]>=0.41,<1"
"$environment/bin/python" -m pip install --no-deps "qwen-asr==0.0.6"
"$environment/bin/python" -m pip install --no-deps -e "$project_root/inference"
