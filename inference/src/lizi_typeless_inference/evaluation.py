from __future__ import annotations

import unicodedata
from collections.abc import Iterable


def normalize_text(text: str) -> str:
    normalized = unicodedata.normalize("NFKC", text).casefold()
    return "".join(
        character
        for character in normalized
        if character.isalnum() or character == "\ufffd"
    )


def edit_distance(reference: str, hypothesis: str) -> int:
    previous = list(range(len(hypothesis) + 1))
    for reference_index, reference_character in enumerate(reference, start=1):
        current = [reference_index]
        for hypothesis_index, hypothesis_character in enumerate(hypothesis, start=1):
            substitution = previous[hypothesis_index - 1] + (
                reference_character != hypothesis_character
            )
            current.append(
                min(
                    current[-1] + 1,
                    previous[hypothesis_index] + 1,
                    substitution,
                )
            )
        previous = current
    return previous[-1]


def character_error_counts(reference: str, hypothesis: str) -> tuple[int, int]:
    normalized_reference = normalize_text(reference)
    if not normalized_reference:
        raise ValueError("The normalized reference text is empty.")
    normalized_hypothesis = normalize_text(hypothesis)
    return (
        edit_distance(normalized_reference, normalized_hypothesis),
        len(normalized_reference),
    )


def technical_term_hits(hypothesis: str, terms: Iterable[str]) -> dict[str, bool]:
    normalized_hypothesis = normalize_text(hypothesis)
    hits: dict[str, bool] = {}
    for term in terms:
        normalized_term = normalize_text(term)
        if not normalized_term:
            raise ValueError("Technical terms must contain at least one letter or number.")
        hits[term] = normalized_term in normalized_hypothesis
    return hits
