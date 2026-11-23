namespace HospitalManagement.Api.Services;

/// <summary>
/// One gate per ambulance request, shared by EVERYTHING that can change that trip's state: the driver's buttons, the server's GPS
/// arrival detection and the dispatch desk's confirmations. Whoever gets the gate first makes the change; the next one then
/// re-reads the committed state and finds the step already done, so a step is recorded once and never twice.
/// The gates live in this process (like the SignalR hubs): run a single API instance. The entries are reference-counted and removed
/// when nobody is waiting, so the table never grows.
/// </summary>
public static class AmbulanceGates
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Users;
    }

    private static readonly Dictionary<Guid, Entry> Entries = new();

    private sealed class Releaser(Guid key, Entry entry) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            entry.Gate.Release();
            Leave(key, entry);
        }
    }

    public static async Task<IDisposable> AcquireAsync(Guid key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (Entries)
        {
            if (!Entries.TryGetValue(key, out entry!)) Entries[key] = entry = new Entry();
            entry.Users++;
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken);
        }
        catch
        {
            Leave(key, entry);
            throw;
        }
        return new Releaser(key, entry);
    }

    private static void Leave(Guid key, Entry entry)
    {
        lock (Entries)
        {
            if (--entry.Users == 0) Entries.Remove(key);
        }
    }
}
