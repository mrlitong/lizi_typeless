from lizi_typeless_inference.prompt import (
    ORGANIZER_EXAMPLES,
    ORGANIZER_SYSTEM_PROMPT,
    collapse_whole_text_repetition,
)


def test_prompt_contains_every_product_safety_boundary() -> None:
    required_rules = [
        "忠实保留",
        "每一条有意义信息",
        "只有用户明确改口",
        "明确列举",
        "不回答",
        "不补充事实",
        "不扩写",
        "只输出整理后的文字",
    ]

    for rule in required_rules:
        assert rule in ORGANIZER_SYSTEM_PROMPT


def test_examples_include_question_and_explicit_correction() -> None:
    assert any(source.endswith("？") and output == source for source, output in ORGANIZER_EXAMPLES)
    assert any("不是 Docker" in source and "/home/litong" in output for source, output in ORGANIZER_EXAMPLES)
    assert any(source.count("请检查 Docker 服务") == 3 for source, _ in ORGANIZER_EXAMPLES)
    assert any("第一" in source and "1." in output for source, output in ORGANIZER_EXAMPLES)
    assert any("斜杠 home" in source and "/home/litong/logs" in output for source, output in ORGANIZER_EXAMPLES)


def test_only_whole_text_obvious_repetition_is_collapsed() -> None:
    repeated = "请检查 Docker 服务，请检查 Docker 服务，请检查 Docker 服务。"

    assert collapse_whole_text_repetition(repeated) == "请检查 Docker 服务。"
    assert collapse_whole_text_repetition("请检查 Docker 服务，请检查 Docker 服务。") != "请检查 Docker 服务。"
    assert collapse_whole_text_repetition(repeated + "然后发布。") == repeated + "然后发布。"
    assert collapse_whole_text_repetition("好，好，好。") == "好，好，好。"
