from lizi_typeless_inference.prompt import (
    ORGANIZER_EXAMPLES,
    ORGANIZER_SYSTEM_PROMPT,
    choose_faithful_output,
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
        "无法确定时原样保留",
        "否定限制必须完整保留",
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
    assert any("幺二七" in source and output == source for source, output in ORGANIZER_EXAMPLES)
    assert any(source.count("不要") == 2 and output == source for source, output in ORGANIZER_EXAMPLES)


def test_only_whole_text_obvious_repetition_is_collapsed() -> None:
    repeated = "请检查 Docker 服务，请检查 Docker 服务，请检查 Docker 服务。"

    assert collapse_whole_text_repetition(repeated) == "请检查 Docker 服务。"
    assert collapse_whole_text_repetition("请检查 Docker 服务，请检查 Docker 服务。") != "请检查 Docker 服务。"
    assert collapse_whole_text_repetition(repeated + "然后发布。") == repeated + "然后发布。"
    assert collapse_whole_text_repetition("好，好，好。") == "好，好，好。"


def test_faithfulness_guard_rejects_changed_numeric_facts() -> None:
    spoken_address = "检查幺二七点零点零点一冒号八七六五的健康接口。"
    wrong_address = "检查 127.0.0.0.0:8765 的健康接口。"
    version = "版本是 1.34.2。"

    assert choose_faithful_output(spoken_address, wrong_address) == spoken_address
    assert choose_faithful_output(version, "版本是 1.3.2。") == version


def test_faithfulness_guard_rejects_dropped_constraints_and_content() -> None:
    source = "今天需要整理需求。不要删除历史记录，也不要上传语音。"

    assert choose_faithful_output(source, "今天需要整理需求。") == source
    assert choose_faithful_output("请保留甲乙丙丁戊己庚辛。", "请保留甲乙。") == "请保留甲乙丙丁戊己庚辛。"


def test_faithfulness_guard_allows_safe_cleanup_and_numbered_lists() -> None:
    cleanup_source = "嗯，请检查 Docker 服务。"
    cleanup = "请检查 Docker 服务。"
    list_source = "需要做三件事，第一更新 Docker，第二检查 Kubernetes，第三重启服务。"
    numbered_list = "需要做三件事：\n1. 更新 Docker。\n2. 检查 Kubernetes。\n3. 重启服务。"

    assert choose_faithful_output(cleanup_source, cleanup) == cleanup
    assert choose_faithful_output(list_source, numbered_list) == numbered_list
