using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Common.Configuration;
using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.Communication.Contracts;
using Meshmakers.Octo.Communication.Contracts.Services;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiResources;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiScopes;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ApiSecrets;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Authentication;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Certificates;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Clients;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Communication;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Diagnostics;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.IdentityProviders;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.LargeBinaries;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Models;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.NotificationMessages;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Roles;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.ServiceHooks;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Tenants;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.TimeSeries;
using Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Users;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient.AdminPanel.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.System;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.Tenants;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.BotServices;
using Meshmakers.Octo.Sdk.ServiceClient.CommunicationControllerServices;
using Meshmakers.Octo.Sdk.ServiceClient.IdentityServices;
using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.StreamData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Meshmakers.Octo.Frontend.ManagementTool;

internal static class Program
{
    private static async Task<int> Main()
    {
        var logger = LogManager.GetCurrentClassLogger();
        try
        {
            var servicesProvider = BuildDi();
            using (servicesProvider as IDisposable)
            {
                var runner = servicesProvider.GetRequiredService<Runner>();
                return await runner.DoActionAsync();
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Stopped program because of exception");
            return -100;
        }
        finally
        {
            // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
            LogManager.Shutdown();
        }
    }

    private static IServiceProvider BuildDi()
    {
        var services = new ServiceCollection();

        // Runner is the custom class
        services.AddTransient<Runner>();

        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true, true)
            .AddJsonFile(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    $".{Constants.OctoToolUserFolderName}{Path.DirectorySeparatorChar}settings.json"),
                true, true)
            .Build();

        services.Configure<OctoToolOptions>(options =>
            config.GetSection(Constants.OctoToolOptionsRootNode).Bind(options));

        services.Configure<OctoToolAuthenticationOptions>(options =>
            config.GetSection(Constants.AuthenticationRootNode).Bind(options));

        // configure Logging with NLog
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
            loggingBuilder.AddNLog(config);
        });

        services.AddSingleton<IConsoleService, ConsoleService>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IParserService, ParserService>();
        services.AddSingleton<ICommandParser, CommandParser>();
        services.AddSingleton<IConfigWriter, ConfigWriter>(provider =>
        {
            var configWriter = new ConfigWriter();
            configWriter.AddOptions(Constants.OctoToolOptionsRootNode,
                provider.GetRequiredService<IOptions<OctoToolOptions>>());
            configWriter.AddOptions(Constants.AuthenticationRootNode,
                provider.GetRequiredService<IOptions<OctoToolAuthenticationOptions>>());
            return configWriter;
        });

        services.AddOptions<AuthenticatorOptions>()
            .Configure<IOptions<OctoToolOptions>>(
                (options, toolOptions) =>
                {
                    options.IssuerUri = toolOptions.Value.IdentityServiceUrl ?? string.Empty;
                    options.ClientId = CommonConstants.OctoToolClientId;
                    options.ClientSecret = CommonConstants.OctoToolClientSecret;
                });

        services.AddOptions<TenantClientOptions>()
            .Configure<IOptions<OctoToolOptions>>(
                (options, toolOptions) =>
                {
                    options.TenantId = toolOptions.Value.TenantId ?? string.Empty;
                    options.EndpointUri = toolOptions.Value.AssetServiceUrl;
                });

        services.AddOptions<AssetServiceClientOptions>()
            .Configure<IOptions<OctoToolOptions>>(
                (options, toolOptions) => { options.EndpointUri = toolOptions.Value.AssetServiceUrl; });

        services.AddOptions<BotServiceClientOptions>()
            .Configure<IOptions<OctoToolOptions>>(
                (options, toolOptions) => { options.EndpointUri = toolOptions.Value.BotServiceUrl; });

        services.AddOptions<CommunicationServiceClientOptions>()
            .Configure<IOptions<OctoToolOptions>>(
                (options, toolOptions) => { options.EndpointUri = toolOptions.Value.CommunicationServiceUrl; });

        services.AddOptions<IdentityServiceClientOptions>()
            .Configure<IOptions<OctoToolOptions>>(
                (options, toolOptions) => { options.EndpointUri = toolOptions.Value.IdentityServiceUrl; });

        services.AddOptions<StreamDataServiceClientOptions>()
            .Configure<IOptions<OctoToolOptions>>(
                (options, toolOptions) => options.EndpointUri = toolOptions.Value.AssetServiceUrl
            );

        services.AddOptions<AdminPanelClientOptions>()
            .Configure<IOptions<OctoToolOptions>>(
                (options, toolOptions) => { options.EndpointUri = toolOptions.Value.AdminPanelUrl; });

        services.AddSingleton<ITenantClientAccessToken, ServiceClientAccessToken>();
        services.AddSingleton<IBotServiceClientAccessToken, ServiceClientAccessToken>();
        services.AddSingleton<IIdentityServiceClientAccessToken, ServiceClientAccessToken>();
        services.AddSingleton<IAssetServiceClientAccessToken, ServiceClientAccessToken>();
        services.AddSingleton<IAdminPanelClientAccessToken, ServiceClientAccessToken>();
        services.AddSingleton<ICommunicationServiceClientAccessToken, ServiceClientAccessToken>();
        services.AddSingleton<IStreamDataServiceClientAccessToken, ServiceClientAccessToken>();

        services.AddSingleton<ITenantClient, TenantClient>();
        services.AddSingleton<IAssetServicesClient, AssetServicesClient>();
        services.AddSingleton<IIdentityServicesClient, IdentityServicesClient>();
        services.AddSingleton<IIdentityServicesSetupClient, IdentityServicesSetupClient>();
        services.AddSingleton<IBotServicesClient, BotServicesClient>();
        services.AddSingleton<ICommunicationServicesClient, CommunicationServicesClient>();
        services.AddSingleton<IAuthenticatorClient, AuthenticatorClient>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<INotificationRepository, WsNotificationRepository>();
        services.AddSingleton<IStreamDataServicesClient, StreamDataServicesClient>();
        services.AddSingleton<IAdminPanelClient, AdminPanelClient>();

        services.AddTransient<ICommand, ConfigOctoCommand>();
        services.AddTransient<ICommand, SetupCommand>();

        services.AddTransient<ICommand, LogInCommand>();
        services.AddTransient<ICommand, AuthStatusCommand>();

        services.AddTransient<ICommand, ImportConstructionKitModel>();
        services.AddTransient<ICommand, ImportRuntimeModel>();
        services.AddTransient<ICommand, ExportRuntimeModelByQuery>();
        services.AddTransient<ICommand, ExportRuntimeModelByDeepGraph>();

        services.AddTransient<ICommand, GetClients>();
        services.AddTransient<ICommand, AddAuthorizationCodeClient>();
        services.AddTransient<ICommand, AddClientCredentialsClient>();
        services.AddTransient<ICommand, AddDeviceCodeClient>();
        services.AddTransient<ICommand, UpdateClient>();
        services.AddTransient<ICommand, DeleteClient>();

        services.AddTransient<ICommand, GetIdentityProviders>();
        services.AddTransient<ICommand, AddOAuthIdentityProvider>();
        services.AddTransient<ICommand, AddOpenLdapIdentityProvider>();
        services.AddTransient<ICommand, AddActiveDirectoryIdentityProvider>();
        services.AddTransient<ICommand, AddAzureEntryIdIdentityProvider>();
        services.AddTransient<ICommand, UpdateIdentityProvider>();
        services.AddTransient<ICommand, DeleteIdentityProvider>();

        services.AddTransient<ICommand, CreateTenant>();
        services.AddTransient<ICommand, CleanTenant>();
        services.AddTransient<ICommand, AttachTenant>();
        services.AddTransient<ICommand, DeleteTenant>();
        services.AddTransient<ICommand, ClearTenantCache>();
        services.AddTransient<ICommand, UpdateSystemCkModelTenant>();

        services.AddTransient<ICommand, GetUsers>();
        services.AddTransient<ICommand, CreateUser>();
        services.AddTransient<ICommand, UpdateUser>();
        services.AddTransient<ICommand, DeleteUser>();
        services.AddTransient<ICommand, ResetPassword>();
        services.AddTransient<ICommand, AddUserToRole>();
        services.AddTransient<ICommand, RemoveUserFromRole>();

        services.AddTransient<ICommand, GetRoles>();
        services.AddTransient<ICommand, CreateRole>();
        services.AddTransient<ICommand, UpdateRole>();
        services.AddTransient<ICommand, DeleteRole>();

        services.AddTransient<ICommand, GetApiScopes>();
        services.AddTransient<ICommand, CreateApiScope>();
        services.AddTransient<ICommand, UpdateApiScope>();
        services.AddTransient<ICommand, DeleteApiScope>();

        services.AddTransient<ICommand, GetApiSecretsApiResource>();
        services.AddTransient<ICommand, GetApiSecretsClient>();
        services.AddTransient<ICommand, CreateApiSecretApiResource>();
        services.AddTransient<ICommand, CreateApiSecretClient>();
        services.AddTransient<ICommand, UpdateApiSecretApiResource>();
        services.AddTransient<ICommand, UpdateApiSecretClient>();
        services.AddTransient<ICommand, DeleteApiSecretApiResource>();
        services.AddTransient<ICommand, DeleteApiSecretClient>();
        services.AddTransient<ICommand, AddScopeToClient>();

        services.AddTransient<ICommand, GetServiceHooks>();
        services.AddTransient<ICommand, CreateServiceHook>();
        services.AddTransient<ICommand, UpdateServiceHook>();
        services.AddTransient<ICommand, DeleteServiceHook>();

        services.AddTransient<ICommand, GetNotificationMessages>();
        services.AddTransient<ICommand, CreateNotification>();
        services.AddTransient<ICommand, CompletePendingNotifications>();
        services.AddTransient<ICommand, ResetNotificationsInError>();

        services.AddTransient<ICommand, CreateLargeBinary>();
        services.AddTransient<ICommand, DownloadLargeBinary>();

        services.AddTransient<ICommand, GetApiResources>();
        services.AddTransient<ICommand, CreateApiResource>();
        services.AddTransient<ICommand, DeleteApiResource>();
        services.AddTransient<ICommand, UpdateApiResource>();

        services.AddTransient<ICommand, EnableCommunicationCommand>();
        services.AddTransient<ICommand, DisableCommunicationCommand>();

        services.AddTransient<ICommand, EnableStreamDataCommand>();
        services.AddTransient<ICommand, DisableStreamDataCommand>();
        
        services.AddTransient<ICommand, GenerateOperatorCertificatesCommand>();
        
        services.AddTransient<ICommand, ReconfigureLogLevel>();

        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider;
    }
}