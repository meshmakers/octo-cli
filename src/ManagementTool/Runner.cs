using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.Common.Shared;
using Meshmakers.Octo.Sdk.ServiceClient;
using Meshmakers.Octo.Sdk.ServiceClient.Authentication;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool;

internal class Runner
{
    private readonly ILogger<Runner> _logger;
    private readonly ICommandParser _parser;

    public Runner(ILogger<Runner> logger, ICommandParser parser)
    {
        _logger = logger;
        _parser = parser;
    }

    public async Task<int> DoActionAsync()
    {
        try
        {
            _logger.LogInformation("Octo Mesh Management Tool, Version {ProductVersion}",
                GetProductVersion());
            _logger.LogInformation("{Copyright}", GetCopyright());

            await _parser.ParseAndValidateAsync();

            return 0;
        }
        catch (MandatoryArgumentsMissingException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            _parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (InvalidProgramException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            _parser.ShowUsageInformation(Constants.OctoExeName);
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
        catch (AuthenticationFailedException ex)
        {
            _logger.LogError(ex, "Authentication failed: {Message}", ex.Message);

            return -4;
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
            .Single();

        return attribute.Copyright;
    }
}
