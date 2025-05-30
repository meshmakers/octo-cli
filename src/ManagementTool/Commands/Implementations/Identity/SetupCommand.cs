﻿using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Identity;

internal class SetupCommand : Command<OctoToolOptions>
{
    private readonly IArgument _eMailArg;
    private readonly IIdentityServicesSetupClient _identityServicesSetupClient;
    private readonly IArgument _passwordArg;

    public SetupCommand(ILogger<SetupCommand> logger, IOptions<OctoToolOptions> options,
        IIdentityServicesSetupClient identityServicesSetupClient)
        : base(logger, Constants.IdentityServicesGroup, "Setup", "Sets identity services up", options)
    {
        _identityServicesSetupClient = identityServicesSetupClient;

        _eMailArg = CommandArgumentValue.AddArgument("e", "email",
            ["E-Mail of admin"], 1);
        _passwordArg = CommandArgumentValue.AddArgument("p", "password",
            ["Password of admin"], 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Adding admin");

        var eMail = CommandArgumentValue.GetArgumentScalarValue<string>(_eMailArg);
        var password = CommandArgumentValue.GetArgumentScalarValue<string>(_passwordArg);

        await _identityServicesSetupClient.AddAdminUser(new AdminUserDto
        {
            EMail = eMail,
            Password = password
        });
    }
}