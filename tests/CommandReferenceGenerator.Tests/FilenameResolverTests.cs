using Meshmakers.Octo.Frontend.CommandReferenceGenerator;

namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator.Tests;

public class FilenameResolverTests
{
    [Fact]
    public void Resolve_returns_verb_so_filename_matches_CLI_invocation()
    {
        // The verb is what the user types after `-c`, so the file name (and URL slug)
        // must match it — `octo-cli -c Attach` → AttachTenant.cs class → Attach.md file.
        var cmd = new CommandDescriptor(null, "Attach", "x", Array.Empty<ArgumentDescriptor>())
        {
            ClassName = "AttachTenant"
        };

        Assert.Equal("Attach", FilenameResolver.Resolve(cmd));
    }

    [Fact]
    public void Resolve_returns_verb_even_when_class_name_null()
    {
        var cmd = new CommandDescriptor(null, "Foo", "x", Array.Empty<ArgumentDescriptor>());

        Assert.Equal("Foo", FilenameResolver.Resolve(cmd));
    }

    [Fact]
    public void ResolveDisambiguated_strips_command_suffix_from_class_name()
    {
        var cmd = new CommandDescriptor(null, "LogIn", "x", Array.Empty<ArgumentDescriptor>())
        {
            ClassName = "LogInCommand"
        };

        Assert.Equal("LogIn", FilenameResolver.ResolveDisambiguated(cmd));
    }

    [Fact]
    public void ResolveDisambiguated_returns_class_name_when_no_command_suffix()
    {
        var cmd = new CommandDescriptor(null, "Create", "x", Array.Empty<ArgumentDescriptor>())
        {
            ClassName = "CreateTenant"
        };

        Assert.Equal("CreateTenant", FilenameResolver.ResolveDisambiguated(cmd));
    }

    [Fact]
    public void ResolveDisambiguated_falls_back_to_verb_when_class_name_null()
    {
        var cmd = new CommandDescriptor(null, "Foo", "x", Array.Empty<ArgumentDescriptor>());

        Assert.Equal("Foo", FilenameResolver.ResolveDisambiguated(cmd));
    }

    [Fact]
    public void ResolveDisambiguated_does_not_strip_when_class_name_equals_command_suffix()
    {
        var cmd = new CommandDescriptor(null, "Foo", "x", Array.Empty<ArgumentDescriptor>())
        {
            ClassName = "Command"
        };

        Assert.Equal("Command", FilenameResolver.ResolveDisambiguated(cmd));
    }
}
