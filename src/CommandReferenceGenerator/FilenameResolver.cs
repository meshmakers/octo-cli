namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public static class FilenameResolver
{
    private const string CommandSuffix = "Command";

    public static string Resolve(CommandDescriptor cmd) => cmd.Verb;

    public static string ResolveDisambiguated(CommandDescriptor cmd)
    {
        if (cmd.ClassName == null) return cmd.Verb;
        return cmd.ClassName.EndsWith(CommandSuffix) && cmd.ClassName.Length > CommandSuffix.Length
            ? cmd.ClassName[..^CommandSuffix.Length]
            : cmd.ClassName;
    }
}
