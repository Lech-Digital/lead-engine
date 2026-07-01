from leadengine.email import render_html, render_text


def test_render_text_appends_signature():
    out = render_text("Congrats on the raise.")
    assert out.startswith("Congrats on the raise.")
    assert "Andrzej Lech" in out
    assert "Founder, Coldstart" in out
    assert "getcoldstart.netlify.app" in out


def test_render_text_strips_incoming_whitespace():
    assert render_text("  Body.  \n\n").startswith("Body.")


def test_render_html_wraps_paragraphs_and_adds_branded_signature():
    html = render_html("First line.\n\nSecond line.")
    # each blank-line-separated block becomes its own paragraph
    assert html.count("<p") == 2
    assert "First line." in html and "Second line." in html
    # signature carries the logo wordmark + link
    assert "Cold" in html and "start" in html
    assert 'href="https://getcoldstart.netlify.app"' in html
    assert "Andrzej Lech" in html


def test_render_html_escapes_body_to_prevent_injection():
    html = render_html("Ampersand & <tag> stay literal.")
    assert "&amp;" in html
    assert "&lt;tag&gt;" in html
    assert "<tag>" not in html
