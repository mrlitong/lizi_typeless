from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path
from time import perf_counter

import httpx

from streaming_client import stream_audio


async def benchmark(args: argparse.Namespace) -> dict[str, object]:
    streaming = await stream_audio(
        args.audio,
        args.websocket_url,
        args.chunk_ms,
        args.real_time,
        args.timeout_seconds,
    )
    final = streaming.final
    result: dict[str, object] = {
        "audio": str(args.audio),
        "audioSeconds": streaming.audio_seconds,
        "sampleRate": streaming.sample_rate,
        "channels": streaming.channels,
        "chunkMilliseconds": args.chunk_ms,
        "realTimePacing": args.real_time,
        "sendMilliseconds": streaming.send_milliseconds,
        "finishToFinalMilliseconds": streaming.finish_to_final_milliseconds,
        "serverComputeMilliseconds": final["durationMilliseconds"],
        "serverFinishMilliseconds": final["finishMilliseconds"],
        "previewCount": streaming.preview_count,
        "language": final.get("language", ""),
        "rawText": final.get("text", ""),
    }

    if args.organize:
        organize_started_at = perf_counter()
        async with httpx.AsyncClient(base_url=args.http_base, timeout=args.timeout_seconds) as client:
            response = await client.post("/v1/organize", json={"text": final.get("text", "")})
            response.raise_for_status()
            organization = response.json()
        result.update(
            {
                "organizeWallMilliseconds": (perf_counter() - organize_started_at) * 1000,
                "organizeComputeMilliseconds": organization["duration_milliseconds"],
                "organizedText": organization["text"],
            }
        )

    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("audio", type=Path)
    parser.add_argument("--websocket-url", default="ws://127.0.0.1:8765/v1/stream")
    parser.add_argument("--http-base", default="http://127.0.0.1:8765")
    parser.add_argument("--chunk-ms", type=int, default=20)
    parser.add_argument("--real-time", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--organize", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--timeout-seconds", type=float, default=300.0)
    args = parser.parse_args()

    if args.chunk_ms <= 0:
        parser.error("--chunk-ms must be greater than zero")
    result = asyncio.run(benchmark(args))
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
