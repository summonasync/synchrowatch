using System.Security.Cryptography;

namespace Syncrowatch.Core.Engine;

public sealed class ChangeBatcher : IDisposable
{
    private readonly string _root;
    private readonly TimeSpan _debounce;
    private readonly SyncIgnoreRules _rules;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private readonly Dictionary<string, ContentState> _pending = new(StringComparer.Ordinal);
    private bool _disposed;

    public event Action<IReadOnlyList<string>>? BatchReady;

    public ChangeBatcher(string root, TimeSpan debounce, SyncIgnoreRules rules, TimeProvider? timeProvider = null)
    {
        if (debounce <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounce), debounce, "debounce must be positive");
        }

        _root = Path.GetFullPath(root);
        _debounce = debounce;
        _rules = rules;
        _timer = (timeProvider ?? TimeProvider.System).CreateTimer(Flush, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public void FlushNow() => Flush(null);

    public IReadOnlyCollection<string> PendingPaths
    {
        get
        {
            lock (_gate)
            {
                return [.. _pending.Keys];
            }
        }
    }

    public void Accept(FolderChange change)
    {
        var relative = change.RelativePath.Replace('\\', '/');
        var fullPath = Path.Combine(_root, relative);
        if (Directory.Exists(fullPath) || _rules.IsIgnored(relative))
        {
            return;
        }

        var state = ContentState.Probe(fullPath);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending[relative] = state;
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
        }

        _timer.Dispose();
    }

    private void Flush(object? state)
    {
        List<KeyValuePair<string, ContentState>> snapshot;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            snapshot = [.. _pending];
            _pending.Clear();
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        var ready = new List<string>();
        var deferred = new List<KeyValuePair<string, ContentState>>();
        foreach (var (relative, expected) in snapshot)
        {
            var current = ContentState.Probe(Path.Combine(_root, relative));
            if (ContentState.Settled(expected, current))
            {
                ready.Add(relative);
            }
            else
            {
                deferred.Add(new KeyValuePair<string, ContentState>(relative, current));
            }
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var entry in deferred)
            {
                _pending[entry.Key] = entry.Value;
            }

            if (deferred.Count > 0)
            {
                _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }

        if (ready.Count > 0)
        {
            BatchReady?.Invoke(ready);
        }
    }

    private readonly record struct ContentState(bool Present, string? Hash)
    {
        public static ContentState Probe(string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                return new ContentState(false, null);
            }

            try
            {
                using var stream = File.OpenRead(fullPath);
                return new ContentState(true, Convert.ToHexStringLower(SHA256.HashData(stream)));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return new ContentState(false, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new ContentState(true, null);
            }
        }

        public static bool Settled(ContentState expected, ContentState current)
        {
            if (!expected.Present)
            {
                return !current.Present;
            }

            return current.Present
                && expected.Hash is { Length: > 0 } baseline
                && current.Hash == baseline;
        }
    }
}
