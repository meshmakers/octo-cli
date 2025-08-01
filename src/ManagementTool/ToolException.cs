namespace Meshmakers.Octo.Frontend.ManagementTool;

public class ToolException : Exception
{
    public ToolException()
    {
    }

    public ToolException(string message) : base(message)
    {
    }

    public ToolException(string message, Exception inner) : base(message, inner)
    {
    }

    public static Exception NoTenantIdConfigured()
    {
        return new ToolException("No tenant id has been saved in configuration. Use --config to set a value");
    }

    public static Exception FilePathDoesNotExist(string filePath)
    {
        return new ToolException(
            $"The file path '{filePath}' does not exist. Please check the path and try again.");
    }

    public static Exception InvalidFilterTerm(string filterArg)
    {
        return new ToolException(
            $"The filter term '{filterArg}' is invalid. Please provide a valid filter with three terms.");
    }

    public static Exception InvalidFilterOperator(string filterArg, string term)
    {
        return new ToolException(
            $"The filter operator '{filterArg}' is invalid for the term '{term}'. Please provide a valid filter operator.");
    }

    public static Exception IdNotFound(string serviceHookId, string serviceHook)
    {
        return new ToolException(
            $"The service hook with ID '{serviceHookId}' was not found in the service '{serviceHook}'. Please check the ID and try again.");
    }
}