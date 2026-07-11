from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager
import json
from pathlib import Path
import os
import signal
import tempfile
from typing import AsyncIterator, Callable

from fastapi import FastAPI, File, Form, HTTPException, UploadFile, WebSocket, WebSocketDisconnect
from pydantic import BaseModel, Field

from .config import Settings
from .audio_stream import PcmFormat, StreamingAudioConverter
from .runtime import InferenceRuntime, QwenRuntime, QwenVllmRuntime


class HealthResponse(BaseModel):
    status: str
    ready: bool
    asr_model: str
    organizer_model: str
    device: str
    streaming: bool


class TranscriptionResponse(BaseModel):
    text: str
    language: str
    duration_milliseconds: float


class OrganizationRequest(BaseModel):
    text: str = Field(min_length=1, max_length=20_000)


class OrganizationResponse(BaseModel):
    text: str
    duration_milliseconds: float


RuntimeFactory = Callable[[Settings], InferenceRuntime]


def create_app(
    settings: Settings | None = None,
    runtime_factory: RuntimeFactory | None = None,
) -> FastAPI:
    active_settings = settings or Settings.from_environment()
    selected_factory = runtime_factory or (
        QwenVllmRuntime if active_settings.asr_backend == "vllm" else QwenRuntime
    )

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        app.state.runtime = await asyncio.to_thread(selected_factory, active_settings)
        yield

    app = FastAPI(
        title="lizi_typeless Local Inference",
        version="0.1.0",
        lifespan=lifespan,
    )

    @app.get("/v1/health", response_model=HealthResponse)
    async def health() -> HealthResponse:
        runtime = _runtime(app)
        return HealthResponse(
            status="ready",
            ready=True,
            asr_model=runtime.asr_model_name,
            organizer_model=runtime.organizer_model_name,
            device=runtime.device_name,
            streaming=getattr(runtime, "supports_streaming", False),
        )

    @app.websocket("/v1/stream")
    async def stream(websocket: WebSocket) -> None:
        await websocket.accept()
        runtime = _runtime(app)
        if not getattr(runtime, "supports_streaming", False):
            await websocket.send_json(
                {"type": "error", "message": "The active ASR backend does not support streaming."}
            )
            await websocket.close(code=1003)
            return

        try:
            start = await websocket.receive_json()
            if start.get("type") != "start":
                raise ValueError("The first streaming message must be a start message.")
            converter = StreamingAudioConverter(
                PcmFormat(
                    sample_rate=int(start["sampleRate"]),
                    channels=int(start["channels"]),
                    bits_per_sample=int(start["bitsPerSample"]),
                    encoding=str(start["encoding"]),
                    block_align=int(start["blockAlign"]),
                )
            )
            state = await asyncio.to_thread(runtime.start_stream)
            await websocket.send_json({"type": "started"})

            last_preview = ""
            compute_milliseconds = 0.0
            while True:
                message = await websocket.receive()
                if message["type"] == "websocket.disconnect":
                    return
                if message.get("bytes") is not None:
                    pcm = converter.append(message["bytes"])
                    if pcm.size == 0:
                        continue
                    update = await asyncio.to_thread(runtime.append_stream, state, pcm)
                    compute_milliseconds += update.duration_milliseconds
                    if update.text and update.text != last_preview:
                        last_preview = update.text
                        await websocket.send_json(
                            {
                                "type": "preview",
                                "text": update.text,
                                "language": update.language,
                            }
                        )
                    continue

                payload = json.loads(message.get("text") or "{}")
                if payload.get("type") == "cancel":
                    await websocket.close(code=1000)
                    return
                if payload.get("type") != "finish":
                    raise ValueError("Unknown streaming message type.")

                tail = converter.finish()
                if tail.size:
                    update = await asyncio.to_thread(runtime.append_stream, state, tail)
                    compute_milliseconds += update.duration_milliseconds
                final = await asyncio.to_thread(runtime.finish_stream, state)
                compute_milliseconds += final.duration_milliseconds
                await websocket.send_json(
                    {
                        "type": "final",
                        "text": final.text,
                        "language": final.language,
                        "durationMilliseconds": compute_milliseconds,
                        "finishMilliseconds": final.duration_milliseconds,
                    }
                )
                await websocket.close(code=1000)
                return
        except WebSocketDisconnect:
            return
        except Exception as exception:
            await websocket.send_json({"type": "error", "message": str(exception)})
            await websocket.close(code=1011)

    @app.post("/v1/transcribe", response_model=TranscriptionResponse)
    async def transcribe(
        audio: UploadFile = File(...),
        preview: bool = Form(False),
    ) -> TranscriptionResponse:
        suffix = Path(audio.filename or "audio.wav").suffix or ".wav"
        temporary_path: Path | None = None
        try:
            with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as temporary:
                temporary_path = Path(temporary.name)
                total = 0
                while chunk := await audio.read(1024 * 1024):
                    total += len(chunk)
                    if total > active_settings.max_audio_bytes:
                        raise HTTPException(status_code=413, detail="Audio exceeds the one-minute MVP limit.")
                    temporary.write(chunk)

            if total == 0:
                raise HTTPException(status_code=400, detail="Audio file is empty.")
            result = await asyncio.to_thread(_runtime(app).transcribe, temporary_path, preview)
            return TranscriptionResponse(
                text=result.text,
                language=result.language,
                duration_milliseconds=result.duration_milliseconds,
            )
        finally:
            await audio.close()
            if temporary_path is not None:
                temporary_path.unlink(missing_ok=True)

    @app.post("/v1/organize", response_model=OrganizationResponse)
    async def organize(request: OrganizationRequest) -> OrganizationResponse:
        if not request.text.strip():
            raise HTTPException(status_code=422, detail="Text must not be blank.")
        result = await asyncio.to_thread(_runtime(app).organize, request.text)
        if not result.text:
            raise HTTPException(status_code=502, detail="Organizer returned empty text.")
        return OrganizationResponse(
            text=result.text,
            duration_milliseconds=result.duration_milliseconds,
        )

    @app.post("/v1/shutdown")
    async def shutdown() -> dict[str, str]:
        if not active_settings.allow_shutdown:
            raise HTTPException(status_code=403, detail="Shutdown is disabled.")
        loop = asyncio.get_running_loop()
        loop.call_later(0.2, os.kill, os.getpid(), signal.SIGTERM)
        return {"status": "stopping"}

    return app


def _runtime(app: FastAPI) -> InferenceRuntime:
    return app.state.runtime
