namespace Hmi.Services.Shell;

/// <summary>
/// Scoped state shared between the dispatcher shell and the workspace pages: the element a page has selected
/// (<see cref="Selected"/>), focus requests for diagrams (<see cref="RequestFocus"/>), the alarm selected in the alarm
/// bar, the last realtime update and the circuit connection state. Components re-render on <see cref="Changed"/>.
/// </summary>
public sealed class ShellState(TimeProvider time)
{
    private string? _selected;
    private string? _selectedAlarmKey;
    private string _connection = "Connected";
    private DateTimeOffset _lastUpdateAt = time.GetUtcNow();

    /// <summary>Raised whenever any value changes; shell components subscribe and re-render.</summary>
    public event Action? Changed;

    /// <summary>Raised when the quick switcher asks a diagram to zoom to an element id.</summary>
    public event Action<string>? FocusRequested;

    /// <summary>Raised when the status bar help button asks for the shortcut overlay.</summary>
    public event Action? HelpRequested;

    /// <summary>The element the current page has selected (shown in the status bar).</summary>
    public string? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The alarm key the alarm bar has selected (acknowledged with Ctrl+Shift+A).</summary>
    public string? SelectedAlarmKey
    {
        get => _selectedAlarmKey;
        set
        {
            _selectedAlarmKey = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Connection state of the Blazor circuit, reported by shell.js (defaults to connected without JS).</summary>
    public string Connection
    {
        get => _connection;
        set
        {
            _connection = value;
            Changed?.Invoke();
        }
    }

    /// <summary>When the shell last received a realtime update (drives the status bar age).</summary>
    public DateTimeOffset LastUpdateAt => _lastUpdateAt;

    /// <summary>Records a realtime update as happening now.</summary>
    public bool Touch()
    {
        _lastUpdateAt = time.GetUtcNow();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Asks the diagram workspace to zoom to (and select) an element id.</summary>
    public bool RequestFocus(string elementId)
    {
        FocusRequested?.Invoke(elementId);
        return true;
    }

    /// <summary>Asks the shell to open the keyboard shortcut overlay.</summary>
    public bool RequestHelp()
    {
        HelpRequested?.Invoke();
        return true;
    }
}
