from __future__ import annotations

import argparse
import asyncio
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
from statistics import median
from time import perf_counter
from typing import Any

import httpx

from lizi_typeless_inference.evaluation import (
    character_error_counts,
    normalize_text,
    technical_term_hits,
)
from streaming_client import stream_audio


@dataclass(frozen=True, slots=True)
class Sample:
    id: str
    audio_path: Path
    reference: str
    mandarin: bool
    technical_terms: tuple[str, ...]
    expected_organized: str | None


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    index = min(len(ordered) - 1, round((len(ordered) - 1) * fraction))
    return ordered[index]


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def load_manifest(path: Path, minimum_samples: int) -> list[Sample]:
    samples: list[Sample] = []
    seen_ids: set[str] = set()
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue
        try:
            value = json.loads(line)
            if not isinstance(value, dict):
                raise TypeError("each line must contain a JSON object")
            if not all(isinstance(value.get(field), str) for field in ("id", "audio", "reference")):
                raise TypeError("id, audio, and reference must be strings")
            sample_id = value["id"].strip()
            reference = value["reference"].strip()
            audio_value = Path(value["audio"])
            mandarin = value.get("mandarin", True)
            if not isinstance(mandarin, bool):
                raise TypeError("mandarin must be a boolean")
            terms_value = value.get("technicalTerms", [])
            if not isinstance(terms_value, list) or not all(
                isinstance(term, str) for term in terms_value
            ):
                raise TypeError("technicalTerms must be an array of strings")
            technical_terms = tuple(terms_value)
            expected = value.get("expectedOrganized")
            if expected is not None and not isinstance(expected, str):
                raise TypeError("expectedOrganized must be a string or null")
            expected_organized = expected.strip() if expected is not None else None
        except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exception:
            raise ValueError(f"Invalid manifest line {line_number}: {exception}") from exception

        if not sample_id or not reference:
            raise ValueError(f"Manifest line {line_number} has an empty id or reference.")
        if sample_id in seen_ids:
            raise ValueError(f"Duplicate sample id: {sample_id}")
        seen_ids.add(sample_id)

        audio_path = audio_value if audio_value.is_absolute() else path.parent / audio_value
        audio_path = audio_path.resolve()
        if not audio_path.is_file():
            raise FileNotFoundError(f"Audio file for sample {sample_id} is missing: {audio_path}")
        samples.append(
            Sample(
                id=sample_id,
                audio_path=audio_path,
                reference=reference,
                mandarin=mandarin,
                technical_terms=technical_terms,
                expected_organized=expected_organized,
            )
        )

    if len(samples) < minimum_samples:
        raise ValueError(
            f"The manifest has {len(samples)} samples; at least {minimum_samples} are required."
        )
    return samples


def evaluate_sample(
    client: httpx.Client,
    sample: Sample,
    websocket_url: str,
    chunk_milliseconds: int,
    timeout_seconds: float,
) -> dict[str, Any]:
    streaming = asyncio.run(
        stream_audio(
            sample.audio_path,
            websocket_url,
            chunk_milliseconds,
            real_time=False,
            timeout_seconds=timeout_seconds,
        )
    )
    transcription = streaming.final

    organization_started_at = perf_counter()
    response = client.post("/v1/organize", json={"text": transcription["text"]})
    response.raise_for_status()
    organization_wall = (perf_counter() - organization_started_at) * 1000
    organization = response.json()

    errors, reference_characters = character_error_counts(
        sample.reference,
        transcription["text"],
    )
    raw_term_hits = technical_term_hits(transcription["text"], sample.technical_terms)
    final_term_hits = technical_term_hits(organization["text"], sample.technical_terms)
    organized_normalized_match = (
        normalize_text(organization["text"]) == normalize_text(sample.expected_organized)
        if sample.expected_organized is not None
        else None
    )
    return {
        "id": sample.id,
        "audio": str(sample.audio_path),
        "audioSha256": file_sha256(sample.audio_path),
        "mandarin": sample.mandarin,
        "reference": sample.reference,
        "rawText": transcription["text"],
        "organizedText": organization["text"],
        "expectedOrganized": sample.expected_organized,
        "organizedExactMatch": (
            organization["text"].strip() == sample.expected_organized
            if sample.expected_organized is not None
            else None
        ),
        "organizedNormalizedMatch": organized_normalized_match,
        "language": transcription["language"],
        "characterErrors": errors,
        "referenceCharacters": reference_characters,
        "cer": errors / reference_characters,
        "rawTechnicalTermHits": raw_term_hits,
        "technicalTermHits": final_term_hits,
        "transcriptionWallMilliseconds": (
            streaming.send_milliseconds + streaming.finish_to_final_milliseconds
        ),
        "transcriptionComputeMilliseconds": transcription["durationMilliseconds"],
        "streamingPreviewCount": streaming.preview_count,
        "organizationWallMilliseconds": organization_wall,
        "organizationComputeMilliseconds": organization["duration_milliseconds"],
    }


def summarize(results: list[dict[str, Any]]) -> dict[str, Any]:
    total_errors = sum(result["characterErrors"] for result in results)
    total_characters = sum(result["referenceCharacters"] for result in results)
    mandarin_results = [result for result in results if result["mandarin"]]
    mandarin_errors = sum(result["characterErrors"] for result in mandarin_results)
    mandarin_characters = sum(result["referenceCharacters"] for result in mandarin_results)
    term_values = [
        hit
        for result in results
        for hit in result["technicalTermHits"].values()
    ]
    raw_term_values = [
        hit
        for result in results
        for hit in result["rawTechnicalTermHits"].values()
    ]
    exact_values = [
        result["organizedExactMatch"]
        for result in results
        if result["organizedExactMatch"] is not None
    ]
    normalized_values = [
        result["organizedNormalizedMatch"]
        for result in results
        if result["organizedNormalizedMatch"] is not None
    ]
    transcription_times = [result["transcriptionWallMilliseconds"] for result in results]
    organization_times = [result["organizationWallMilliseconds"] for result in results]
    return {
        "sampleCount": len(results),
        "overallCer": total_errors / total_characters,
        "mandarinSampleCount": len(mandarin_results),
        "mandarinCer": (
            mandarin_errors / mandarin_characters if mandarin_characters else None
        ),
        "technicalTermCount": len(term_values),
        "rawTechnicalTermAccuracy": (
            sum(raw_term_values) / len(raw_term_values) if raw_term_values else None
        ),
        "technicalTermAccuracy": sum(term_values) / len(term_values) if term_values else None,
        "organizedExpectedCount": len(exact_values),
        "organizedExactMatchRate": sum(exact_values) / len(exact_values) if exact_values else None,
        "organizedNormalizedMatchRate": (
            sum(normalized_values) / len(normalized_values) if normalized_values else None
        ),
        "transcriptionWallP50Milliseconds": median(transcription_times),
        "transcriptionWallP95Milliseconds": percentile(transcription_times, 0.95),
        "organizationWallP50Milliseconds": median(organization_times),
        "organizationWallP95Milliseconds": percentile(organization_times, 0.95),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--base-url", default="http://127.0.0.1:8765")
    parser.add_argument("--websocket-url", default="ws://127.0.0.1:8765/v1/stream")
    parser.add_argument("--output", type=Path, default=Path("artifacts/voice-benchmark.json"))
    parser.add_argument("--minimum-samples", type=int, default=50)
    parser.add_argument("--chunk-ms", type=int, default=20)
    parser.add_argument("--timeout-seconds", type=float, default=300.0)
    args = parser.parse_args()

    if args.minimum_samples < 1:
        parser.error("--minimum-samples must be at least one")
    if args.chunk_ms <= 0:
        parser.error("--chunk-ms must be greater than zero")
    manifest = args.manifest.resolve()
    samples = load_manifest(manifest, args.minimum_samples)
    with httpx.Client(base_url=args.base_url, timeout=args.timeout_seconds) as client:
        health_response = client.get("/v1/health")
        health_response.raise_for_status()
        results = [
            evaluate_sample(
                client,
                sample,
                args.websocket_url,
                args.chunk_ms,
                args.timeout_seconds,
            )
            for sample in samples
        ]

    report = {
        "manifest": str(manifest),
        "manifestSha256": file_sha256(manifest),
        "service": health_response.json(),
        "streamingChunkMilliseconds": args.chunk_ms,
        "summary": summarize(results),
        "samples": results,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(report["summary"], ensure_ascii=False, indent=2))
    print(f"Report: {args.output.resolve()}")


if __name__ == "__main__":
    main()
