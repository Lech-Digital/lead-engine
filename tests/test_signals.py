from leadengine.models import RawItem
from leadengine.signals import score_item

TARGETS = ["HR", "People Operations"]
SIGNALS = ["hiring", "raised"]


def _item(text: str) -> RawItem:
    return RawItem(source="test", external_id="1", text=text, title="")


def test_score_item_no_match_scores_zero():
    result = score_item(_item("we sell shoes online"), TARGETS, SIGNALS)
    assert result.score == 0.0
    assert result.matched == []


def test_score_item_target_and_signal_scores_high():
    result = score_item(
        _item("We just raised a seed round and are hiring our first HR lead"),
        TARGETS, SIGNALS,
    )
    assert result.score >= 0.7
    assert "HR" in result.matched
    assert "hiring" in result.matched


def test_score_item_target_only_is_below_full():
    # niche match but no buying intent -> capped around 0.3 (half weight)
    result = score_item(_item("our HR department is great"), TARGETS, SIGNALS)
    assert 0.0 < result.score < 0.5


def test_score_item_base_score_floors_an_otherwise_weak_item():
    # A source with intrinsic confidence (e.g. a funding announcement) supplies a
    # base_score. An off-niche item that would normally score 0 is floored to it.
    result = score_item(_item("we sell shoes online"), TARGETS, SIGNALS, base_score=0.5)
    assert result.score == 0.5


def test_score_item_base_score_does_not_lower_a_strong_item():
    # The floor must never drag down an item that already scores higher on its own.
    strong = score_item(
        _item("We just raised a seed round and are hiring our first HR lead"),
        TARGETS, SIGNALS, base_score=0.5,
    )
    assert strong.score >= 0.7


def test_score_item_score_weight_demotes_a_noisy_source():
    # A noisy source (e.g. forum comments) can be down-weighted so its items sort
    # below higher-intent sources under the enrichment cap.
    base = score_item(_item("we are hiring an HR lead"), TARGETS, SIGNALS)
    demoted = score_item(_item("we are hiring an HR lead"), TARGETS, SIGNALS, score_weight=0.7)
    assert demoted.score == round(base.score * 0.7, 3)


def test_score_item_score_weight_does_not_sink_below_base_floor():
    # Down-weighting must never pull a high-confidence source under its own floor.
    result = score_item(
        _item("we sell shoes online"), TARGETS, SIGNALS, base_score=0.5, score_weight=0.5
    )
    assert result.score == 0.5
