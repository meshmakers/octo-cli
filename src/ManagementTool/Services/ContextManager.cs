using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

public class ContextManager : IContextManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directoryPath;
    private readonly string _contextsFilePath;
    private readonly string _settingsFilePath;

    private ContextConfiguration _configuration;

    public ContextManager()
    {
        _directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            $".{Constants.OctoToolUserFolderName}");
        _contextsFilePath = Path.Combine(_directoryPath, "contexts.json");
        _settingsFilePath = Path.Combine(_directoryPath, "settings.json");
        _configuration = new ContextConfiguration();
    }

    public ContextConfiguration Load()
    {
        if (!File.Exists(_contextsFilePath))
        {
            _configuration = new ContextConfiguration();
            return _configuration;
        }

        var json = File.ReadAllText(_contextsFilePath);
        _configuration = JsonSerializer.Deserialize<ContextConfiguration>(json, JsonOptions)
                         ?? new ContextConfiguration();
        return _configuration;
    }

    public void Save(ContextConfiguration configuration)
    {
        _configuration = configuration;
        SaveToFile();
    }

    public ContextEntry? GetActiveContext()
    {
        if (string.IsNullOrEmpty(_configuration.ActiveContext))
        {
            return null;
        }

        return _configuration.Contexts.GetValueOrDefault(_configuration.ActiveContext);
    }

    public string? GetActiveContextName()
    {
        return _configuration.ActiveContext;
    }

    public void AddOrUpdateContext(string name, ContextEntry entry)
    {
        _configuration.Contexts[name] = entry;

        // Auto-activate if this is the first context or no active context
        if (string.IsNullOrEmpty(_configuration.ActiveContext) || _configuration.Contexts.Count == 1)
        {
            _configuration.ActiveContext = name;
        }

        SaveToFile();
    }

    public void RemoveContext(string name)
    {
        if (!_configuration.Contexts.Remove(name))
        {
            return;
        }

        // If the removed context was active, switch to another or clear
        if (_configuration.ActiveContext == name)
        {
            _configuration.ActiveContext = _configuration.Contexts.Keys.FirstOrDefault();
        }

        SaveToFile();
    }

    public void SetActiveContext(string name)
    {
        if (!_configuration.Contexts.ContainsKey(name))
        {
            throw new ToolException($"Context '{name}' does not exist.");
        }

        _configuration.ActiveContext = name;
        SaveToFile();
    }

    public IReadOnlyDictionary<string, ContextEntry> ListContexts()
    {
        return _configuration.Contexts;
    }

    public void MigrateIfNeeded()
    {
        if (File.Exists(_contextsFilePath))
        {
            return;
        }

        if (!File.Exists(_settingsFilePath))
        {
            return;
        }

        Logger.Info("Migrating settings.json to contexts.json");

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var entry = new ContextEntry();

            if (root.TryGetProperty(Constants.OctoToolOptionsRootNode, out var optionsElement))
            {
                var optionsJson = optionsElement.GetRawText();
                entry.OctoToolOptions = JsonSerializer.Deserialize<OctoToolOptions>(optionsJson, JsonOptions)
                                        ?? new OctoToolOptions();
            }

            if (root.TryGetProperty(Constants.AuthenticationRootNode, out var authElement))
            {
                var authJson = authElement.GetRawText();
                entry.Authentication = JsonSerializer.Deserialize<OctoToolAuthenticationOptions>(authJson, JsonOptions)
                                       ?? new OctoToolAuthenticationOptions();
            }

            _configuration = new ContextConfiguration
            {
                ActiveContext = "default",
                Contexts = new Dictionary<string, ContextEntry> { ["default"] = entry }
            };

            SaveToFile();
            Logger.Info("Migration complete. Settings imported as 'default' context.");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to migrate settings.json. Starting with empty context configuration.");
            _configuration = new ContextConfiguration();
        }
    }

    public void SaveActiveContext()
    {
        SaveToFile();
    }

    private void SaveToFile()
    {
        if (!Directory.Exists(_directoryPath))
        {
            Directory.CreateDirectory(_directoryPath);
        }

        var json = JsonSerializer.Serialize(_configuration, JsonOptions);
        File.WriteAllText(_contextsFilePath, json);
    }
}
