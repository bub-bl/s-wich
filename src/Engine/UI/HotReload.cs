namespace Crowbar.Engine.UI;

public sealed partial class UiSystem
{
    private FileSystemWatcher? _razorWatcher;
    private FileSystemWatcher? _styleWatcher;
    private string? _razorPath;
    private string? _stylePath;
    private string _razorClassName = "Root";
    private volatile bool _reloadRequested;

    public void WatchFiles(string razorPath, string? stylePath = null, string className = "Root")
    {
        StopWatching();
        _razorPath = Path.GetFullPath(razorPath); _stylePath = stylePath is null ? null : Path.GetFullPath(stylePath); _razorClassName = className;
        _razorWatcher = CreateWatcher(_razorPath);
        if (_stylePath is not null) _styleWatcher = CreateWatcher(_stylePath);
    }

    public void Update(float deltaTime = 1f / 60f)
    {
        AdvanceAnimations(Screen, deltaTime);
        AdvanceCarets(Screen, deltaTime);
        if (!_reloadRequested) return;
        _reloadRequested = false;
        if (_razorPath is not null && File.Exists(_razorPath)) LoadRazor(File.ReadAllText(_razorPath), _razorClassName);
        if (_stylePath is not null && File.Exists(_stylePath)) LoadStyles(File.ReadAllText(_stylePath));
    }

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

    private FileSystemWatcher CreateWatcher(string path)
    {
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true };
        FileSystemEventHandler handler = (_, _) => _reloadRequested = true;
        RenamedEventHandler renamed = (_, _) => _reloadRequested = true;
        watcher.Changed += handler; watcher.Created += handler; watcher.Renamed += renamed; watcher.Error += (_, _) => _reloadRequested = true;
        return watcher;
    }

    public void StopWatching() { _razorWatcher?.Dispose(); _styleWatcher?.Dispose(); _razorWatcher = null; _styleWatcher = null; }
}
