namespace ThreeDS.Progressive.Services;

/// <summary>
/// The entries of a history that still describe the project. «Deshacer» appends an entry with <c>UndoOf</c> and keeps the original
/// (the history is an audit trail), so recipes and applied options must not read entries whose effect was reverted.
/// An undo entry is itself reverted when a later undo targets it, which makes the original effective again.
/// </summary>
internal static class EffectiveHistory
{
    /// <summary>Ids of entries reverted by an effective undo, plus the children of a reverted Global Template application.</summary>
    internal static HashSet<Guid> Reverted(IReadOnlyList<HistoryEntry> entries)
    {
        var reverted=new HashSet<Guid>();
        // Undo entries always come after their target, so walking newest to oldest knows whether each undo still stands.
        for(int i=entries.Count-1;i>=0;i--)
        {
            var entry=entries[i];
            if(reverted.Contains(entry.Id))continue;
            if(entry.Parameters.TryGetValue("UndoOf",out var target)&&Guid.TryParse(target,out var id))reverted.Add(id);
        }
        // The recipe is undone through its master entry; its per-action rows share the GlobalApplication id.
        var applications=entries.Where(e=>reverted.Contains(e.Id)&&e.Parameters.ContainsKey("GlobalTemplateSnapshot")&&e.Parameters.ContainsKey("GlobalApplication"))
            .Select(e=>e.Parameters["GlobalApplication"]).ToHashSet();
        if(applications.Count>0)
            foreach(var entry in entries)
                if(!entry.Parameters.ContainsKey("GlobalTemplateSnapshot")&&entry.Parameters.TryGetValue("GlobalApplication",out var app)&&applications.Contains(app))reverted.Add(entry.Id);
        return reverted;
    }
    internal static IReadOnlyList<HistoryEntry> Entries(IReadOnlyList<HistoryEntry> entries)
    {
        if(!entries.Any(e=>e.Parameters.ContainsKey("UndoOf")))return entries;
        var reverted=Reverted(entries);
        return entries.Where(e=>!reverted.Contains(e.Id)).ToArray();
    }
    internal static HistoryDocument Of(HistoryDocument history)=>ReferenceEquals(Entries(history.Entries),history.Entries)?history:history with {Entries=Entries(history.Entries)};
}
