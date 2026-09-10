using DualSenseClient.Core.Utilities;

namespace DualSenseClient.Tests.Core;

public class MarkdownTests
{
    [Test]
    public void ParseBlocks_Heading_ReturnsHeadingWords()
    {
        IReadOnlyList<Block> blocks = Markdown.ParseBlocks("## v1.2.0 (2026-09-08)");

        Assert.That(blocks.Count, Is.EqualTo(1));
        Assert.That(blocks[0].Style, Is.EqualTo(BlockStyle.Heading));
        Assert.That(blocks[0].Words.Select(w => w.Text), Is.EqualTo(["v1.2.0 ", "(2026-09-08)"]));
        Assert.That(blocks[0].Words.All(w => w is { Bold: false, Url: null }), Is.True);
    }

    [Test]
    public void ParseBlocks_SubHeading_ReturnsHeadingWords()
    {
        IReadOnlyList<Block> blocks = Markdown.ParseBlocks("### Features");

        Assert.That(blocks.Count, Is.EqualTo(1));
        Assert.That(blocks[0].Style, Is.EqualTo(BlockStyle.Heading));
        Assert.That(blocks[0].Words.Select(w => w.Text), Is.EqualTo(["Features"]));
    }

    [Test]
    public void ParseBlocks_Bullet_KeepsTightParensAroundLink()
    {
        const string line = "- **feat(x): Cool thing** ([abc1234](https://example.com/c/abc))";

        IReadOnlyList<Block> blocks = Markdown.ParseBlocks(line);

        Assert.That(blocks.Count, Is.EqualTo(1));
        Assert.That(blocks[0].Style, Is.EqualTo(BlockStyle.Bullet));
        Assert.That(blocks[0].Words.Select(w => (w.Text, w.Bold, w.Url is not null)), Is.EqualTo(new[]
        {
            ("• ", false, false), ("feat(x): ", true, false), ("Cool ", true, false), ("thing", true, false), (" ", false, false), ("(", false, false),
            ("abc1234", false, true), (")", false, false)
        }));
        Assert.That(blocks[0].Words.Single(w => w.Url is not null).Url, Is.EqualTo("https://example.com/c/abc"));
    }

    [Test]
    public void ParseBlocks_InvalidUrl_DegradesToPlainText()
    {
        IReadOnlyList<Block> blocks = Markdown.ParseBlocks("See ([x](notaurl)) here");

        Assert.That(blocks[0].Words.Select(w => w.Text), Is.EqualTo(["See ", "(", "x", ") ", "here"]));
        Assert.That(blocks[0].Words.All(w => w.Url is null), Is.True);
    }

    [Test]
    public void ParseBlocks_BlankLines_SkippedAndPlainKept()
    {
        IReadOnlyList<Block> blocks = Markdown.ParseBlocks("First\n\nNo new commits since last release.");

        Assert.That(blocks.Count, Is.EqualTo(2));
        Assert.That(blocks.All(b => b.Style == BlockStyle.Paragraph), Is.True);
        Assert.That(blocks[1].Words.Select(w => w.Text), Is.EqualTo(["No ", "new ", "commits ", "since ", "last ", "release."]));
    }

    [Test]
    public void ParseBlocks_LinkInsideBold_KeepsBold()
    {
        IReadOnlyList<Block> blocks = Markdown.ParseBlocks("**see [docs](https://example.com)**");

        Assert.That(blocks[0].Words.Select(w => (w.Text, w.Bold, w.Url is not null)), Is.EqualTo(new[]
        {
            ("see ", true, false), ("docs", true, true)
        }));
    }
}