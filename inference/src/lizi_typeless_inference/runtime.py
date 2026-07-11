from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path
from threading import Lock
from time import perf_counter
from typing import Any, Protocol

import numpy as np

from .config import Settings
from .prompt import (
    ORGANIZER_EXAMPLES,
    ORGANIZER_SYSTEM_PROMPT,
    collapse_whole_text_repetition,
)
from .vocabulary import correct_personal_vocabulary


ASR_CONTEXT = "Docker K8s Kubernetes Claude Code Codex Azure WSL2 RTX 5090 VS Code Windows Terminal"
MAX_ASR_NEW_TOKENS = 512


@dataclass(frozen=True, slots=True)
class Transcription:
    text: str
    language: str
    duration_milliseconds: float


@dataclass(frozen=True, slots=True)
class Organization:
    text: str
    duration_milliseconds: float


@dataclass(frozen=True, slots=True)
class StreamUpdate:
    text: str
    language: str
    duration_milliseconds: float


class InferenceRuntime(Protocol):
    @property
    def asr_model_name(self) -> str: ...

    @property
    def organizer_model_name(self) -> str: ...

    @property
    def device_name(self) -> str: ...

    @property
    def supports_streaming(self) -> bool: ...

    def transcribe(self, audio_path: Path, preview: bool) -> Transcription: ...

    def organize(self, text: str) -> Organization: ...

    def start_stream(self) -> Any: ...

    def append_stream(self, state: Any, audio: np.ndarray) -> StreamUpdate: ...

    def finish_stream(self, state: Any) -> StreamUpdate: ...


class QwenOrganizer:
    def __init__(self, model_path: Path, device: str, torch: Any, lock: Lock) -> None:
        from transformers import AutoModelForCausalLM, AutoTokenizer

        self._torch = torch
        self._lock = lock
        self._tokenizer = AutoTokenizer.from_pretrained(
            str(model_path),
            local_files_only=True,
        )
        self._model = AutoModelForCausalLM.from_pretrained(
            str(model_path),
            dtype=torch.bfloat16,
            device_map=device,
            local_files_only=True,
            attn_implementation="sdpa",
        )
        self._model.eval()
        self.organize("Warmup.")

    def organize(self, text: str) -> Organization:
        started_at = perf_counter()
        text = collapse_whole_text_repetition(text)
        messages = [{"role": "system", "content": ORGANIZER_SYSTEM_PROMPT}]
        for source, expected in ORGANIZER_EXAMPLES:
            messages.extend(
                (
                    {"role": "user", "content": f"<原文>\n{source}\n</原文>"},
                    {"role": "assistant", "content": expected},
                )
            )
        messages.append({"role": "user", "content": f"<原文>\n{text}\n</原文>"})
        prompt = self._tokenizer.apply_chat_template(
            messages,
            tokenize=False,
            add_generation_prompt=True,
            enable_thinking=False,
        )
        model_inputs = self._tokenizer([prompt], return_tensors="pt").to(self._model.device)
        with self._lock, self._torch.inference_mode():
            generated = self._model.generate(
                **model_inputs,
                max_new_tokens=512,
                do_sample=False,
            )
        response_tokens = generated[0][model_inputs.input_ids.shape[1] :]
        organized = self._tokenizer.decode(response_tokens, skip_special_tokens=True).strip()
        return Organization(
            text=organized,
            duration_milliseconds=(perf_counter() - started_at) * 1000,
        )


class QwenRuntime:
    def __init__(self, settings: Settings) -> None:
        import torch
        from qwen_asr import Qwen3ASRModel

        _validate_environment(settings, torch)
        self._torch = torch
        self._lock = Lock()
        self._asr_model_path = settings.asr_model_path.resolve()
        self._organizer_model_path = settings.organizer_model_path.resolve()
        self._asr = Qwen3ASRModel.from_pretrained(
            str(self._asr_model_path),
            dtype=torch.bfloat16,
            device_map=settings.device,
            max_inference_batch_size=1,
            max_new_tokens=MAX_ASR_NEW_TOKENS,
        )
        self._organizer = QwenOrganizer(
            self._organizer_model_path,
            settings.device,
            torch,
            self._lock,
        )

    @property
    def asr_model_name(self) -> str:
        return self._asr_model_path.name

    @property
    def organizer_model_name(self) -> str:
        return self._organizer_model_path.name

    @property
    def device_name(self) -> str:
        return self._torch.cuda.get_device_name(0)

    @property
    def supports_streaming(self) -> bool:
        return False

    def transcribe(self, audio_path: Path, preview: bool) -> Transcription:
        started_at = perf_counter()
        with self._lock, self._torch.inference_mode():
            results = self._asr.transcribe(
                audio=str(audio_path),
                language=None,
                context=ASR_CONTEXT,
                return_time_stamps=False,
            )
        return _single_transcription(results, started_at)

    def organize(self, text: str) -> Organization:
        return self._organizer.organize(text)

    def start_stream(self) -> Any:
        raise RuntimeError("The transformers backend does not support streaming.")

    def append_stream(self, state: Any, audio: np.ndarray) -> StreamUpdate:
        raise RuntimeError("The transformers backend does not support streaming.")

    def finish_stream(self, state: Any) -> StreamUpdate:
        raise RuntimeError("The transformers backend does not support streaming.")


class QwenVllmRuntime:
    def __init__(self, settings: Settings) -> None:
        os.environ.setdefault("HF_HUB_OFFLINE", "1")
        os.environ.setdefault("TRANSFORMERS_OFFLINE", "1")
        import torch
        from qwen_asr import Qwen3ASRModel

        _validate_environment(settings, torch)
        self._torch = torch
        self._lock = Lock()
        self._asr_model_path = settings.asr_model_path.resolve()
        self._organizer_model_path = settings.organizer_model_path.resolve()
        self._asr = Qwen3ASRModel.LLM(
            model=str(self._asr_model_path),
            gpu_memory_utilization=0.50,
            max_model_len=4096,
            max_new_tokens=MAX_ASR_NEW_TOKENS,
            enforce_eager=True,
            dtype="bfloat16",
            load_format="safetensors",
        )
        self._warm_streaming_path()
        self._organizer = QwenOrganizer(
            self._organizer_model_path,
            settings.device,
            torch,
            self._lock,
        )

    @property
    def asr_model_name(self) -> str:
        return self._asr_model_path.name

    @property
    def organizer_model_name(self) -> str:
        return self._organizer_model_path.name

    @property
    def device_name(self) -> str:
        return self._torch.cuda.get_device_name(0)

    @property
    def supports_streaming(self) -> bool:
        return True

    def transcribe(self, audio_path: Path, preview: bool) -> Transcription:
        started_at = perf_counter()
        with self._lock:
            results = self._asr.transcribe(
                audio=str(audio_path),
                language=None,
                context=ASR_CONTEXT,
                return_time_stamps=False,
            )
        return _single_transcription(results, started_at)

    def organize(self, text: str) -> Organization:
        return self._organizer.organize(text)

    def start_stream(self) -> Any:
        with self._lock:
            return self._asr.init_streaming_state(
                context=ASR_CONTEXT,
                unfixed_chunk_num=2,
                unfixed_token_num=5,
                chunk_size_sec=2.0,
            )

    def append_stream(self, state: Any, audio: np.ndarray) -> StreamUpdate:
        started_at = perf_counter()
        with self._lock:
            self._asr.streaming_transcribe(audio, state)
        return StreamUpdate(
            text=correct_personal_vocabulary(state.text),
            language=state.language,
            duration_milliseconds=(perf_counter() - started_at) * 1000,
        )

    def finish_stream(self, state: Any) -> StreamUpdate:
        started_at = perf_counter()
        with self._lock:
            self._asr.finish_streaming_transcribe(state)
        return StreamUpdate(
            text=correct_personal_vocabulary(state.text),
            language=state.language,
            duration_milliseconds=(perf_counter() - started_at) * 1000,
        )

    def _warm_streaming_path(self) -> None:
        state = self._asr.init_streaming_state(chunk_size_sec=2.0)
        silence = np.zeros(16_000, dtype=np.float32)
        self._asr.streaming_transcribe(silence, state)
        self._asr.streaming_transcribe(silence, state)
        self._asr.finish_streaming_transcribe(state)


def _validate_environment(settings: Settings, torch: Any) -> None:
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA is not available to PyTorch inside WSL2.")
    if not settings.asr_model_path.is_dir():
        raise FileNotFoundError(
            f"ASR model is missing: {settings.asr_model_path}. Run scripts/download-models.sh first."
        )
    if not settings.organizer_model_path.is_dir():
        raise FileNotFoundError(
            f"Organizer model is missing: {settings.organizer_model_path}. "
            "Run scripts/download-models.sh first."
        )


def _single_transcription(results: list[Any], started_at: float) -> Transcription:
    if len(results) != 1:
        raise RuntimeError(f"Expected one ASR result, received {len(results)}.")
    result = results[0]
    return Transcription(
        text=correct_personal_vocabulary(result.text.strip()),
        language=(result.language or "").strip(),
        duration_milliseconds=(perf_counter() - started_at) * 1000,
    )
