namespace Meshmakers.Octo.Frontend.CommandReferenceGenerator;

public static class GroupSlugger
{
    public const string NoGroupSlug = "general";
    public const string NoGroupLabel = "General";

    public static string Slug(string? groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return NoGroupSlug;
        return groupName.Trim().ToLowerInvariant().Replace(' ', '-');
    }

    public static string Label(string? groupName)
    {
        return string.IsNullOrWhiteSpace(groupName) ? NoGroupLabel : groupName.Trim();
    }
}
