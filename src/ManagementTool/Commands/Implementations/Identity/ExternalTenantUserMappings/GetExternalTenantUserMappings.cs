using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.ExternalTenantUserMappings;

internal class GetExternalTenantUserMappings : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IConsoleService _consoleService;
    private readonly IArgument _skip;
    private readonly IArgument _sourceTenantId;
    private readonly IArgument _take;

    public GetExternalTenantUserMappings(ILogger<GetExternalTenantUserMappings> logger,
        IOptions<OctoToolOptions> options,
        IConsoleService consoleService,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "GetExternalTenantUserMappings",
            "Gets external tenant user mappings.", options, identityServicesClient, authenticationService)
    {
        _consoleService = consoleService;
        _skip = CommandArgumentValue.AddArgument("skip", "skip",
            ["Number of items to skip"], false, 1);
        _take = CommandArgumentValue.AddArgument("take", "take",
            ["Number of items to take"], false, 1);
        _sourceTenantId = CommandArgumentValue.AddArgument("stid", "sourceTenantId",
            ["Filter by source tenant ID"], false, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Getting external tenant user mappings from '{ServiceClientServiceUri}'",
            ServiceClient.ServiceUri);

        int? skip = CommandArgumentValue.IsArgumentUsed(_skip)
            ? CommandArgumentValue.GetArgumentScalarValue<int>(_skip)
            : null;
        int? take = CommandArgumentValue.IsArgumentUsed(_take)
            ? CommandArgumentValue.GetArgumentScalarValue<int>(_take)
            : null;
        string? sourceTenantId = CommandArgumentValue.IsArgumentUsed(_sourceTenantId)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_sourceTenantId)
            : null;

        var result = await ServiceClient.GetExternalTenantUserMappings(skip, take, sourceTenantId);
        if (!result.Any())
        {
            Logger.LogInformation("No external tenant user mappings have been returned");
            return;
        }

        var resultString = JsonConvert.SerializeObject(result, Formatting.Indented);
        _consoleService.WriteLine(resultString);
    }
}
