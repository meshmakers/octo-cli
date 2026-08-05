namespace Meshmakers.Octo.Frontend.ManagementTool;

internal static class Constants
{
    public const string OctoExeName = "octo-cli";
    public const string OctoToolUserFolderName = "octo-cli";
    public const string OctoToolOptionsRootNode = "OctoToolOptions";
    public const string AuthenticationRootNode = "Authentication";

    public const string EnvVarClientId = "OCTO_CLI_CLIENT_ID";
    public const string EnvVarClientSecret = "OCTO_CLI_CLIENT_SECRET";

    /// <summary>
    ///     Selects the context for a single invocation, like the --context argument but for a whole
    ///     subshell. The argument wins when both are given.
    /// </summary>
    public const string EnvVarContext = "OCTO_CLI_CONTEXT";

    /// <summary>
    ///     Parent directory of the ".octo-cli" folder. Pointing parallel jobs at different values
    ///     gives each of them its own contexts.json instead of a shared one.
    /// </summary>
    public const string EnvVarHome = "OCTO_CLI_HOME";

    /// <summary>
    ///     Short and long term of the top-level context argument. No abbreviation is offered so the
    ///     term cannot collide with an argument a future command declares.
    /// </summary>
    public const string ContextArgumentTerm = "context";

    public const string IdentityServicesGroup = "Identity Services";
    public const string BotServicesGroup = "Bot Services";
    public const string CommunicationServicesGroup = "Communication Services";
    public const string AssetRepositoryServicesGroup = "Asset Repository Services";
    public const string ReportingServicesGroup = "Reporting Services";
    public const string AiServicesGroup = "AI Services";
    public const string DiagnosticsGroup = "Diagnostics";
    public const string DevOpsGroup = "DevOps";
    public const string ContextGroup = "Context Management";
}