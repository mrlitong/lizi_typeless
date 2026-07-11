from pathlib import Path

from fastapi.testclient import TestClient
import numpy as np

from lizi_typeless_inference.api import create_app
from lizi_typeless_inference.config import Settings
from lizi_typeless_inference.runtime import Organization, StreamUpdate, Transcription


class FakeRuntime:
    asr_model_name = "fake-asr"
    organizer_model_name = "fake-organizer"
    device_name = "test-device"

    def transcribe(self, audio_path: Path, preview: bool) -> Transcription:
        assert audio_path.exists()
        assert audio_path.read_bytes() == b"RIFF-test"
        return Transcription("Docker and K8s", "Chinese", 12.5)

    def organize(self, text: str) -> Organization:
        return Organization(f"organized: {text}", 7.25)


def settings(tmp_path: Path) -> Settings:
    return Settings(
        asr_model_path=tmp_path / "asr",
        organizer_model_path=tmp_path / "organizer",
        allow_shutdown=False,
    )


def test_health_reports_loaded_local_models(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), runtime_factory=lambda _: FakeRuntime())

    with TestClient(app) as client:
        response = client.get("/v1/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ready",
        "ready": True,
        "asr_model": "fake-asr",
        "organizer_model": "fake-organizer",
        "device": "test-device",
        "streaming": False,
    }


def test_transcription_uses_uploaded_audio_and_returns_timing(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), runtime_factory=lambda _: FakeRuntime())

    with TestClient(app) as client:
        response = client.post(
            "/v1/transcribe",
            files={"audio": ("sample.wav", b"RIFF-test", "audio/wav")},
            data={"preview": "false"},
        )

    assert response.status_code == 200
    assert response.json() == {
        "text": "Docker and K8s",
        "language": "Chinese",
        "duration_milliseconds": 12.5,
    }


def test_organization_returns_text_without_automatic_insertion(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), runtime_factory=lambda _: FakeRuntime())

    with TestClient(app) as client:
        response = client.post("/v1/organize", json={"text": "raw text"})

    assert response.status_code == 200
    assert response.json() == {
        "text": "organized: raw text",
        "duration_milliseconds": 7.25,
    }


def test_blank_organization_input_is_rejected(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), runtime_factory=lambda _: FakeRuntime())

    with TestClient(app) as client:
        response = client.post("/v1/organize", json={"text": "   "})

    assert response.status_code == 422


class StreamingFakeRuntime(FakeRuntime):
    supports_streaming = True

    def start_stream(self) -> dict[str, int]:
        return {"samples": 0}

    def append_stream(self, state: dict[str, int], audio: np.ndarray) -> StreamUpdate:
        state["samples"] += audio.shape[0]
        return StreamUpdate("live preview", "Chinese", 2.0)

    def finish_stream(self, state: dict[str, int]) -> StreamUpdate:
        return StreamUpdate(f"final {state['samples']}", "Chinese", 3.0)


def test_streaming_websocket_converts_pcm_and_returns_final_text(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), runtime_factory=lambda _: StreamingFakeRuntime())
    pcm = np.zeros(16_000, dtype="<i2").tobytes()

    with TestClient(app) as client, client.websocket_connect("/v1/stream") as websocket:
        websocket.send_json(
            {
                "type": "start",
                "sampleRate": 16_000,
                "channels": 1,
                "bitsPerSample": 16,
                "encoding": "pcm",
                "blockAlign": 2,
            }
        )
        assert websocket.receive_json() == {"type": "started"}
        websocket.send_bytes(pcm)
        assert websocket.receive_json()["type"] == "preview"
        websocket.send_json({"type": "finish"})
        final = websocket.receive_json()

    assert final["type"] == "final"
    assert final["text"].startswith("final ")
    assert final["durationMilliseconds"] == 5.0
    assert final["finishMilliseconds"] == 3.0
