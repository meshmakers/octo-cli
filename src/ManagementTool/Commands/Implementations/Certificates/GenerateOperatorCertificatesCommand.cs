using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Certificates;

public class GenerateOperatorCertificatesCommand : Command<OctoToolOptions>
{
    private readonly IArgument _outputArg;
    private readonly IArgument _namespaceArg;
    private readonly IArgument _serverNameArg;

    public GenerateOperatorCertificatesCommand(ILogger<GenerateOperatorCertificatesCommand> logger, 
        IOptions<OctoToolOptions> options)
        : base(logger, "GenerateOperatorCertificates", "Generate CA and service/server certificates to run OctoMesh operator", options)
    {
        _outputArg = CommandArgumentValue.AddArgument("o", "output", ["Directory path where certificates are stored."], true, 1);
        _serverNameArg = CommandArgumentValue.AddArgument("s", "serverName", ["Name of service or server of operator, e. g. octo-mesh-op1-communication-operator."], true, 1);
        _namespaceArg = CommandArgumentValue.AddArgument("n", "namespace", ["Namespace of operator, e. g. octo-operator-system."], true, 1);
    }

    public override Task Execute()
    {
        var outputPath = CommandArgumentValue.GetArgumentScalarValue<string>(_outputArg);
        var serverName = CommandArgumentValue.GetArgumentScalarValue<string>(_serverNameArg);
        var namespaceName = CommandArgumentValue.GetArgumentScalarValue<string>(_namespaceArg);
        
        Logger.LogInformation("Generating CA and Service certificate at \'{OutputPath}\'", outputPath);
        
        using CertificateGenerator generator = new(serverName, namespaceName);

        File.WriteAllText(Path.Combine(outputPath, "ca.pem"), generator.Root.Certificate.EncodeToPem());
        File.WriteAllText(Path.Combine(outputPath, "ca-key.pem"), generator.Root.Key.EncodeToPem());
        File.WriteAllText(Path.Combine(outputPath, "svc.pem"), generator.Server.Certificate.EncodeToPem());
        File.WriteAllText(Path.Combine(outputPath, "svc-key.pem"), generator.Server.Key.EncodeToPem());

        return Task.CompletedTask;
    }
}