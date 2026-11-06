namespace GridPulse.Web.Services;

/// <summary>
/// Fixed-capacity buffer that keeps the newest items. Live charts append once per second for as
/// long as the page is open; this keeps their memory constant.
/// </summary>
public sealed class RingBuffer<T>(int capacity) : IReadOnlyCollection<T>
{
    private readonly T[] _items = capacity > 0 ? new T[capacity] : throw new ArgumentOutOfRangeException(nameof(capacity));
    private int _start;

    public int Count { get; private set; }

    public int Capacity => _items.Length;

    public void Add(T item)
    {
        var end = (_start + Count) % _items.Length;
        _items[end] = item;
        if (Count == _items.Length)
        {
            _start = (_start + 1) % _items.Length;
        }
        else
        {
            Count++;
        }
    }

    public void Clear()
    {
        Array.Clear(_items);
        _start = 0;
        Count = 0;
    }

    /// <summary>Items from oldest to newest.</summary>
    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return _items[(_start + i) % _items.Length];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
