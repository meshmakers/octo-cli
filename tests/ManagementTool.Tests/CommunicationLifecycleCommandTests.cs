using FakeItEasy;
using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Frontend.ManagementTool;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ManagementTool.Tests;

/// <summary>
///     AB#4924 §13 — the per-tenant leasing kill switch on the octo-cli surface.
/// </summary>
/// <remarks>
///     🔴 <b>The PUT replaces the whole record.</b> Before AB#4924 the lifecycle record held a single
///     flag and <c>SetCommunicationLifecycle</c> could build a DTO from the command line alone. With
///     two, that would mean "turn leasing off" silently resets scale-to-zero — a side effect nobody
///     asked for and nobody would look for. The command therefore reads the current record first and
///     overwrites only the flags it was given; these tests are what keep that true.
/// </remarks>
public sealed class CommunicationLifecycleCommandTests
{
    [Fact]
    public async Task Set_TurningLeasingOff_LeavesScaleToZeroUntouched()
    {
        var serviceClient = NewServiceClient(new CommunicationLifecycleDto(ScaleToZeroEnabled: true,
            LeasingEnabled: true));
        var command = NewSetCommand(serviceClient);
        command.CommandArgumentValue.ParseLayer(["-le", "false"]);

        await command.Execute();

        A.CallTo(() => serviceClient.SetLifecycleAsync(
                A<CommunicationLifecycleDto>.That.Matches(d => d.ScaleToZeroEnabled && !d.LeasingEnabled)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Set_TurningScaleToZeroOff_LeavesLeasingUntouched()
    {
        var serviceClient = NewServiceClient(new CommunicationLifecycleDto(ScaleToZeroEnabled: true,
            LeasingEnabled: true));
        var command = NewSetCommand(serviceClient);
        command.CommandArgumentValue.ParseLayer(["-sze", "false"]);

        await command.Execute();

        A.CallTo(() => serviceClient.SetLifecycleAsync(
                A<CommunicationLifecycleDto>.That.Matches(d => !d.ScaleToZeroEnabled && d.LeasingEnabled)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Set_BothFlagsAtOnce_SendsBoth()
    {
        var serviceClient = NewServiceClient(new CommunicationLifecycleDto(false));
        var command = NewSetCommand(serviceClient);
        command.CommandArgumentValue.ParseLayer(["-sze", "true", "-le", "true"]);

        await command.Execute();

        A.CallTo(() => serviceClient.SetLifecycleAsync(
                A<CommunicationLifecycleDto>.That.Matches(d => d.ScaleToZeroEnabled && d.LeasingEnabled)))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    ///     🔴 An empty command line writes nothing. Treating it as "set everything to its default"
    ///     would turn a typo into a per-tenant outage of both features at once.
    /// </summary>
    [Fact]
    public async Task Set_WithNeitherFlag_WritesNothing()
    {
        var serviceClient = NewServiceClient(new CommunicationLifecycleDto(true, true));
        var command = NewSetCommand(serviceClient);
        command.CommandArgumentValue.ParseLayer([]);

        await command.Execute();

        A.CallTo(() => serviceClient.SetLifecycleAsync(A<CommunicationLifecycleDto>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Set_WithoutTenantInContext_DoesNotCallTheService()
    {
        var serviceClient = NewServiceClient(new CommunicationLifecycleDto(false));
        var command = NewSetCommand(serviceClient, tenantId: null);
        command.CommandArgumentValue.ParseLayer(["-le", "true"]);

        await command.Execute();

        A.CallTo(() => serviceClient.SetLifecycleAsync(A<CommunicationLifecycleDto>._)).MustNotHaveHappened();
        A.CallTo(() => serviceClient.GetLifecycleAsync()).MustNotHaveHappened();
    }

    /// <summary>
    ///     A tenant that has never set the record answers false for both. The CLI is where an operator
    ///     checks that, so the default has to be visible there rather than inferred.
    /// </summary>
    [Fact]
    public async Task Get_ReadsBothFlags()
    {
        var serviceClient = NewServiceClient(new CommunicationLifecycleDto(false));
        var command = new GetCommunicationLifecycleCommand(
            NullLogger<GetCommunicationLifecycleCommand>.Instance,
            Options.Create(new OctoToolOptions { TenantId = "meshtest" }), serviceClient,
            A.Fake<IAuthenticationService>());

        await command.Execute();

        A.CallTo(() => serviceClient.GetLifecycleAsync()).MustHaveHappenedOnceExactly();
    }

    private static SetCommunicationLifecycleCommand NewSetCommand(ICommunicationServicesClient serviceClient,
        string? tenantId = "meshtest") =>
        new(NullLogger<SetCommunicationLifecycleCommand>.Instance,
            Options.Create(new OctoToolOptions { TenantId = tenantId }), serviceClient,
            A.Fake<IAuthenticationService>());

    private static ICommunicationServicesClient NewServiceClient(CommunicationLifecycleDto current)
    {
        var serviceClient = A.Fake<ICommunicationServicesClient>();
        A.CallTo(() => serviceClient.GetLifecycleAsync()).Returns(current);
        A.CallTo(() => serviceClient.SetLifecycleAsync(A<CommunicationLifecycleDto>._))
            .ReturnsLazily((CommunicationLifecycleDto d) => d);
        return serviceClient;
    }
}
