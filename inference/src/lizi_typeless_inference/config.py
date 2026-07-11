from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import os


PROJECT_ROOT = Path(__file__).resolve().parents[3]


@dataclass(frozen=True, slots=True)
class Settings:
    asr_model_path: Path
    organizer_model_path: Path
    asr_backend: str = "transformers"
    host: str = "127.0.0.1"
    port: int = 8765
    device: str = "cuda:0"
    max_audio_bytes: int = 32 * 1024 * 1024
    allow_shutdown: bool = True

    @classmethod
    def from_environment(cls) -> "Settings":
        return cls(
            asr_model_path=Path(
                os.environ.get(
                    "LIZI_TYPELESS_ASR_MODEL",
                    PROJECT_ROOT / "models" / "Qwen3-ASR-1.7B",
                )
            ),
            organizer_model_path=Path(
                os.environ.get(
                    "LIZI_TYPELESS_ORGANIZER_MODEL",
                    PROJECT_ROOT / "models" / "Qwen3-1.7B",
                )
            ),
            asr_backend=os.environ.get("LIZI_TYPELESS_ASR_BACKEND", "transformers"),
            host=os.environ.get("LIZI_TYPELESS_HOST", "127.0.0.1"),
            port=int(os.environ.get("LIZI_TYPELESS_PORT", "8765")),
            device=os.environ.get("LIZI_TYPELESS_DEVICE", "cuda:0"),
        )
