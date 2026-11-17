using Wrench;
using Zomboid.Java;

namespace Zomboid.Lua;

/// <summary>
/// Marks a C# class as the Lua class <see cref="Name"/>, one built with <c>derive</c> such as <c>ISBaseTimedAction</c>.
/// A C# subclass of it becomes a new Lua class derived from that one.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LuaClassAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Makes a method the Lua method <see cref="Name"/>. A virtual one only goes into a C# subclass's Lua class where the
/// subclass overrides it, so Lua's own runs otherwise; a non-virtual one always does.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LuaMethodAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Wraps an existing Lua table as a <see cref="LuaObject"/>.</summary>
public readonly struct LuaWrap(GameObject table)
{
    public GameObject Table { get; } = table;
}

/// <summary>The arguments for a Lua class's <c>new</c>, when constructing a C# subclass instance.</summary>
public readonly struct LuaNew(params object?[] args)
{
    public object?[] Args { get; } = args;
}

/// <summary>
/// A Lua table with a typed view: either an existing one (vanilla's objects, wrapped like Java objects and valid for
/// the current call) or an instance of a C# subclass, which Lua and Java see as a new Lua class derived from the
/// nearest <see cref="LuaClassAttribute"/> whose <see cref="LuaMethodAttribute"/> methods run the C# overrides.
/// </summary>
/// <remarks>
/// Game objects are only valid for the current call, so a subclass keeps state that must outlive it in table fields
/// (<see cref="Get{T}"/>, <see cref="Set"/>), the way Lua classes keep theirs on <c>self</c>.
/// </remarks>
public abstract class LuaObject : IGameObjectView, IEquatable<LuaObject>
{
    private GameObject? _table;

    protected LuaObject(LuaWrap wrap) => _table = wrap.Table ?? throw new ArgumentNullException(nameof(wrap));

    /// <summary>Creates a C# subclass instance: the Lua base class's <c>new</c> runs with <see cref="LuaNew.Args"/>.</summary>
    protected LuaObject(LuaNew construct)
    {
        Class = LuaClass.For(GetType());
        _table = Class.Instantiate(this, construct.Args ?? []);
    }

    /// <summary>The object's Lua table.</summary>
    public GameObject Table => _table ?? throw new ObjectDisposedException(GetType().Name,
        "This Lua object was released; Lua calls on its table now go to the Lua base class.");

    /// <summary>True once <see cref="Release"/> has run.</summary>
    public bool IsReleased => _table is null;

    /// <summary>True for instances of a C# subclass, false for wrapped Lua tables.</summary>
    public bool IsSubclassInstance => Class is not null;

    GameObject IGameObjectView.View => Table;

    internal LuaClass? Class { get; }

    internal long Id { get; set; }

    private ModInstance? Mod => Class?.Mod ?? LuaHost.Required.CurrentMod;

    /// <summary>The table field <c>self[field]</c>, converted like the typed API's return values.</summary>
    protected T Get<T>(string field) => LuaConvert.To<T>(LuaHost.Required.Index(Table, field));

    protected void Set(string field, object? value) => LuaHost.Required.SetIndex(Mod, Table, field, value);

    /// <summary><c>self:method(args)</c>, resolved through the Lua class like any Lua call on the object.</summary>
    protected dynamic CallLua(string method, params object?[] args) =>
        First(LuaHost.Required.CallMethod(Mod, Table, method, args))!;

    /// <summary>The nearest Lua base class's own method, like Lua's <c>Base.method(self, args)</c>; null if it has none.</summary>
    protected dynamic CallBase(string method, params object?[] args) =>
        Class is { } luaClass ? luaClass.CallBase(Table, method, args)! : CallLua(method, args);

    /// <summary>
    /// Runs Lua class <paramref name="luaClass"/>'s <paramref name="method"/>: for a wrapped table as <c>self:method()</c>
    /// (so Lua subclasses' overrides apply), for a C# subclass instance as <c>luaClass.method(self)</c> (so the C#
    /// override calling its base doesn't call itself).
    /// </summary>
    protected T Invoke<T>(string luaClass, string method, params object?[] args) =>
        LuaConvert.To<T>(InvokeRaw(luaClass, method, args));

    protected void InvokeVoid(string luaClass, string method, params object?[] args) => InvokeRaw(luaClass, method, args);

    /// <summary>Stops routing Lua calls to this object and releases its table; later calls go to the Lua base class.</summary>
    protected void Release()
    {
        if (Class is not null)
            LuaClass.Release(this);
    }

    public bool Equals(LuaObject? other) => other is not null && (ReferenceEquals(this, other) || Table == other.Table);

    public override bool Equals(object? obj) => obj is LuaObject other && Equals(other);

    public override int GetHashCode() => Table.GetHashCode();

    public override string ToString() => _table?.ToString() ?? $"<released {GetType().Name}>";

    public static bool operator ==(LuaObject? a, LuaObject? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(LuaObject? a, LuaObject? b) => !(a == b);

    internal void DropTable()
    {
        _table?.Dispose();
        _table = null;
    }

    internal static object? First(object?[] results) => results.Length > 0 ? results[0] : null;

    private protected static bool IsTruthy(object? value) => value is not (null or false);

    private object? InvokeRaw(string luaClass, string method, object?[] args)
    {
        if (Class is null)
            return First(LuaHost.Required.CallMethod(Mod, Table, method, args));
        var owner = LuaHost.Required.GetGlobal(luaClass) as GameObject ?? throw new LuaException($"Lua class '{luaClass}' isn't loaded");
        return LuaHost.Required.Index(owner, method) is { } function
            ? First(LuaHost.Required.Call(Mod, function, [Table, .. args]))
            : null;
    }
}
