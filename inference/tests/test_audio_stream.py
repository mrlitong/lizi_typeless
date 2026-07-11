import numpy as np

from lizi_typeless_inference.audio_stream import PcmFormat, StreamingAudioConverter


def test_float_stereo_is_downmixed_and_resampled_across_odd_chunks() -> None:
    sample_rate = 48_000
    time = np.arange(sample_rate, dtype=np.float32) / sample_rate
    mono = np.sin(2 * np.pi * 440 * time).astype(np.float32)
    stereo = np.column_stack((mono, mono)).astype("<f4").tobytes()
    converter = StreamingAudioConverter(
        PcmFormat(sample_rate, 2, 32, "ieee-float", block_align=8)
    )

    output = np.concatenate(
        (
            converter.append(stereo[:12_347]),
            converter.append(stereo[12_347:]),
            converter.finish(),
        )
    )

    assert abs(output.shape[0] - 16_000) <= 1
    assert np.max(np.abs(output)) > 0.95


def test_pcm16_is_normalized() -> None:
    converter = StreamingAudioConverter(PcmFormat(16_000, 1, 16, "pcm", block_align=2))
    samples = np.array([-32768, 0, 32767], dtype="<i2")

    output = np.concatenate((converter.append(samples.tobytes()), converter.finish()))

    assert np.allclose(output, [-1.0, 0.0, 32767 / 32768], atol=1e-4)
