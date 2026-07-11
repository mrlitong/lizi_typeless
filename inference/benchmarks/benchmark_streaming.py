from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
from statistics import median
from time import perf_counter

import numpy as np
import soundfile as sf


def percentile(values: list[float], percent: float) -> float:
    ordered = sorted(values)
    index = min(len(ordered) - 1, round((len(ordered) - 1) * percent))
    return ordered[index]


def load_audio(path: Path) -> np.ndarray:
    audio, sample_rate = sf.read(path, dtype="float32", always_2d=False)
    if audio.ndim == 2:
        audio = audio.mean(axis=1)
    if sample_rate != 16_000:
        duration = audio.shape[0] / float(sample_rate)
        sample_count = round(duration * 16_000)
        source = np.linspace(0.0, duration, num=audio.shape[0], endpoint=False)
        target = np.linspace(0.0, duration, num=sample_count, endpoint=False)
        audio = np.interp(target, source, audio)
    return np.asarray(audio, dtype=np.float32)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("audio", type=Path)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--chunk-ms", type=int, default=500)
    args = parser.parse_args()

    os.environ.setdefault("HF_HUB_OFFLINE", "1")
    os.environ.setdefault("TRANSFORMERS_OFFLINE", "1")
    from qwen_asr import Qwen3ASRModel

    audio = load_audio(args.audio)
    load_started = perf_counter()
    asr = Qwen3ASRModel.LLM(
        model=str(args.model.resolve()),
        gpu_memory_utilization=0.50,
        max_model_len=4096,
        max_new_tokens=64,
        enforce_eager=True,
        dtype="bfloat16",
        load_format="safetensors",
    )
    load_milliseconds = (perf_counter() - load_started) * 1000

    warmup_started = perf_counter()
    warmup_state = asr.init_streaming_state(
        unfixed_chunk_num=2,
        unfixed_token_num=5,
        chunk_size_sec=2.0,
    )
    silence = np.zeros(16_000, dtype=np.float32)
    asr.streaming_transcribe(silence, warmup_state)
    asr.streaming_transcribe(silence, warmup_state)
    asr.finish_streaming_transcribe(warmup_state)
    warmup_milliseconds = (perf_counter() - warmup_started) * 1000

    state = asr.init_streaming_state(
        unfixed_chunk_num=2,
        unfixed_token_num=5,
        chunk_size_sec=2.0,
    )
    chunk_size = round(args.chunk_ms / 1000 * 16_000)
    calls: list[float] = []
    for offset in range(0, audio.shape[0], chunk_size):
        started = perf_counter()
        asr.streaming_transcribe(audio[offset : offset + chunk_size], state)
        calls.append((perf_counter() - started) * 1000)

    finish_started = perf_counter()
    asr.finish_streaming_transcribe(state)
    finish_milliseconds = (perf_counter() - finish_started) * 1000

    print(
        json.dumps(
            {
                "audioSeconds": audio.shape[0] / 16_000,
                "chunkMilliseconds": args.chunk_ms,
                "loadMilliseconds": load_milliseconds,
                "warmupMilliseconds": warmup_milliseconds,
                "appendP50Milliseconds": median(calls),
                "appendP95Milliseconds": percentile(calls, 0.95),
                "appendMaxMilliseconds": max(calls),
                "finishMilliseconds": finish_milliseconds,
                "calls": len(calls),
                "textLength": len(state.text),
                "textPreview": state.text[:120],
            },
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
