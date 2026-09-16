namespace Gravity;

/// <summary>Visit order is independent of the original map rows.</summary>
internal sealed class GravityProgress<T> where T : notnull
{
    public int RequiredEncounters { get; }
    public HashSet<T> Visited { get; }
    public HashSet<T> Available { get; }
    public int EncountersVisited { get; }
    public bool Started { get; }

    public GravityProgress(T start, IReadOnlyList<T> encounters, IReadOnlyList<T> bosses,
        IEnumerable<T> visited, int requiredEncounters = 15, bool lockEncountersAfterBossUnlock = false)
    {
        var total = encounters.Distinct().Count();
        RequiredEncounters = requiredEncounters == -1 ? total : Math.Clamp(requiredEncounters, 0, total);
        Visited = visited.ToHashSet();
        Started = Visited.Contains(start);
        EncountersVisited = encounters.Distinct().Count(Visited.Contains);
        Available = [];
        if (!Started)
            Available.Add(start);
        else
        {
            var encountersLocked = lockEncountersAfterBossUnlock && RequiredEncounters > 0
                && EncountersVisited >= RequiredEncounters;
            if (!bosses.Any(Visited.Contains) && !encountersLocked)
                Available.UnionWith(encounters.Where(point => !Visited.Contains(point)));
            if (EncountersVisited >= RequiredEncounters)
            {
                foreach (var boss in bosses)
                {
                    if (Visited.Contains(boss)) continue;
                    Available.Add(boss);
                    break;
                }
            }
        }
    }
}
