from __future__ import annotations

import asyncio
from dataclasses import dataclass
import json
from pathlib import Path
from time import perf_counter

import numpy as np
import soundfile as sf
from websockets.asyncio.client import ClientConnection, connect


@dataclass(frozen=True, slots=True)
class StreamingResult:
    audio_seconds: float
    sample_rate: int
    channels: int
    send_milliseconds: float
    finish_to_final_milliseconds: float
    preview_count: int
    final: dict[str, object]


def load_audio(path: Path) -> tuple[bytes, int, int, float]:
    audio, sample_rate = sf.read(path, dtype="float32", always_2d=True)
    audio = np.asarray(audio, dtype="<f4", order="C")
    duration_seconds = audio.shape[0] / float(sample_rate)
    return audio.tobytes(), sample_rate, audio.shape[1], duration_seconds


async def receive_final(websocket: ClientConnection) -> tuple[dict[str, object], int]:
    preview_count = 0
    while True:
        message = json.loads(await websocket.recv())
        message_type = message.get("type")
        if message_type == "preview":
            preview_count += 1
            continue
        if message_type == "error":
            raise RuntimeError(str(message.get("message") or "Streaming inference failed."))
        if message_type == "final":
            return message, preview_count


async def stream_audio(
    path: Path,
    websocket_url: str,
    chunk_milliseconds: int,
    real_time: bool,
    timeout_seconds: float,
) -> StreamingResult:
    audio, sample_rate, channels, audio_seconds = load_audio(path)
    block_align = channels * 4
    frames_per_chunk = max(1, round(sample_rate * chunk_milliseconds / 1000))
    bytes_per_chunk = frames_per_chunk * block_align

    async with connect(websocket_url, max_size=2**20) as websocket:
        await websocket.send(
            json.dumps(
                {
                    "type": "start",
                    "sampleRate": sample_rate,
                    "channels": channels,
                    "bitsPerSample": 32,
                    "encoding": "ieee-float",
                    "blockAlign": block_align,
                }
            )
        )
        started = json.loads(await websocket.recv())
        if started.get("type") != "started":
            raise RuntimeError(f"Unexpected streaming handshake: {started}")

        receiver = asyncio.create_task(receive_final(websocket))
        send_started_at = perf_counter()
        for offset in range(0, len(audio), bytes_per_chunk):
            await websocket.send(audio[offset : offset + bytes_per_chunk])
            if real_time:
                frames_sent = min(len(audio), offset + bytes_per_chunk) // block_align
                target = send_started_at + (frames_sent / sample_rate)
                delay = target - perf_counter()
                if delay > 0:
                    await asyncio.sleep(delay)

        send_finished_at = perf_counter()
        await websocket.send(json.dumps({"type": "finish"}))
        final, preview_count = await asyncio.wait_for(receiver, timeout=timeout_seconds)
        final_received_at = perf_counter()

    return StreamingResult(
        audio_seconds=audio_seconds,
        sample_rate=sample_rate,
        channels=channels,
        send_milliseconds=(send_finished_at - send_started_at) * 1000,
        finish_to_final_milliseconds=(final_received_at - send_finished_at) * 1000,
        preview_count=preview_count,
        final=final,
    )
