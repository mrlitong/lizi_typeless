from __future__ import annotations

import argparse
from datetime import datetime
import json
from pathlib import Path
from statistics import median
from typing import Any


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    index = min(len(ordered) - 1, round((len(ordered) - 1) * fraction))
    return ordered[index]


def describe(values: list[float]) -> dict[str, float | int] | None:
    if not values:
        return None
    return {
        "count": len(values),
        "p50Milliseconds": median(values),
        "p95Milliseconds": percentile(values, 0.95),
        "maxMilliseconds": max(values),
    }


def recording_seconds(session: dict[str, Any]) -> float | None:
    if session.get("stoppedAt") is None:
        return None
    started = datetime.fromisoformat(session["startedAt"])
    stopped = datetime.fromisoformat(session["stoppedAt"])
    return (stopped - started).total_seconds()


def load_sessions(root: Path) -> list[dict[str, Any]]:
    sessions = []
    for path in root.glob("*/session.json"):
        value = json.loads(path.read_text(encoding="utf-8-sig"))
        value["metadataPath"] = str(path)
        sessions.append(value)
    return sessions


def end_to_insertion_values(
    sessions: list[dict[str, Any]],
    minimum_seconds: float,
    maximum_seconds: float,
) -> list[float]:
    values = []
    for session in sessions:
        duration = recording_seconds(session)
        timing = session.get("timings", {}).get("endToInsertionMilliseconds")
        if (
            session.get("status") == "completed"
            and duration is not None
            and minimum_seconds <= duration <= maximum_seconds
            and timing is not None
        ):
            values.append(float(timing))
    return values


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("sessions", type=Path)
    parser.add_argument("--minimum-per-group", type=int, default=30)
    parser.add_argument("--allow-partial", action="store_true")
    parser.add_argument("--output", type=Path, default=Path("artifacts/session-timings.json"))
    args = parser.parse_args()

    if args.minimum_per_group < 1:
        parser.error("--minimum-per-group must be at least one")
    root = args.sessions.resolve()
    if not root.is_dir():
        parser.error(f"session directory does not exist: {root}")

    sessions = load_sessions(root)
    short_values = end_to_insertion_values(sessions, 5.0, 15.0)
    long_values = end_to_insertion_values(sessions, 45.0, 60.0)
    if not args.allow_partial and (
        len(short_values) < args.minimum_per_group or len(long_values) < args.minimum_per_group
    ):
        raise ValueError(
            "Not enough completed timed sessions: "
            f"short={len(short_values)}, long={len(long_values)}, "
            f"required={args.minimum_per_group}."
        )

    capture_values = [
        float(timing)
        for session in sessions
        if session.get("timings", {}).get("firstAudioFrameMilliseconds") is not None
        and (timing := session.get("timings", {}).get("captureStartMilliseconds")) is not None
    ]
    first_frame_values = [
        float(timing)
        for session in sessions
        if (timing := session.get("timings", {}).get("firstAudioFrameMilliseconds")) is not None
    ]
    report = {
        "sessionsDirectory": str(root),
        "totalSessionCount": len(sessions),
        "partialReport": args.allow_partial,
        "requiredPerGroup": args.minimum_per_group,
        "captureStart": describe(capture_values),
        "firstAudioFrame": describe(first_frame_values),
        "shortEndToInsertion": describe(short_values),
        "longEndToInsertion": describe(long_values),
        "shortLongP50DifferenceMilliseconds": (
            abs(median(short_values) - median(long_values))
            if short_values and long_values
            else None
        ),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
