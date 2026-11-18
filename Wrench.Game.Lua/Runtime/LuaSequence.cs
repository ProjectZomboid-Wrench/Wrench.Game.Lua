using System.Collections;
using Wrench;

namespace Zomboid.Lua;

/// <summary>A Lua sequence (<c>t[1]</c>, <c>t[2]</c>, … up to the first nil), indexed from 0 like any C# list.</summary>
public sealed class LuaSequence<T> : IReadOnlyList<T>, IGameObjectView
{
    private readonly GameObject _table;

    internal LuaSequence(GameObject table) => _table = table;

    public LuaSequence() : this(LuaHost.Required.NewTable()) { }

    /// <summary>A new, empty Lua table.</summary>
    public static LuaSequence<T> New() => new(LuaHost.Required.NewTable());

    GameObject IGameObjectView.View => _table;

    public GameObject AsGameObject() => _table;

    public int Count
    {
        get
        {
            var count = 0;
            while (LuaHost.Required.Index(_table, (double)(count + 1)) is not null)
                count++;
            return count;
        }
    }

    public T this[int index]
    {
        get => LuaConvert.To<T>(LuaHost.Required.Index(_table, (double)(index + 1)));
        set => LuaHost.Required.SetIndex(LuaHost.Required.CurrentMod, _table, (double)(index + 1), value);
    }

    public void Add(T item) => LuaHost.Required.SetIndex(LuaHost.Required.CurrentMod, _table, (double)(Count + 1), item);

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 1; LuaHost.Required.Index(_table, (double)i) is { } value; i++)
            yield return LuaConvert.To<T>(value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A Lua table used as a map, e.g. <c>table&lt;string, number&gt;</c>.</summary>
public sealed class LuaMap<TKey, TValue> : IGameObjectView where TKey : notnull
{
    private readonly GameObject _table;

    internal LuaMap(GameObject table) => _table = table;

    /// <summary>A new, empty table.</summary>
    public LuaMap() : this(LuaHost.Required.NewTable()) { }

    /// <summary>A new, empty Lua table.</summary>
    public static LuaMap<TKey, TValue> New() => new(LuaHost.Required.NewTable());

    GameObject IGameObjectView.View => _table;

    public GameObject AsGameObject() => _table;

    /// <summary>The value at <paramref name="key"/>; default for nil. Setting null removes the key.</summary>
    public TValue this[TKey key]
    {
        get => LuaConvert.To<TValue>(LuaHost.Required.Index(_table, LuaKey(key)));
        set => LuaHost.Required.SetIndex(LuaHost.Required.CurrentMod, _table, LuaKey(key), value);
    }

    public bool ContainsKey(TKey key) => LuaHost.Required.Index(_table, LuaKey(key)) is not null;

    public bool TryGetValue(TKey key, out TValue value)
    {
        var raw = LuaHost.Required.Index(_table, LuaKey(key));
        value = LuaConvert.To<TValue>(raw);
        return raw is not null;
    }

    // Lua numbers are doubles: an int key 1 must be the same key as Lua's 1.
    private static object LuaKey(TKey key) => key is IConvertible and not string and not bool ? Convert.ToDouble(key) : key;
}
