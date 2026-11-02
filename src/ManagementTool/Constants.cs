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
    ///     Secret of the client's <b>mirror</b> in a child tenant (AB#7192), used by <c>Delete --disable-features</c> to get
    ///     the child token. Without it the secret of <see cref="EnvVarClientSecret" /> is tried in the child, which only
    ///     works while the child still accepts the inherited parent secret (AB#5061).
    /// </summary>
    public const string EnvVarChildClientSecret = "OCTO_CLI_CHILD_CLIENT_SECRET";

    /// <summary>
    ///     The password a command sets (AB#8044): <c>ResetPassword</c>, <c>CreateUser</c>, <c>Setup</c>. For automation; read
    ///     after <c>--password-stdin</c> and before the hidden prompt.
    /// </summary>
    public const string EnvVarNewPassword = "OCTO_CLI_NEW_PASSWORD";

    /// <summary>
    ///     The secret of a client a command creates (AB#8044): <c>AddClientCredentialsClient</c>, <c>AddDeviceCodeClient</c>.
    ///     Deliberately not <see cref="EnvVarClientSecret" />, which is the secret octo-cli itself logs in with.
    /// </summary>
    public const string EnvVarNewClientSecret = "OCTO_CLI_NEW_CLIENT_SECRET";

    /// <summary>
    ///     The client secret of an identity provider a command adds or updates (AB#8044): <c>AddOAuthIdentityProvider</c>,
    ///     <c>AddAzureEntryIdIdentityProvider</c>, <c>UpdateIdentityProvider</c>.
    /// </summary>
    public const string EnvVarIdentityProviderClientSecret = "OCTO_CLI_IDP_CLIENT_SECRET";

    /// <summary>The Anthropic access token <c>RedeemAiTicket</c> hands over (AB#8044).</summary>
    public const string EnvVarAiAccessToken = "OCTO_CLI_AI_ACCESS_TOKEN";

    /// <summary>The Anthropic refresh token <c>RedeemAiTicket</c> hands over (AB#8044).</summary>
    public const string EnvVarAiRefreshToken = "OCTO_CLI_AI_REFRESH_TOKEN";

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