using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
/// Generates and opens an HTML viewer for tenant comparison results
/// </summary>
internal static class ComparisonViewerGenerator
{
    private const string TemplateResourceName = "Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Bots.TenantComparison.template.html";
    private const string BundleResourceName = "Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Bots.TenantComparison.dist.viewer.bundle.js";

    /// <summary>
    /// Generates an HTML viewer file with the comparison data and opens it in the default browser
    /// </summary>
    /// <param name="jsonFilePath">Path to the JSON comparison result file</param>
    /// <param name="logger">Logger instance</param>
    public static void GenerateAndOpen(string jsonFilePath, ILogger logger)
    {
        try
        {
            logger.LogInformation("Generating comparison viewer for file: {FilePath}", jsonFilePath);

            // Read the comparison JSON data
            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException($"Comparison result file not found: {jsonFilePath}");
            }

            var jsonData = File.ReadAllText(jsonFilePath);

            // Validate JSON
            try
            {
                JsonDocument.Parse(jsonData);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid JSON in comparison file: {ex.Message}", ex);
            }

            // Load embedded resources
            var assembly = Assembly.GetExecutingAssembly();
            var htmlTemplate = LoadEmbeddedResource(assembly, TemplateResourceName);
            var bundleJs = LoadEmbeddedResource(assembly, BundleResourceName);

            // Inject the comparison data and bundle into the HTML template
            var html = InjectDataAndBundle(htmlTemplate, jsonData, bundleJs);

            // Generate a unique temp file
            var tempFileName = $"octo-comparison-{Guid.NewGuid():N}.html";
            var tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

            // Write the HTML file
            File.WriteAllText(tempFilePath, html, Encoding.UTF8);

            logger.LogInformation("Comparison viewer generated at: {TempFilePath}", tempFilePath);
            logger.LogInformation("Opening viewer in default browser...");

            // Open in default browser
            var processStartInfo = new ProcessStartInfo(tempFilePath)
            {
                UseShellExecute = true
            };
            Process.Start(processStartInfo);

            logger.LogInformation("Viewer opened successfully. The file will remain available at the temp location for later viewing.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate or open comparison viewer");
            throw;
        }
    }

    private static string LoadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            var availableResources = assembly.GetManifestResourceNames();
            var resourceList = string.Join(", ", availableResources);
            throw new InvalidOperationException(
                $"Embedded resource not found: {resourceName}. Available resources: {resourceList}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string InjectDataAndBundle(string htmlTemplate, string jsonData, string bundleJs)
    {
        // Escape the JSON data for embedding in JavaScript
        var escapedJson = jsonData
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        // Create the data injection script
        var dataScript = $"window.COMPARISON_DATA = JSON.parse('{escapedJson}');";

        // Inject the data script
        var html = htmlTemplate.Replace("// <!--INJECT_DATA-->", dataScript);

        // Inject the bundle as an inline script
        var bundleScript = $"<script>{bundleJs}</script>";
        html = html.Replace("<!--INJECT_BUNDLE-->", bundleScript);

        return html;
    }
}
