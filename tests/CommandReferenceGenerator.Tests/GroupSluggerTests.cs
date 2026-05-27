using Meshmakers.Octo.Frontend.CommandReferenceGenerator;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator.Tests;

public class GroupSluggerTests
{
    [Fact]
    public void Slug_returns_kebab_case_lowercase()
    {
        Assert.Equal("asset-repository-services", GroupSlugger.Slug("Asset Repository Services"));
    }

    [Fact]
    public void Slug_returns_general_when_null_or_whitespace()
    {
        Assert.Equal("general", GroupSlugger.Slug(null));
        Assert.Equal("general", GroupSlugger.Slug(""));
        Assert.Equal("general", GroupSlugger.Slug("   "));
    }

    [Fact]
    public void Label_returns_general_when_null_or_whitespace()
    {
        Assert.Equal("General", GroupSlugger.Label(null));
        Assert.Equal("General", GroupSlugger.Label(""));
    }

    [Fact]
    public void Label_returns_original_group_when_set()
    {
        Assert.Equal("Identity Services", GroupSlugger.Label("Identity Services"));
    }
}
