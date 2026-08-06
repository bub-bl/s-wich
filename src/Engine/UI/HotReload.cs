namespace Crowbar.Engine.UI;

public sealed partial class UiSystem
{
    private string? _razorPath;
    private string? _stylePath;
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
            if (_razorPath is not null && File.Exists(_razorPath))
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

    public void StopWatching() { }
}
