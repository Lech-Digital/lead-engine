using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Structural variance for generated drafts. One fixed prompt skeleton made every
// email share the same sentence shape ("Congrats on raising $X… you're likely under
// pressure to…") — a pattern spam filters increasingly flag across sends. Each lead
// deterministically gets one of several genuinely different skeletons (and subject
// patterns), so a night's batch never shares one shape. Selection must be stable
// across processes: FNV-1a, never string.GetHashCode().
//
// "Genuinely different" means paragraph COUNT, where the ask LIVES (its own final
// paragraph vs folded into the end of another vs opening the email), and whether
// there is a one-line opener — not adjectives on an otherwise-identical 3-part shape.
internal static class DraftSkeletons
{
    // Shared invariants — every skeleton embeds all five verbatim.
    private const string Greeting =
        "- Greeting on its own line: \"Hi <first name>,\" when the author's real first name is clearly visible in the content; otherwise exactly \"Hi there,\". Never guess or invent a name.";
    private const string Banned =
        "Banned: \"I hope this email finds you well\", \"I came across\", \"reaching out\", \"game-changer\", \"revolutionize\", \"synergy\", \"circle back\", \"touch base\", \"in today's fast-paced\".";
    private const string Honesty =
        "STRICTLY honest: never invent facts, numbers, names, or results — never claim leads are already found.";
    // The hallucination guardrail (replaces "Invent it from THEIR market").
    internal const string EvidenceRule =
        "Name specific decision-makers, segments, or companies only when the harvested content itself supports " +
        "them; if their market is not clear from the content, stay general (\"the customers you're targeting\") — " +
        "never invent an industry for them, and never reuse an example from these instructions.";
    // The send pipeline (SmtpOutreachMailer.BuildMessage) unconditionally appends a real
    // signature after the composed body. Without this line the model is free to write its
    // own sign-off first, producing a double sign-off in the sent email.
    private const string NoSignature = "No signature (one is added on send).";

    internal static int IndexFor(string key, int count)
    {
        // FNV-1a 32-bit — stable across runs and machines.
        uint hash = 2166136261;
        foreach (var c in key) { hash ^= c; hash *= 16777619; }
        return (int)(hash % (uint)count);
    }

    private static readonly string[] SubjectPatterns =
    [
        "personal and specific to their moment, following the pattern \"<their moment> + an idea for <their company or name>\" (e.g. \"Congrats on the raise + an idea for Acme\"). Always include their company or name.",
        "a short, concrete question about the pressure their moment creates, naming their company (e.g. \"Scaling sales at Acme after the raise?\"). No greeting words in the subject.",
        "their moment stated plainly as news they recognize, plus a hint of the idea (e.g. \"Acme's Series A — one thought on what comes next\").",
        "lowercase, casual, as if a colleague wrote it, naming the company (e.g. \"an idea for acme's next quarter\"). No exclamation marks.",
    ];

    internal static string SubjectInstruction(string key) =>
        $"SUBJECT: {SubjectPatterns[IndexFor(key, SubjectPatterns.Length)]} Max 10 words, no clickbait, no emojis.";

    // Five genuinely different email shapes — different paragraph COUNT, different ask
    // placement, different opener shape — not the same 3-part skeleton with different nouns:
    //   1. Observation-first — greeting + 3 paragraphs, the ask ALONE in its own final paragraph.
    //   2. Question-first    — greeting + 2 paragraphs, the ask folded into the END of paragraph 2.
    //   3. Pressure-first    — greeting + a forced ONE-SENTENCE opening line of its own, then one
    //                          body paragraph, then the ask alone — three short parts, not the
    //                          fuller 3-paragraph shape of #1.
    //   4. Ultra-short        — greeting + ONE paragraph total, the ask as its last sentence.
    //   5. Inverted           — greeting + 2 paragraphs, but paragraph 1 opens with the offer's
    //                          outcome BEFORE any mention of their moment; paragraph 2 supplies
    //                          the reason it's aimed at THEM and folds in the ask. Reverses the
    //                          conventional reason-then-offer order entirely.
    private static readonly Func<NicheOptions, string>[] NormalShapes =
    [
        n => $"""
        DRAFT: a short, warm email from {n.SenderName} ({n.SenderTitle}) — greeting line, then exactly 3 paragraphs separated by ONE blank line each, the ask ALONE in its own final paragraph:
        {Greeting}
        - Paragraph 1 (observation, 2 sentences): the concrete thing they did or published — the specific fact, no praise words — then the need it creates for them right now.
        - Paragraph 2 (benefit, 1-2 sentences): the specific outcome the offer gives them for that need — a result they'd want, not a feature list.
        - Paragraph 3 (the ask, and NOTHING else in this paragraph): one friendly, low-effort question that is easy to answer with a single sentence. If the OFFER includes anything free or no-commitment, the question must offer exactly that.
        {NoSignature}
        {Honesty} {EvidenceRule}
        Short, human, no marketing voice. {Banned}
        """,
        n => $"""
        DRAFT: a short, curious email from {n.SenderName} ({n.SenderTitle}) — greeting line, then exactly 2 paragraphs separated by ONE blank line; the ask is folded into the END of paragraph 2, never a paragraph of its own:
        {Greeting}
        - Paragraph 1 (1-2 sentences): open with one sharp, genuine question about how they're handling the concrete challenge their moment creates — proof you read their content, not flattery.
        - Paragraph 2 (2 sentences — the ask folded in, NOT a new paragraph): one sentence on the outcome the offer gives them for exactly that, then in the SAME paragraph one friendly, low-effort closing question. If the OFFER includes anything free or no-commitment, offer exactly that.
        {NoSignature}
        {Honesty} {EvidenceRule}
        Short, human, no marketing voice. {Banned}
        """,
        n => $"""
        DRAFT: a short, direct email from {n.SenderName} ({n.SenderTitle}) — a greeting line, then a ONE-SENTENCE opening line standing alone as its own short paragraph, then one body paragraph, then the ask alone as its own final paragraph — each part separated by ONE blank line:
        {Greeting}
        - Opening line (its OWN paragraph, EXACTLY one sentence, no more): name the clock that just started ticking for them — the pressure their moment creates — grounded in the concrete fact from their content. No question, no offer yet, just the pressure stated plainly.
        - Body paragraph (1-2 sentences): how the offer takes exactly that pressure off — one outcome, stated plainly, no feature list.
        - Final paragraph (the ask ALONE, and NOTHING else): one friendly, low-effort question. If the OFFER includes anything free or no-commitment, offer exactly that.
        {NoSignature}
        {Honesty} {EvidenceRule}
        Short, human, no marketing voice. {Banned}
        """,
        n => $"""
        DRAFT: an ultra-short note from {n.SenderName} ({n.SenderTitle}) — at most 4 sentences total, greeting line plus ONE paragraph, no other paragraphs:
        {Greeting}
        - The paragraph: one sentence tying their concrete moment to the need it creates, one sentence on the outcome the offer gives them, one friendly, low-effort question as the LAST sentence — all inside this single paragraph. If the OFFER includes anything free or no-commitment, offer exactly that.
        {NoSignature}
        {Honesty} {EvidenceRule}
        Reads like a busy peer typed it in 40 seconds. {Banned}
        """,
        n => $"""
        DRAFT: a short email from {n.SenderName} ({n.SenderTitle}) — greeting line, then exactly 2 paragraphs separated by ONE blank line, in REVERSED order from every other shape: the offer's outcome opens the email, their specific moment and the ask come after:
        {Greeting}
        - Paragraph 1 (offer-first, 1-2 sentences): open directly with the specific outcome the offer creates for someone in their position — a plain opening claim about that outcome, framed toward them — before any mention of their moment. No question here.
        - Paragraph 2 (reason + ask folded in, 1-2 sentences): now ground that opening claim in the concrete fact from their content — why THEM specifically — then, in this SAME paragraph, one friendly, low-effort closing question. If the OFFER includes anything free or no-commitment, offer exactly that.
        {NoSignature}
        {Honesty} {EvidenceRule}
        Short, human, no marketing voice. {Banned}
        """,
    ];

    internal static string NormalDraftInstruction(string key, NicheOptions n) =>
        NormalShapes[IndexFor(key, NormalShapes.Length)](n);

    // Fixed-pitch mode: the sender's intro/pitch/CTA are appended after the AI text,
    // so every variant keeps the contract: no selling, no offer names, no closing question,
    // and a "> "-quoted analysis block (DraftComposer anchors the intro before it). The old
    // fixed-pitch prompt never carried a no-signature instruction either (a pre-existing,
    // separate latent risk) — added here defensively since nothing stops the model writing a
    // sign-off before DraftComposer appends the pitch/CTA blocks after it.
    private static readonly string[] FixedPitchShapes =
    [
        $"""
        DRAFT: ONLY the greeting, one congratulations sentence, and a short quoted analysis — the sender's intro, pitch and call-to-action are appended automatically after your text, so do NOT sell, do NOT name any offer or tool, do NOT ask a closing question. Produce exactly:
        {Greeting}
        - After a blank line: ONE short sentence — a genuine, specific congratulations on their moment, naming the concrete thing. Speak TO them (always you/your, never their company as a third-person subject). Warm; an exclamation mark is fine.
        - After a blank line: the quoted analysis — at most 2 short sentences where EVERY line starts with "> ". Name the pressure this moment creates for them and who a lead engine could find for them to connect with. {EvidenceRule}
        {NoSignature}
        {Honesty}
        Short, human, no marketing voice. {Banned}
        """,
        $"""
        DRAFT: ONLY the greeting, one observation sentence, and a short quoted analysis — the sender's intro, pitch and call-to-action are appended automatically after your text, so do NOT sell, do NOT name any offer or tool, do NOT ask a closing question. Produce exactly:
        {Greeting}
        - After a blank line: ONE short sentence stating the concrete thing they just did, plainly and specifically — recognition, not praise. Speak TO them (always you/your).
        - After a blank line: the quoted analysis — at most 2 short sentences where EVERY line starts with "> ". Name what this moment means for their pipeline and who a lead engine could find for them. {EvidenceRule}
        {NoSignature}
        {Honesty}
        Short, human, no marketing voice. {Banned}
        """,
        $"""
        DRAFT: ONLY the greeting, one forward-looking sentence, and a short quoted analysis — the sender's intro, pitch and call-to-action are appended automatically after your text, so do NOT sell, do NOT name any offer or tool, do NOT ask a closing question. Produce exactly:
        {Greeting}
        - After a blank line: ONE short sentence on what their moment sets them up to do next — their opportunity, stated to them (always you/your), grounded in the concrete fact. No congratulations word needed.
        - After a blank line: the quoted analysis — at most 2 short sentences where EVERY line starts with "> ". Name the first customers they'll need for that next step and how a lead engine could surface them. {EvidenceRule}
        {NoSignature}
        {Honesty}
        Short, human, no marketing voice. {Banned}
        """,
    ];

    internal static string FixedPitchDraftInstruction(string key) =>
        FixedPitchShapes[IndexFor(key, FixedPitchShapes.Length)];
}
