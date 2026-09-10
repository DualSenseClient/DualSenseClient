using System.Text.RegularExpressions;

namespace DualSenseClient.Core.Utilities;

/// <summary>
/// Minimal markdown parsing for release notes: <c># </c>..<c>###### </c>
/// headings, <c>- </c>/<c>* </c> bullets, <c>**bold**</c>, and <c>[text](url)</c> links.
/// Anything else yields plain paragraphs. Pure strings in, no UI dependencies,
/// so callers render <see cref="Block"/>/<see cref="Word"/> however fits.
/// </summary>
/// <summary>
/// Paragraph layout.
/// </summary>
public enum BlockStyle
{
    /// <summary>
    /// Plain paragraph.
    /// </summary>
    Paragraph,

    /// <summary>
    /// <c># </c>..<c>###### </c> heading.
    /// </summary>
    Heading,

    /// <summary>
    /// <c>- </c>/<c>* </c> bullet (prefixed with a <c>• </c> word).
    /// </summary>
    Bullet
}

/// <summary>
/// One renderable word. <see cref="Word.Text"/> keeps its source spacing, so
/// <c>(sha)</c> stays tight while <c>popups </c> keeps its trailing space.
/// </summary>
/// <param name="Text">The word text including any trailing space.</param>
/// <param name="Bold">Whether the word renders bold.</param>
/// <param name="Url">Absolute link target, or <c>null</c> for plain text.</param>
public sealed record Word(string Text, bool Bold, string? Url);

/// <summary>
/// One markdown block (line) and its words.
/// </summary>
/// <param name="Style">The paragraph layout.</param>
/// <param name="Words">The words in order.</param>
public sealed record Block(BlockStyle Style, IReadOnlyList<Word> Words);

/// <summary>
/// A piece of a paragraph: plain (optionally bold) text or a link.
/// </summary>
internal abstract record MarkdownSegment;

/// <summary>
/// Plain paragraph text.
/// </summary>
internal sealed record MarkdownTextSegment(string Text, bool Bold) : MarkdownSegment;

/// <summary>
/// A link with its target.
/// </summary>
internal sealed record MarkdownLinkSegment(string Text, string Url, bool Bold) : MarkdownSegment;

/// <summary>
/// Changelog Markdown parsing
/// </summary>
public static class Markdown
{
    /// <summary>
    /// Matches <c>[text](url)</c> links.
    /// </summary>
    private static readonly Regex LinkRegex = new Regex(@"\[([^\]]+)\]\(([^)\s]+)\)", RegexOptions.Compiled);

    /// <summary>
    /// Matches <c>**bold**</c> spans.
    /// </summary>
    private static readonly Regex BoldRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Splits <paramref name="markdown"/> into blocks, skipping blank lines.
    /// </summary>
    public static IReadOnlyList<Block> ParseBlocks(string markdown)
    {
        List<Block> blocks = [];
        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            BlockStyle style = BlockStyle.Paragraph;
            int hashes = 0;
            while (hashes < line.Length && line[hashes] == '#')
            {
                hashes++;
            }

            if (hashes > 0 && hashes < line.Length && line[hashes] == ' ')
            {
                style = BlockStyle.Heading;
                line = line[(hashes + 1)..].Trim();
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                style = BlockStyle.Bullet;
                line = line[2..].TrimStart();
            }

            List<Word> words = [];
            if (style == BlockStyle.Bullet)
            {
                words.Add(new Word("• ", false, null));
            }

            words.AddRange(ParseWords(line, false));
            blocks.Add(new Block(style, words));
        }

        return blocks;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into words, resolving <c>**bold**</c>
    /// first and links within each part. Links with non-absolute URLs degrade
    /// to their plain inner text.
    /// </summary>
    private static List<Word> ParseWords(string text, bool bold)
    {
        List<Word> words = [];
        foreach (MarkdownSegment segment in ParseBold(text))
        {
            switch (segment)
            {
                case MarkdownLinkSegment link when IsAbsoluteUrl(link.Url):
                    words.Add(new Word(link.Text, bold || link.Bold, link.Url));
                    break;
                case MarkdownLinkSegment link:
                    words.AddRange(SplitWords(link.Text, bold || link.Bold));
                    break;
                case MarkdownTextSegment part:
                    words.AddRange(SplitWords(part.Text, bold || part.Bold));
                    break;
            }
        }

        return words;
    }

    /// <summary>
    /// Splits <paramref name="text"/> on <c>**bold**</c> markers first, then
    /// resolves links within each part so bold-wrapped links keep both styles.
    /// </summary>
    private static List<MarkdownSegment> ParseBold(string text)
    {
        List<MarkdownSegment> segments = [];
        int pos = 0;
        foreach (Match match in BoldRegex.Matches(text))
        {
            if (match.Index > pos)
            {
                segments.AddRange(ParseLinks(text.Substring(pos, match.Index - pos)));
            }

            foreach (MarkdownSegment inner in ParseLinks(match.Groups[1].Value))
            {
                segments.Add(inner switch
                {
                    MarkdownTextSegment t => new MarkdownTextSegment(t.Text, true),
                    MarkdownLinkSegment l => new MarkdownLinkSegment(l.Text, l.Url, true),
                    _ => inner
                });
            }

            pos = match.Index + match.Length;
        }

        if (pos < text.Length)
        {
            segments.AddRange(ParseLinks(text[pos..]));
        }

        return segments;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into link and plain segments.
    /// </summary>
    private static List<MarkdownSegment> ParseLinks(string text)
    {
        List<MarkdownSegment> segments = [];
        int pos = 0;
        foreach (Match match in LinkRegex.Matches(text))
        {
            if (match.Index > pos)
            {
                segments.Add(new MarkdownTextSegment(text.Substring(pos, match.Index - pos), false));
            }

            segments.Add(new MarkdownLinkSegment(match.Groups[1].Value, match.Groups[2].Value, false));
            pos = match.Index + match.Length;
        }

        if (pos < text.Length)
        {
            segments.Add(new MarkdownTextSegment(text[pos..], false));
        }

        return segments;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into word chunks, keeping each word's
    /// trailing spaces with it so wrapping never strands a leading space.
    /// </summary>
    private static List<Word> SplitWords(string text, bool bold)
    {
        List<Word> words = [];
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                // Whitespace run becomes its own chunk (only occurs between a
                // link and the next word, since lines are pre-trimmed).
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                words.Add(new Word(" ", bold, null));
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                string word = text[start..i];
                if (i < text.Length)
                {
                    // Attach one trailing space so wrapping never strands a leading space.
                    word += " ";
                    while (i < text.Length && char.IsWhiteSpace(text[i]))
                    {
                        i++;
                    }
                }

                words.Add(new Word(word, bold, null));
            }
        }

        return words;
    }

    /// <summary>
    /// Whether <paramref name="url"/> is an absolute URL worth linking.
    /// </summary>
    private static bool IsAbsoluteUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out _);
}