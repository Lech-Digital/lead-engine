from datetime import datetime, timezone

from leadengine.sources.funding import FundingRSSSource, _parse_feed

RSS_2_0 = """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel>
  <title>VC Funding News</title>
  <item>
    <title>Acme raises $20M Series A to scale its platform</title>
    <link>https://example.com/acme-series-a</link>
    <description>Acme, a B2B SaaS startup, has raised a Series A round led by Foo Ventures.</description>
    <guid>https://example.com/acme-series-a</guid>
    <pubDate>Wed, 04 Jun 2025 13:00:00 GMT</pubDate>
  </item>
  <item>
    <title>OldCo raised seed funding</title>
    <link>https://example.com/oldco</link>
    <description>OldCo closed a seed round.</description>
    <guid>guid-oldco</guid>
    <pubDate>Mon, 06 Jan 2020 13:00:00 GMT</pubDate>
  </item>
</channel></rss>
"""

ATOM = """<?xml version="1.0" encoding="UTF-8"?>
<feed xmlns="http://www.w3.org/2005/Atom">
  <title>Funding Atom</title>
  <entry>
    <title>Beta Inc closes $5M seed round</title>
    <link href="https://example.com/beta-seed"/>
    <summary>Beta Inc announced it has raised seed funding to grow its team.</summary>
    <id>tag:example.com,2025:beta-seed</id>
    <updated>2025-06-04T09:30:00Z</updated>
  </entry>
</feed>
"""


def _cfg(**over):
    cfg = {"feeds": ["https://example.com/feed"], "lookback_days": 365, "base_score": 0.5}
    cfg.update(over)
    return cfg


def test_parse_feed_reads_rss_2_0_items():
    entries = _parse_feed(RSS_2_0)
    assert len(entries) == 2
    first = entries[0]
    assert first["title"] == "Acme raises $20M Series A to scale its platform"
    assert first["url"] == "https://example.com/acme-series-a"
    assert "Foo Ventures" in first["summary"]
    assert first["id"] == "https://example.com/acme-series-a"
    assert first["published"] == datetime(2025, 6, 4, 13, 0, tzinfo=timezone.utc)


def test_parse_feed_reads_atom_entries():
    entries = _parse_feed(ATOM)
    assert len(entries) == 1
    e = entries[0]
    assert e["title"] == "Beta Inc closes $5M seed round"
    assert e["url"] == "https://example.com/beta-seed"          # from link href attr
    assert "grow its team" in e["summary"]
    assert e["id"] == "tag:example.com,2025:beta-seed"
    assert e["published"] == datetime(2025, 6, 4, 9, 30, tzinfo=timezone.utc)


def test_parse_feed_ignores_malformed_xml():
    assert _parse_feed("<not><closed>") == []


def test_source_builds_raw_items_with_honest_funding_framing():
    src = FundingRSSSource(_cfg())
    now = datetime(2025, 6, 5, tzinfo=timezone.utc)
    items = src._items_from_feed(RSS_2_0, "https://example.com/feed", now=now)

    # OldCo's 2020 post is outside the 365-day lookback -> dropped.
    assert len(items) == 1
    item = items[0]
    assert item.source == "funding"
    assert item.title == "Acme raises $20M Series A to scale its platform"
    assert item.url == "https://example.com/acme-series-a"
    # The harvested text is fed verbatim to the LLM, so the framing must be honest:
    # it restates the real announcement, never fabricates first-person claims.
    assert item.text.startswith("FUNDING SIGNAL")
    assert "Series A" in item.text
    # external_id is stable and feed-scoped for dedup.
    assert item.external_id.endswith("https://example.com/acme-series-a")


def test_source_base_score_defaults_and_is_configurable():
    assert FundingRSSSource(_cfg()).base_score == 0.5
    assert FundingRSSSource(_cfg(base_score=0.7)).base_score == 0.7
    # Sensible non-zero default even when the key is omitted.
    assert FundingRSSSource({"feeds": []}).base_score > 0.0


# Feeds like TechCrunch's /venture/ mix real rounds with VC editorial. Items that
# don't look like a funding round must be dropped BEFORE they cost an LLM call.
EDITORIAL_FEED = """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel>
  <item>
    <title>Patronus AI lands $50M to stress-test AI agents</title>
    <link>https://example.com/patronus</link>
    <description>The startup announced a fresh round of investment.</description>
    <guid>g1</guid>
    <pubDate>Wed, 04 Jun 2025 13:00:00 GMT</pubDate>
  </item>
  <item>
    <title>How to invest when everything is moving too fast</title>
    <link>https://example.com/oped</link>
    <description>A columnist shares views on the market.</description>
    <guid>g2</guid>
    <pubDate>Wed, 04 Jun 2025 13:00:00 GMT</pubDate>
  </item>
</channel></rss>
"""


def test_source_filters_out_non_funding_editorial_by_default():
    src = FundingRSSSource(_cfg())
    now = datetime(2025, 6, 5, tzinfo=timezone.utc)
    items = src._items_from_feed(EDITORIAL_FEED, "https://example.com/feed", now=now)

    titles = [i.title for i in items]
    assert any("Patronus" in t for t in titles)            # real round kept
    assert all("How to invest" not in t for t in titles)   # editorial dropped


def test_source_can_disable_funding_term_filter_for_curated_feeds():
    src = FundingRSSSource(_cfg(require_funding_terms=False))
    now = datetime(2025, 6, 5, tzinfo=timezone.utc)
    items = src._items_from_feed(EDITORIAL_FEED, "https://example.com/feed", now=now)
    assert len(items) == 2  # nothing filtered when the feed is already curated
