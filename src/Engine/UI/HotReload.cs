namespace Crowbar.Engine.UI;

public sealed partial class UiSystem
{
    private string? _razorPath;
    private string? _stylePath;
    private string? _watchDirectory;
    private Dictionary<string, DateTime>? _watchSnapshot;
    private string _razorClassName = "Root";
    private volatile bool _reloadRequested;
    private DateTime _reloadNotBeforeUtc;
    private DateTime _lastRazorWriteUtc;
    private DateTime _lastStyleWriteUtc;

    private bool _styleIsScoped;

    public void WatchFiles(string razorPath, string? stylePath = null, string className = "Root")
    {
        StopWatching();
        _razorPath = Path.GetFullPath(razorPath);
        _razorClassName = className;
        if (stylePath is not null)
        {
            _stylePath = Path.GetFullPath(stylePath);
            _styleIsScoped = false;
        }
        else
        {
            var associatedCss = GetAssociatedCssPath(_razorPath);
            if (File.Exists(associatedCss))
            {
                _stylePath = associatedCss;
                _styleIsScoped = true;
            }
            else
            {
                _stylePath = null;
                _styleIsScoped = false;
            }
        }
        _lastRazorWriteUtc = GetWriteTime(_razorPath);
        _lastStyleWriteUtc = _stylePath is null ? DateTime.MinValue : GetWriteTime(_stylePath);
    }

    /// <summary>Watches every .razor / .razor.css file under <paramref name="directory"/>.
    /// On change the components are re-registered and the current page is reloaded.</summary>
    public void WatchDirectory(string directory)
    {
        StopWatching();
        _watchDirectory = Path.GetFullPath(directory);
        _watchSnapshot = TakeDirectorySnapshot(_watchDirectory);
    }

    public void Update(float deltaTime = 1f / 60f)
    {
        DetectFileChanges();
        ProcessFileReload();
        RenderRazorIfNeeded();
        AdvanceAnimations(Screen, deltaTime);
        AdvanceCarets(Screen, deltaTime);
    }

    private void ProcessFileReload()
    {
        if (!_reloadRequested || DateTime.UtcNow < _reloadNotBeforeUtc) return;
        try
        {
            if (_watchDirectory is not null)
            {
                RegisterRazorComponentsFromDirectory(_watchDirectory);
                if (_currentRoute is not null && _pages.Contains(_currentRoute)) Navigate(CurrentUrl);
                else if (_currentRoute is not null) { _currentRoute = null; ShowNotFound(CurrentUrl); }
                else if (_razorRoot is not null) _razorRenderPending = true;
                else if (_pages.Count > 0) Navigate(CurrentUrl);
            }
            else if (_razorPath is not null && File.Exists(_razorPath))
            {
                LoadRazorFromFile(_razorPath, _razorClassName);
            }
            else if (_stylePath is not null && File.Exists(_stylePath))
            {
                if (_styleIsScoped)
                {
                    var scopeId = $"b-{_razorClassName.ToLowerInvariant()}";
                    LoadScopedStyles(_stylePath, ReadStableText(_stylePath), scopeId);
                }
                else
                {
                    LoadStyles(ReadStableText(_stylePath));
                }
            }
            _reloadRequested = false;
            Console.WriteLine("[UI] Hot reload applied.");
        }
        catch (IOException)
        {
            // Atomic saves commonly keep the target locked for a few frames.
            // Keep the request alive and retry after the debounce window.
            _reloadNotBeforeUtc = DateTime.UtcNow.AddMilliseconds(150);
        }
        catch (InvalidOperationException ex)
        {
            // Keep the current valid tree alive when a file is temporarily
            // invalid while the editor is writing it. The next file event will
            // schedule another attempt.
            _reloadRequested = false;
            Console.WriteLine($"[UI] Hot reload skipped: {ex.Message}");
        }
    }

    private void DetectFileChanges()
    {
        if (_watchDirectory is not null)
        {
            var snapshot = TakeDirectorySnapshot(_watchDirectory);
            if (_watchSnapshot is null || !SnapshotEqual(_watchSnapshot, snapshot))
            {
                _watchSnapshot = snapshot;
                RequestReload(_watchDirectory);
            }
            return;
        }
        if (_razorPath is not null)
        {
            var writeTime = GetWriteTime(_razorPath);
            if (writeTime != _lastRazorWriteUtc)
            {
                _lastRazorWriteUtc = writeTime;
                RequestReload(_razorPath);
            }
        }
        if (_stylePath is not null)
        {
            var writeTime = GetWriteTime(_stylePath);
            if (writeTime != _lastStyleWriteUtc)
            {
                _lastStyleWriteUtc = writeTime;
                RequestReload(_stylePath);
            }
        }
    }

    private void RequestReload(string path)
    {
        _reloadRequested = true;
        _reloadNotBeforeUtc = DateTime.UtcNow.AddMilliseconds(200);
        Console.WriteLine($"[UI] Change detected: {Path.GetFileName(path)}");
    }

    private static DateTime GetWriteTime(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    private static void AdvanceCarets(Panel panel, float deltaTime)
    {
        if (panel is TextInput input) input.AdvanceCaret(deltaTime);
        foreach (var child in panel.Children) AdvanceCarets(child, deltaTime);
    }

    private static bool AdvanceAnimations(Panel panel, float deltaTime)
    {
        var animated = panel.AdvanceStyleAnimation(deltaTime);
        foreach (var child in panel.Children) animated |= AdvanceAnimations(child, deltaTime);
        return animated;
    }

    private static string ReadStableText(string path)
    {
        string? previous = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                if (previous is not null && previous == text) return text;
                previous = text;
                Thread.Sleep(30);
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(25);
            }
        }
        return previous ?? File.ReadAllText(path);
    }

    public void StopWatching()
    {
        _razorPath = null;
        _stylePath = null;
        _watchDirectory = null;
        _watchSnapshot = null;
    }

    private static Dictionary<string, DateTime> TakeDirectorySnapshot(string directory)
    {
        var snapshot = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        if (!Directory.Exists(directory)) return snapshot;
        foreach (var path in Directory.EnumerateFiles(directory, "*.razor*", SearchOption.AllDirectories))
            snapshot[Path.GetFullPath(path)] = GetWriteTime(path);
        return snapshot;
    }

    private static bool SnapshotEqual(Dictionary<string, DateTime> a, Dictionary<string, DateTime> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (key, writeTime) in a)
            if (!b.TryGetValue(key, out var other) || other != writeTime) return false;
        return true;
    }
}
