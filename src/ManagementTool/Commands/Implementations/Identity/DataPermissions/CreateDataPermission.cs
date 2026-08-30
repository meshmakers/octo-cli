using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity.DataPermissions;

internal class CreateDataPermission : ServiceClientOctoCommand<IIdentityServicesClient>
{
    private readonly IArgument _permissionIdArg;
    private readonly IArgument _descriptionArg;

    public CreateDataPermission(ILogger<CreateDataPermission> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesClient identityServicesClient, IAuthenticationService authenticationService)
        : base(logger, Constants.IdentityServicesGroup, "CreateDataPermission",
            "Creates a data permission (AB#4972)", options, identityServicesClient, authenticationService)
    {
        _permissionIdArg = CommandArgumentValue.AddArgument("pid", "permissionId",
            ["Dot-namespaced permission id, e.g. accounting.documents"], true, 1);
        _descriptionArg = CommandArgumentValue.AddArgument("d", "description", ["Description"], false, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(Samples:
        [
            new CodeSample(arguments: [new CodeSampleArgument(_permissionIdArg, "accounting.documents")],
                description: "Basic usage")
        ]);

    public override async Task Execute()
    {
        var permissionId = CommandArgumentValue.GetArgumentScalarValue<string>(_permissionIdArg);
        var description = CommandArgumentValue.IsArgumentUsed(_descriptionArg)
            ? CommandArgumentValue.GetArgumentScalarValue<string>(_descriptionArg)
            : null;

        await ServiceClient.CreateDataPermission(new DataPermissionDto
        {
            PermissionId = permissionId,
            Description = description
        });
        Logger.LogInformation("Data permission '{PermissionId}' created", permissionId);
    }
}
