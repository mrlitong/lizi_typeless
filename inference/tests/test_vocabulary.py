from lizi_typeless_inference.vocabulary import correct_personal_vocabulary


def test_only_observed_personal_vocabulary_variants_are_corrected() -> None:
    text = "使用 Cloud Code，路径是斜杠 home 斜杠立通斜杠 logs 目录下面。"

    assert correct_personal_vocabulary(text) == "使用 Claude Code，路径是/home/litong/logs。"
    assert correct_personal_vocabulary("Kubernetes 集群") == "Kubernetes 集群"
