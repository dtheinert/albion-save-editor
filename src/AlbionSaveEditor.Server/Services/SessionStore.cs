using System.Collections.Concurrent;
using AlbionSaveEditor.Domain;

namespace AlbionSaveEditor.Services;

/// <summary>In-memory store of uploaded saves, keyed by session id. Nothing is persisted to disk.</summary>
public sealed class SessionStore
{
    private const int MaxSessions = 64;

    private sealed record Entry(SaveFile Save, long Created);

    private readonly ConcurrentDictionary<string, Entry> sessions = new();
    private long counter;

    public string Add(SaveFile save)
    {
        string id = Guid.NewGuid().ToString("N");
        sessions[id] = new Entry(save, Interlocked.Increment(ref counter));
        // keep memory bounded: drop the oldest sessions
        while (sessions.Count > MaxSessions)
        {
            var oldest = sessions.OrderBy(p => p.Value.Created).First().Key;
            sessions.TryRemove(oldest, out _);
        }
        return id;
    }

    public SaveFile? Get(string id) => sessions.TryGetValue(id, out var e) ? e.Save : null;
}
