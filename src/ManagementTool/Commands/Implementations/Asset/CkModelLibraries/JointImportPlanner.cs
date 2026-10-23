using Meshmakers.Octo.Sdk.ServiceClient.AssetRepositoryServices.CkModelCatalog;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Asset.CkModelLibraries;

/// <summary>
///     Client-side planning of a joint CK model import (AB#7376): a group of installed models that pin each other
///     exactly (for example Basic, Industry.Basic and Industry.Energy) can only be updated together, as one set that the
///     tenant upgrade guard judges as one post-state. The asset repository imports <c>ModelsToImport</c> in the order it
///     receives them and concatenates the dependency trees of the roots in request order, so the CLI orders the set
///     itself: a model is sent after every other model of the set whose name appears in its dependency tree.
/// </summary>
internal static class JointImportPlanner
{
    /// <summary>The model name of a full model id (<c>Industry.Basic-2.6.0</c> gives <c>Industry.Basic</c>).</summary>
    public static string NameOf(string modelId)
    {
        var index = modelId.LastIndexOf('-');
        return index > 0 ? modelId[..index] : modelId;
    }

    /// <summary>
    ///     Orders <see cref="BatchDependencyResolutionResponseDto.ModelsToImport" /> so that every model comes after each
    ///     other model of the set whose name appears in its dependency tree. Ties keep the resolver order (stable); a
    ///     dependency cycle - which the compiler rejects - leaves the remaining models in resolver order.
    /// </summary>
    public static List<string> OrderByDependencies(BatchDependencyResolutionResponseDto resolution)
    {
        var models = resolution.ModelsToImport.Distinct().ToList();
        if (models.Count < 2)
        {
            return models;
        }

        var names = models.ToDictionary(m => m, NameOf);
        var predecessors = models.ToDictionary(
            m => m,
            m => models.Where(o => o != m && TreeNamesOf(m, resolution).Contains(names[o])).ToHashSet());

        var ordered = new List<string>(models.Count);
        var remaining = new List<string>(models);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(m => predecessors[m].All(p => ordered.Contains(p))) ?? remaining[0];
            ordered.Add(next);
            remaining.Remove(next);
        }

        return ordered;
    }

    /// <summary>
    ///     The joint command an operator runs when <paramref name="requested" /> alone is blocked: the requested models
    ///     plus every suggested dependent update of the blockers that is not yet in the set. <c>null</c> when the
    ///     blockers propose nothing new.
    /// </summary>
    public static string? BuildJointCommand(string catalogName, IReadOnlyCollection<string> requested,
        IEnumerable<CkUpgradeBlockerDto> blockers, bool wait)
    {
        var set = requested.ToList();
        var names = set.Select(NameOf).ToHashSet();
        var added = false;
        foreach (var suggestion in blockers.Select(b => b.SuggestedDependentUpdate)
                     .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).Distinct())
        {
            if (names.Add(NameOf(suggestion)))
            {
                set.Add(suggestion);
                added = true;
            }
        }

        return added
            ? $"octo-cli -c ImportFromCatalog -cn {catalogName} -m {string.Join(",", set)}{(wait ? " -w" : string.Empty)}"
            : null;
    }

    private static HashSet<string> TreeNamesOf(string modelId, BatchDependencyResolutionResponseDto resolution)
    {
        var result = new HashSet<string>();
        foreach (var tree in resolution.DependencyTrees)
        {
            if (tree.RootModel != null)
            {
                Collect(tree.RootModel, modelId, false, result);
            }
        }

        return result;
    }

    // Adds the names of all dependencies below every occurrence of modelId in the tree.
    private static void Collect(DependencyResolutionItemDto item, string modelId, bool inside, HashSet<string> result)
    {
        var isTarget = inside || item.ModelId == modelId;
        foreach (var dependency in item.Dependencies)
        {
            if (isTarget)
            {
                result.Add(string.IsNullOrEmpty(dependency.Name) ? NameOf(dependency.ModelId) : dependency.Name);
            }

            Collect(dependency, modelId, isTarget, result);
        }
    }
}
