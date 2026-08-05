using System.Reflection;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Frontend.ManagementTool.Services;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Meshmakers.Octo.Sdk.ServiceClient.Authorization;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool;

internal class Runner
{
    private readonly ILogger<Runner> _logger;
    private readonly ICommandParser _parser;
    private readonly IContextManager _contextManager;
    private readonly ContextSelection _contextSelection;

    public Runner(ILogger<Runner> logger, ICommandParser parser, IContextManager contextManager,
        ContextSelection contextSelection)
    {
        _logger = logger;
        _parser = parser;
        _contextManager = contextManager;
        _contextSelection = contextSelection;
    }

    public async Task<int> DoActionAsync()
    {
        try
        {
            _logger.LogInformation("Octo Mesh Management Tool, Version {ProductVersion}",
                GetProductVersion());
            _logger.LogInformation("{Copyright}", GetCopyright());
            _logger.LogInformation("Executable directory: {BinDirectory}", GetBinDirectory());

            LogContextInUse();

            await _parser.ParseAndValidateAsync(Constants.OctoExeName);

            return 0;
        }
        catch (MandatoryArgumentsMissingException ex)
        {
            _logger.LogError("{Message}", ex.Message);

            _parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (ArgumentValueMissingException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return -1;
        }
        catch (InvalidParameterException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return -1;
        }
        catch (ServiceConfigurationMissingException ex)
        {
            _logger.LogError("{Message}, Please use the 'config' command", ex.Message);
            return -2;
        }
        catch (ServiceClientResultException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return -3;
        }
        catch (ServiceClientException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return -3;
        }
        catch (AuthorizationFailedException ex)
        {
            _logger.LogError("Authorization failed: {Message}", ex.Message);

            return -4;
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogError("Authentication failed: {Message}", ex.Message);

            return -4;
        }
        catch (ToolException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return -5;
        }
        catch (Exception ex)
        {
            var tmp = ex;
            while (tmp != null)
            {
                _logger.LogCritical(tmp, "{Message}", tmp.Message);
                tmp = tmp.InnerException;
            }

            return -99;
        }
    }

    /// <summary>
    ///     Names the context this run works with. Logged for every command, because with parallel
    ///     invocations against different contexts the output is otherwise impossible to attribute.
    /// </summary>
    private void LogContextInUse()
    {
        var name = _contextManager.GetEffectiveContextName();

        if (_contextSelection.IsOverridden)
        {
            _logger.LogInformation(
                "Using context '{ContextName}' from {ContextSource} (active context '{ActiveContextName}' unchanged)",
                name, _contextSelection.Source, _contextManager.GetActiveContextName() ?? "<none>");
        }
        else
        {
            _logger.LogInformation("Using active context '{ContextName}'", name ?? "<none>");
        }

        _logger.LogInformation("Context file: {ConfigurationFilePath}", _contextManager.ConfigurationFilePath);
    }

    private static string GetProductVersion()
    {
        var attribute = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyFileVersionAttribute>()
            .Single();
        return attribute.Version;
    }

    private static string GetCopyright()
    {
        var attribute = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyCopyrightAttribute>()
            .SingleOrDefault();
        if (attribute == null)
        {
            return "Development version";
        }
        return attribute.Copyright;
    }
    
    private static string GetBinDirectory()
    {
        return AppContext.BaseDirectory;
    }
}
