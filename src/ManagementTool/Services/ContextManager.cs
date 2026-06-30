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
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    // baseDirectory is the parent of the ".octo-cli" folder; the parameterless
    // constructor uses the user profile. The overload exists so tests can point at
    // a throwaway directory instead of the developer's real ~/.octo-cli.
    public ContextManager(string baseDirectory)
    {
        _directoryPath = Path.Combine(baseDirectory, $".{Constants.OctoToolUserFolderName}");
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
        NormalizeContextComparer();
        return _configuration;
    }

    // Context names are matched case-insensitively (like kubectl). System.Text.Json
    // always deserializes a Dictionary with the ordinal (case-sensitive) comparer
    // regardless of the property initializer, so rebuild it with OrdinalIgnoreCase
    // after every load/migrate. On a case-only collision the last entry wins, which
    // mirrors how the names are treated as the same context from now on.
    private void NormalizeContextComparer()
    {
        if (_configuration.Contexts.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _configuration.Contexts =
            new Dictionary<string, ContextEntry>(_configuration.Contexts, StringComparer.OrdinalIgnoreCase);
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
        // Contexts is keyed case-insensitively; resolve to the stored key so the
        // persisted ActiveContext matches an actual context name exactly.
        if (!_configuration.Contexts.TryGetValue(name, out _))
        {
            throw new ToolException($"Context '{name}' does not exist.");
        }

        _configuration.ActiveContext =
            _configuration.Contexts.Keys.First(k => StringComparer.OrdinalIgnoreCase.Equals(k, name));
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
                Contexts = new Dictionary<string, ContextEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["default"] = entry
                }
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
