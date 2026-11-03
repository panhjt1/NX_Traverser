using System.Collections.Generic;

// NX-independent ownership policy. A pin protects an active traversal ancestor;
// it is reference counted because occurrences can share the same prototype.
internal sealed class PartLifetime<TKey>
{
    private readonly HashSet<TKey> _initial = new HashSet<TKey>();
    private readonly HashSet<TKey> _protected = new HashSet<TKey>();
    private readonly HashSet<TKey> _touched = new HashSet<TKey>();
    private readonly Dictionary<TKey, int> _pins = new Dictionary<TKey, int>();

    public void RegisterInitial(TKey key, bool keepOpen)
    {
        _initial.Add(key);
        if (keepOpen) Protect(key);
    }

    public void Protect(TKey key) { _protected.Add(key); }
    public void Touch(TKey key) { _touched.Add(key); }
    public bool WasInitial(TKey key) { return _initial.Contains(key); }
    public bool WasTouched(TKey key) { return _touched.Contains(key); }
    public bool MustRemainLoaded(TKey key) { return _protected.Contains(key) || _pins.ContainsKey(key); }

    public void Pin(TKey key)
    {
        int count;
        _pins.TryGetValue(key, out count);
        _pins[key] = count + 1;
    }

    public void Unpin(TKey key)
    {
        int count;
        if (!_pins.TryGetValue(key, out count)) return;
        if (count == 1) _pins.Remove(key);
        else _pins[key] = count - 1;
    }

    public bool CanRelease(TKey key)
    {
        return !MustRemainLoaded(key) &&
            (!_initial.Contains(key) || _touched.Contains(key));
    }

    public void Released(TKey key) { _touched.Remove(key); }
}
