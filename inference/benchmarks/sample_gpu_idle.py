from __future__ import annotations

import argparse
from datetime import datetime
import json
from pathlib import Path
from statistics import mean, median
import subprocess
from time import monotonic, sleep


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    index = min(len(ordered) - 1, round((len(ordered) - 1) * fraction))
    return ordered[index]


def describe(values: list[float]) -> dict[str, float]:
    return {
        "minimum": min(values),
        "mean": mean(values),
        "median": median(values),
        "p95": percentile(values, 0.95),
        "maximum": max(values),
    }


def sample_gpu() -> dict[str, float | str]:
    result = subprocess.run(
        [
            "nvidia-smi",
            "--query-gpu=utilization.gpu,power.draw,memory.used",
            "--format=csv,noheader,nounits",
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    fields = [field.strip() for field in result.stdout.splitlines()[0].split(",")]
    if len(fields) != 3:
        raise RuntimeError(f"Unexpected nvidia-smi output: {result.stdout!r}")
    return {
        "timestamp": datetime.now().astimezone().isoformat(),
        "utilizationPercent": float(fields[0]),
        "powerWatts": float(fields[1]),
        "memoryMiB": float(fields[2]),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--label", required=True)
    parser.add_argument("--duration-seconds", type=float, default=600.0)
    parser.add_argument("--interval-seconds", type=float, default=5.0)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    if args.duration_seconds <= 0 or args.interval_seconds <= 0:
        parser.error("duration and interval must be greater than zero")

    samples: list[dict[str, float | str]] = []
    started_at = monotonic()
    while True:
        samples.append(sample_gpu())
        elapsed = monotonic() - started_at
        if elapsed >= args.duration_seconds:
            break
        sleep(min(args.interval_seconds, args.duration_seconds - elapsed))

    summary = {
        "label": args.label,
        "requestedDurationSeconds": args.duration_seconds,
        "actualDurationSeconds": monotonic() - started_at,
        "intervalSeconds": args.interval_seconds,
        "sampleCount": len(samples),
        "utilizationPercent": describe(
            [float(sample["utilizationPercent"]) for sample in samples]
        ),
        "powerWatts": describe([float(sample["powerWatts"]) for sample in samples]),
        "memoryMiB": describe([float(sample["memoryMiB"]) for sample in samples]),
    }
    report = {"summary": summary, "samples": samples}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
