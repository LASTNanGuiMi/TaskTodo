namespace TaskTodo.Core.Interaction;

public static class EdgeInteractionTiming
{
    public static readonly TimeSpan Hover = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Leave = TimeSpan.FromMilliseconds(350);
    public static readonly TimeSpan Animation = TimeSpan.FromMilliseconds(160);
}

// One clock and replaceable deadlines: a previous capsule or leave never owns a callback.
public sealed class EdgeHoverController
{
    private Guid? _candidate;
    private DateTimeOffset? _openAt;
    private DateTimeOffset? _closeAt;
    private bool _inside;
    private bool _busy;

    public bool Enabled { get; private set; }
    public Guid? VisibleListId { get; private set; }

    public void Start()
    {
        Reset();
        Enabled = true;
    }

    public void Reset()
    {
        Enabled = false;
        VisibleListId = _candidate = null;
        _openAt = _closeAt = null;
        _inside = _busy = false;
    }

    public void EnterCapsule(Guid listId, DateTimeOffset now)
    {
        if (!Enabled || _busy) return;
        _inside = true;
        _closeAt = null;
        if (_candidate == listId) return;
        _candidate = listId;
        _openAt = VisibleListId == listId ? null : now + EdgeInteractionTiming.Hover;
    }

    public void ObserveRegion(bool inside, bool busy, DateTimeOffset now)
    {
        _inside = inside;
        _busy = busy;
        if (inside || busy)
            _closeAt = null;
        else
        {
            _candidate = null;
            _openAt = null;
            if (VisibleListId is not null) _closeAt ??= now + EdgeInteractionTiming.Leave;
        }
    }

    public void Tick(DateTimeOffset now)
    {
        if (!Enabled || _busy) return;
        if (_inside && _openAt <= now)
        {
            VisibleListId = _candidate;
            _openAt = null;
        }
        if (!_inside && _closeAt <= now)
        {
            VisibleListId = null;
            _closeAt = null;
        }
    }
}
