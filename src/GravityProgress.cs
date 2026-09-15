namespace Gravity;

/// <summary>Visit order is independent of the original map rows.</summary>
internal sealed class GravityProgress<T> where T : notnull
{
    public const int RequiredEncounters = 15;
    public HashSet<T> Visited { get; }
    public HashSet<T> Available { get; }
    public int EncountersVisited { get; }
    public bool Started { get; }

    public GravityProgress(T start, IReadOnlyList<T> encounters, IReadOnlyList<T> bosses,
        IEnumerable<T> visited)
    {
        Visited = visited.ToHashSet();
        Started = Visited.Contains(start);
        EncountersVisited = encounters.Distinct().Count(Visited.Contains);
        Available = [];
        if (!Started)
            Available.Add(start);
        else if (EncountersVisited < RequiredEncounters && !bosses.Any(Visited.Contains))
            Available.UnionWith(encounters.Where(point => !Visited.Contains(point)));
        else if (EncountersVisited >= RequiredEncounters)
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
