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

    // Writes go through Mutate: re-read the file, apply only this process's change, replace it
    // atomically, all while holding a cross-process lock. That way parallel octo-cli invocations
    // (each with its own --context) do not lose each other's changes. The one exception is
    // MigrateIfNeeded, which writes a whole configuration without re-reading — correct there,
    // because it only runs when contexts.json does not exist yet.
    // The retries cover the window in which another process holds the lock or has the file open.
    private const int FileAccessRetryCount = 50;
    private const int FileAccessRetryDelayMilliseconds = 100;

    private readonly string _directoryPath;
    private readonly string _contextsFilePath;
    private readonly string _settingsFilePath;
    private readonly string _lockFilePath;

    private ContextConfiguration _configuration;

    // Name of the context chosen for this invocation only (--context / OCTO_CLI_CONTEXT).
    // Null means "use the persisted active context". Selecting a context never writes.
    private string? _selectedContextName;

    public ContextManager()
        : this(ResolveBaseDirectory())
    {
    }

    // baseDirectory is the parent of the ".octo-cli" folder; the parameterless
    // constructor uses OCTO_CLI_HOME or the user profile. The overload exists so tests can
    // point at a throwaway directory instead of the developer's real ~/.octo-cli.
    public ContextManager(string baseDirectory)
    {
        _directoryPath = Path.Combine(baseDirectory, $".{Constants.OctoToolUserFolderName}");
        _contextsFilePath = Path.Combine(_directoryPath, "contexts.json");
        _settingsFilePath = Path.Combine(_directoryPath, "settings.json");
        _lockFilePath = Path.Combine(_directoryPath, "contexts.lock");
        _configuration = new ContextConfiguration();
    }

    /// <summary>
    ///     Path of the file the contexts are read from and written to. Surfaced so commands can
    ///     name it in their output — with OCTO_CLI_HOME in play, guessing ~/.octo-cli is wrong.
    /// </summary>
    public string ConfigurationFilePath => _contextsFilePath;

    /// <inheritdoc />
    public bool IsContextOverridden => _selectedContextName != null;

    // OCTO_CLI_HOME points at the parent of the ".octo-cli" folder, matching the
    // baseDirectory parameter above. Giving each parallel job its own value is the
    // strongest form of isolation: separate files instead of a shared one.
    //
    // Trimmed because stray whitespace does not fail — it silently resolves to a *different*
    // directory, where the tool then reports "No contexts configured" with exit code 0 and writes
    // a second configuration tree. `set OCTO_CLI_HOME=C:\foo ` in cmd.exe keeps that trailing
    // space, and pipeline variables pick them up just as easily.
    private static string ResolveBaseDirectory()
    {
        var home = Environment.GetEnvironmentVariable(Constants.EnvVarHome);

        return string.IsNullOrWhiteSpace(home)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : home.Trim();
    }

    public ContextConfiguration Load()
    {
        _configuration = ReadFromFile();
        return _configuration;
    }

    // Context names are matched case-insensitively (like kubectl). System.Text.Json
    // always deserializes a Dictionary with the ordinal (case-sensitive) comparer
    // regardless of the property initializer, so rebuild it with OrdinalIgnoreCase
    // after every load/migrate. On a case-only collision the last entry wins, which
    // mirrors how the names are treated as the same context from now on.
    private static void NormalizeContextComparer(ContextConfiguration configuration)
    {
        if (configuration.Contexts.Comparer.Equals(StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        configuration.Contexts =
            new Dictionary<string, ContextEntry>(configuration.Contexts, StringComparer.OrdinalIgnoreCase);
    }

    public ContextEntry? GetActiveContext()
    {
        return GetContext(_configuration.ActiveContext);
    }

    public string? GetActiveContextName()
    {
        return _configuration.ActiveContext;
    }

    /// <inheritdoc />
    public ContextEntry? GetEffectiveContext()
    {
        return GetContext(GetEffectiveContextName());
    }

    /// <inheritdoc />
    public string? GetEffectiveContextName()
    {
        return _selectedContextName ?? _configuration.ActiveContext;
    }

    /// <inheritdoc />
    public void SelectContext(string name)
    {
        var storedName = ResolveContextName(name);
        if (storedName == null)
        {
            throw ToolException.UnknownContext(name, _configuration.Contexts.Keys);
        }

        _selectedContextName = storedName;
    }

    private ContextEntry? GetContext(string? name)
    {
        return string.IsNullOrEmpty(name) ? null : _configuration.Contexts.GetValueOrDefault(name);
    }

    // Contexts is keyed case-insensitively; resolving to the stored key keeps the persisted
    // ActiveContext and the selected name matching an actual context name exactly.
    private string? ResolveContextName(string name)
    {
        return _configuration.Contexts.Keys.FirstOrDefault(k => StringComparer.OrdinalIgnoreCase.Equals(k, name));
    }

    public void AddOrUpdateContext(string name, ContextEntry entry)
    {
        Mutate(configuration =>
        {
            configuration.Contexts[name] = entry;

            // Auto-activate if this is the first context or no active context
            if (string.IsNullOrEmpty(configuration.ActiveContext) || configuration.Contexts.Count == 1)
            {
                configuration.ActiveContext = name;
            }
        });
    }

    public void RemoveContext(string name)
    {
        Mutate(configuration =>
        {
            if (!configuration.Contexts.Remove(name))
            {
                return;
            }

            // If the removed context was active, switch to another or clear
            if (StringComparer.OrdinalIgnoreCase.Equals(configuration.ActiveContext, name))
            {
                configuration.ActiveContext = configuration.Contexts.Keys.FirstOrDefault();
            }
        });
    }

    public void SetActiveContext(string name)
    {
        var storedName = ResolveContextName(name);
        if (storedName == null)
        {
            throw ToolException.UnknownContext(name, _configuration.Contexts.Keys);
        }

        Mutate(configuration => configuration.ActiveContext = storedName);
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

            WriteWithLock(_configuration);
            Logger.Info("Migration complete. Settings imported as 'default' context.");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to migrate settings.json. Starting with empty context configuration.");
            _configuration = new ContextConfiguration();
        }
    }

    /// <inheritdoc />
    public void SaveEffectiveContext()
    {
        var name = GetEffectiveContextName();
        var entry = GetContext(name);

        if (string.IsNullOrEmpty(name) || entry == null)
        {
            // No context to save into. Writing anything here would only rewrite the file with
            // what it already holds.
            return;
        }

        // Write only this context's entry onto whatever is currently on disk, so a token
        // refreshed by a parallel invocation for a different context is not clobbered.
        Mutate(configuration => configuration.Contexts[name] = entry);
    }

    /// <summary>
    ///     Applies a mutation under a cross-process lock: the file is re-read first so changes
    ///     made by another octo-cli process since our own load are not lost, and the result is
    ///     written atomically.
    /// </summary>
    /// <remarks>
    ///     Deliberately an <see cref="Action{T}" />: a mutation can only adjust the configuration it
    ///     is handed, never hand back a different one. Returning a wholesale replacement is exactly
    ///     how a caller would discard a parallel invocation's changes without noticing.
    /// </remarks>
    private void Mutate(Action<ContextConfiguration> mutation)
    {
        EnsureDirectory();

        using var fileLock = AcquireLock();

        var configuration = ReadFromFile();
        mutation(configuration);
        NormalizeContextComparer(configuration);

        WriteAtomic(configuration);
        _configuration = configuration;
    }

    private void WriteWithLock(ContextConfiguration configuration)
    {
        EnsureDirectory();

        using var fileLock = AcquireLock();

        WriteAtomic(configuration);
    }

    private ContextConfiguration ReadFromFile()
    {
        if (!File.Exists(_contextsFilePath))
        {
            return new ContextConfiguration();
        }

        var json = Retry(() => File.ReadAllText(_contextsFilePath));
        var configuration = JsonSerializer.Deserialize<ContextConfiguration>(json, JsonOptions)
                            ?? new ContextConfiguration();
        NormalizeContextComparer(configuration);

        return configuration;
    }

    // Writing to a temporary file and renaming it over the target means a concurrent reader
    // sees either the previous or the new file in full, never a half-written one.
    private void WriteAtomic(ContextConfiguration configuration)
    {
        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        var temporaryFilePath = _contextsFilePath + ".tmp";

        File.WriteAllText(temporaryFilePath, json);
        Retry(() =>
        {
            File.Move(temporaryFilePath, _contextsFilePath, true);
            return true;
        });
    }

    // A lock file held with FileShare.None is the cross-platform equivalent of a named mutex
    // here: it works the same on Windows and Linux and is released when the process dies.
    private FileStream AcquireLock()
    {
        return Retry(() => new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None));
    }

    private static T Retry<T>(Func<T> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < FileAccessRetryCount)
            {
                Thread.Sleep(FileAccessRetryDelayMilliseconds);
            }
            catch (UnauthorizedAccessException) when (attempt < FileAccessRetryCount)
            {
                Thread.Sleep(FileAccessRetryDelayMilliseconds);
            }
        }
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_directoryPath))
        {
            Directory.CreateDirectory(_directoryPath);
        }
    }
}
