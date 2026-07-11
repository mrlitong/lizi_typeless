from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import soxr


@dataclass(frozen=True, slots=True)
class PcmFormat:
    sample_rate: int
    channels: int
    bits_per_sample: int
    encoding: str
    block_align: int


class StreamingAudioConverter:
    def __init__(self, format: PcmFormat) -> None:
        if format.sample_rate < 8_000 or format.sample_rate > 192_000:
            raise ValueError(f"Unsupported sample rate: {format.sample_rate}.")
        if format.channels < 1 or format.channels > 8:
            raise ValueError(f"Unsupported channel count: {format.channels}.")
        expected_align = format.channels * ((format.bits_per_sample + 7) // 8)
        if format.block_align != expected_align:
            raise ValueError(
                f"Block alignment {format.block_align} does not match audio format {expected_align}."
            )
        if (format.encoding, format.bits_per_sample) not in {
            ("ieee-float", 32),
            ("pcm", 16),
            ("pcm", 24),
            ("pcm", 32),
        }:
            raise ValueError(
                f"Unsupported PCM format: {format.encoding}, {format.bits_per_sample}-bit."
            )

        self._format = format
        self._remainder = b""
        self._resampler = soxr.ResampleStream(
            format.sample_rate,
            16_000,
            num_channels=1,
            dtype="float32",
            quality="HQ",
        )

    def append(self, chunk: bytes) -> np.ndarray:
        data = self._remainder + chunk
        usable_length = len(data) - (len(data) % self._format.block_align)
        self._remainder = data[usable_length:]
        if usable_length == 0:
            return np.zeros(0, dtype=np.float32)

        samples = self._decode(data[:usable_length])
        mono = samples.reshape(-1, self._format.channels).mean(axis=1, dtype=np.float32)
        return self._resampler.resample_chunk(mono.astype(np.float32, copy=False), last=False)

    def finish(self) -> np.ndarray:
        if self._remainder:
            raise ValueError("The final audio frame is incomplete.")
        return self._resampler.resample_chunk(np.zeros(0, dtype=np.float32), last=True)

    def _decode(self, data: bytes) -> np.ndarray:
        if self._format.encoding == "ieee-float":
            return np.frombuffer(data, dtype="<f4")
        if self._format.bits_per_sample == 16:
            return np.frombuffer(data, dtype="<i2").astype(np.float32) / 32768.0
        if self._format.bits_per_sample == 32:
            return np.frombuffer(data, dtype="<i4").astype(np.float32) / 2147483648.0

        packed = np.frombuffer(data, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        values = packed[:, 0] | (packed[:, 1] << 8) | (packed[:, 2] << 16)
        values = np.where(values & 0x800000, values - 0x1000000, values)
        return values.astype(np.float32) / 8388608.0
