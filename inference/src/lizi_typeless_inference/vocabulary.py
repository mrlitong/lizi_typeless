from __future__ import annotations

import re


_SPOKEN_LOG_PATH = re.compile(
    r"斜杠\s*home\s*斜杠\s*(?:litong|立通|利通)\s*斜杠\s*logs(?:\s*目录下面)?",
    re.IGNORECASE,
)


def correct_personal_vocabulary(text: str) -> str:
    corrected = text.replace("Cloud Code", "Claude Code")
    return _SPOKEN_LOG_PATH.sub("/home/litong/logs", corrected)
