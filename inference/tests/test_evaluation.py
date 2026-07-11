from lizi_typeless_inference.evaluation import (
    character_error_counts,
    normalize_text,
    technical_term_hits,
)


def test_normalize_text_keeps_words_numbers_and_chinese_characters() -> None:
    assert normalize_text(" Docker、K8s 路径！ ") == "dockerk8s路径"
    assert normalize_text("Python � �") == "python��"


def test_character_error_counts_returns_aggregate_ready_values() -> None:
    assert character_error_counts("你好，Docker。", "你好 Docker") == (0, 8)
    assert character_error_counts("你好", "你们好") == (1, 2)


def test_technical_term_hits_are_case_and_separator_insensitive() -> None:
    hits = technical_term_hits("Use Claude-Code with K8S.", ["Claude Code", "K8s", "Azure"])

    assert hits == {"Claude Code": True, "K8s": True, "Azure": False}
