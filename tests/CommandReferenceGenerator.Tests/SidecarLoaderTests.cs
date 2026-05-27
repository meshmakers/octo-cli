using Meshmakers.Octo.Frontend.CommandReferenceGenerator;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator.Tests;

public class SidecarLoaderTests
{
    [Fact]
    public void Parse_empty_content_returns_all_nulls()
    {
        var result = SidecarLoader.Parse("");

        Assert.Null(result.Examples);
        Assert.Null(result.Notes);
        Assert.Null(result.SeeAlso);
    }

    [Fact]
    public void Parse_only_examples_returns_only_examples()
    {
        var input = "## Examples\n\nSome example content here.\n\n```bash\noctocli foo\n```";

        var result = SidecarLoader.Parse(input);

        Assert.NotNull(result.Examples);
        Assert.Contains("Some example content here.", result.Examples);
        Assert.Contains("octocli foo", result.Examples);
        Assert.Null(result.Notes);
        Assert.Null(result.SeeAlso);
    }

    [Fact]
    public void Parse_all_three_sections_returns_all()
    {
        var input = "## Examples\n\nExample text\n\n## Notes\n\nNote text\n\n## See Also\n\n- Link";

        var result = SidecarLoader.Parse(input);

        Assert.NotNull(result.Examples);
        Assert.Contains("Example text", result.Examples);
        Assert.NotNull(result.Notes);
        Assert.Contains("Note text", result.Notes);
        Assert.NotNull(result.SeeAlso);
        Assert.Contains("- Link", result.SeeAlso);
    }

    [Fact]
    public void Parse_preamble_before_first_heading_is_ignored()
    {
        var input = "Random preamble text.\n\nMore preamble.\n\n## Examples\n\nReal content";

        var result = SidecarLoader.Parse(input);

        Assert.NotNull(result.Examples);
        Assert.Contains("Real content", result.Examples);
        Assert.DoesNotContain("preamble", result.Examples);
    }

    [Fact]
    public void Parse_unknown_heading_is_warned_and_skipped()
    {
        var input = "## Foo\n\nIgnored content\n\n## Examples\n\nReal content";

        var result = SidecarLoader.Parse(input);

        Assert.NotNull(result.Examples);
        Assert.Contains("Real content", result.Examples);
        Assert.DoesNotContain("Ignored content", result.Examples);
    }

    [Fact]
    public void Parse_wrong_level_heading_treated_as_content()
    {
        var input = "## Examples\n\nFirst paragraph.\n\n### Subheading\n\nSecond paragraph.";

        var result = SidecarLoader.Parse(input);

        Assert.NotNull(result.Examples);
        Assert.Contains("First paragraph", result.Examples);
        Assert.Contains("### Subheading", result.Examples);
        Assert.Contains("Second paragraph", result.Examples);
    }

    [Fact]
    public void Parse_heading_inside_code_fence_is_content_not_section_start()
    {
        var input = "## Examples\n\n```bash\n## This is a bash comment\noctocli foo\n```\n\nAfter the fence.";

        var result = SidecarLoader.Parse(input);

        Assert.NotNull(result.Examples);
        Assert.Contains("## This is a bash comment", result.Examples);
        Assert.Contains("octocli foo", result.Examples);
        Assert.Contains("After the fence", result.Examples);
        Assert.Null(result.Notes);
        Assert.Null(result.SeeAlso);
    }

    [Fact]
    public void Parse_duplicate_heading_uses_first_occurrence()
    {
        var input = "## Examples\n\nFirst content\n\n## Examples\n\nSecond content (should be ignored)";

        var result = SidecarLoader.Parse(input);

        Assert.NotNull(result.Examples);
        Assert.Contains("First content", result.Examples);
        Assert.DoesNotContain("Second content", result.Examples);
    }

    [Fact]
    public void Parse_crlf_line_endings_are_normalized()
    {
        var input = "## Examples\r\n\r\nLine one\r\nLine two";

        var result = SidecarLoader.Parse(input);

        Assert.NotNull(result.Examples);
        Assert.Contains("Line one", result.Examples);
        Assert.Contains("Line two", result.Examples);
        Assert.DoesNotContain("\r", result.Examples);
    }

    [Fact]
    public void Load_missing_file_returns_all_nulls()
    {
        var result = SidecarLoader.Load("C:\\does\\not\\exist\\Foo.cs", "Foo");

        Assert.Null(result.Examples);
        Assert.Null(result.Notes);
        Assert.Null(result.SeeAlso);
    }

    [Fact]
    public void Load_null_or_empty_inputs_return_all_nulls()
    {
        Assert.Null(SidecarLoader.Load(null, "Foo").Examples);
        Assert.Null(SidecarLoader.Load("path.cs", null).Examples);
        Assert.Null(SidecarLoader.Load("", "Foo").Examples);
        Assert.Null(SidecarLoader.Load("path.cs", "").Examples);
    }
}
