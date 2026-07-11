from __future__ import annotations

import argparse
import json
from pathlib import Path


STATISTICS = ("minimum", "mean", "median", "p95", "maximum")
METRICS = ("utilizationPercent", "powerWatts", "memoryMiB")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--maximum-power-increase-watts", type=float, default=10.0)
    args = parser.parse_args()

    baseline = json.loads(args.baseline.read_text(encoding="utf-8"))["summary"]
    candidate = json.loads(args.candidate.read_text(encoding="utf-8"))["summary"]
    deltas = {
        metric: {
            statistic: candidate[metric][statistic] - baseline[metric][statistic]
            for statistic in STATISTICS
        }
        for metric in METRICS
    }
    report = {
        "baseline": baseline,
        "candidate": candidate,
        "delta": deltas,
        "acceptance": {
            "maximumPowerIncreaseWatts": args.maximum_power_increase_watts,
            "medianPowerIncreaseWatts": deltas["powerWatts"]["median"],
            "meanPowerIncreaseWatts": deltas["powerWatts"]["mean"],
            "powerTargetPassed": (
                deltas["powerWatts"]["median"] <= args.maximum_power_increase_watts
                and deltas["powerWatts"]["mean"] <= args.maximum_power_increase_watts
            ),
            "utilizationMedianIncreasePercent": deltas["utilizationPercent"]["median"],
            "utilizationP95IncreasePercent": deltas["utilizationPercent"]["p95"],
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(report["acceptance"], ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
