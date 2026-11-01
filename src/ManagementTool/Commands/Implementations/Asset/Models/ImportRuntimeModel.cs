using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.Models;

internal class ImportRuntimeModel : JobWithWaitOctoCommand
{
    private readonly IAssetServicesClient _assetServicesClient;
    private readonly IArgument _fileArg;
    private readonly IArgument _replaceArg;

    public ImportRuntimeModel(ILogger<ImportRuntimeModel> logger, IOptions<OctoToolOptions> options,
        IAssetServicesClient assetServicesClient, IBotServicesClient botServicesClient,
        IAuthenticationService authenticationService)
        : base(logger, Constants.AssetRepositoryServicesGroup, "ImportRt",
            "Schedules an import job for runtime files. File is specified using -f argument. To wait for job, use -w argument.",
            options, botServicesClient, authenticationService)
    {
        _assetServicesClient = assetServicesClient;

        _fileArg = CommandArgumentValue.AddArgument("f", "file", ["File to import"], true, 1);
        _replaceArg = CommandArgumentValue.AddArgument("r", "replace",
            [
                "When defined, existing entities are replaced (upsert).",
                "Without it entities are only inserted: an entity that exists only as an archived (deleted) document " +
                "with the same rtId is replaced and active again, an active one fails."
            ], false, 0);
    }

    public override async Task PreValidate()
    {
        await base.PreValidate();

        if (_assetServicesClient.AccessToken != null)
        {
            _assetServicesClient.AccessToken.AccessToken = ServiceClient.AccessToken?.AccessToken;
        }
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                new CodeSample(arguments: [
                    new CodeSampleArgument(_fileArg, "./runtime-data.json"),
                    new CodeSampleArgument(_waitForJobArg),
                    new CodeSampleArgument(_replaceArg),
                ],
                    description: "Basic usage"),
            ],
            Notes:
            [
                "### Replaced create\n\n" +
                "An import cannot create an entity of a type whose static CK method declares replacesGenericCreate " +
                "(or of a type derived from it): only the method may create it, exactly as the generic create of the " +
                "API is refused. If the file contains a new entity of such a type, the whole import fails before " +
                "anything is written with message 137 (reason GENERIC_CREATE_REPLACED_BY_METHOD), which names the " +
                "type, the method and the rtId of every refused entity. This applies with and without -r.",
                "Entities whose rtId already exists in the tenant are updated as before (with -r). Files and tenants " +
                "without such types behave as before. To bring such entities into a tenant, restore a backup of the " +
                "tenant or create them by calling the method.",
                "### Hidden and MethodOnly attributes\n\nAn import cannot set an attribute whose CK assignment declares `access: Hidden` or `access: MethodOnly` (also as a member of a record): only a CK method may write it. If the file contains such a value, the whole import fails before anything is written with message 139, which names the type, the attribute path and the rtId of every refused value. `ReadOnly` attributes can be imported. This applies with and without -r, to YAML and JSON files. See [Export and import](../../../../constructionKits/ckLanguage2/accessModifiers.md#export-and-import).",
                "### Users\n\nSince System.Identity 2.28.0 an import cannot create or update users (System.Identity/User): UserName and Email are MethodOnly, so a file that contains a user fails as a whole with message 139. An export still contains the users (without password and two-factor material); leave them out of the export or remove them from the file. To copy a tenant with its users, restore a dump of the tenant; to move single users, create them again with CreateUser. See [Moving Users to Another Tenant](../../../../identityService/users-and-roles.md#moving-users-to-another-tenant).",
            ]
        );

    public override async Task Execute()
    {
        var rtModelFilePath = CommandArgumentValue.GetArgumentScalarValue<string>(_fileArg);

        var importStrategy = CommandArgumentValue.IsArgumentUsed(_replaceArg)
            ? ImportStrategyDto.Upsert
            : ImportStrategyDto.InsertOnly;

        var tenantId = Options.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw ToolException.NoTenantIdConfigured();
        }

        Logger.LogInformation("Importing runtime model \'{RtModelFilePath}\' with import mode \'{ImportStrategy}\'",
            rtModelFilePath, importStrategy);

        if (!File.Exists(rtModelFilePath))
        {
            Logger.LogError("File \'{CkModelFilePath}\' does not exist", rtModelFilePath);
            return;
        }

        var id = await _assetServicesClient.ImportRtModelAsync(tenantId, importStrategy, rtModelFilePath);
        Logger.LogInformation("Runtime model import with job id \'{Id}\' has been started", id);
        await WaitForJob(id);
    }
}