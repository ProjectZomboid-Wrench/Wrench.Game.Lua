using Wrench;

namespace Zomboid;

/// <summary>Names the Lua class, one built with <c>derive</c> such as <c>ISBaseTimedAction</c>, that a <see cref="LuaObject"/> extends.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LuaClassAttribute(string baseClass) : Attribute
{
    public string BaseClass { get; } = baseClass;
}

/// <summary>
/// Makes a method the Lua method <see cref="Name"/>. A virtual one only goes into the Lua class where a subclass
/// overrides it, so Lua's own runs otherwise; a non-virtual one always does.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LuaMethodAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// A C# object that Lua and Java see as an instance of a Lua class derived from <see cref="LuaClassAttribute"/>'s:
/// a table whose <see cref="LuaMethodAttribute"/> methods run the C# overrides. Must be created by a mod's code.
/// </summary>
/// <remarks>
/// Game objects are only valid for the current call, so keep state that must outlive it in table fields
/// (<see cref="Get{T}"/>, <see cref="Set"/>), the way Lua classes keep theirs on <c>self</c>.
/// </remarks>
public abstract class LuaObject
{
    private GameObject? _table;

    /// <param name="args">The arguments for the Lua base class's <c>new</c>, after the class itself.</param>
    protected LuaObject(params object?[] args)
    {
        Class = LuaClass.For(GetType());
        _table = Class.Instantiate(this, args);
    }

    /// <summary>The object's Lua table.</summary>
    public GameObject Table => _table ?? throw new ObjectDisposedException(GetType().Name,
        "This Lua object was released; Lua calls on its table now go to the Lua base class.");

    /// <summary>True once <see cref="Release"/> has run.</summary>
    public bool IsReleased => _table is null;

    internal LuaClass Class { get; }

    internal long Id { get; set; }

    /// <summary>The table field <c>self[field]</c>, converted like the typed API's return values.</summary>
    protected T Get<T>(string field) => JavaConvert.To<T>(LuaHost.Required.Index(Table, field));

    protected void Set(string field, object? value) => LuaHost.Required.SetIndex(Class.Mod, Table, field, value);

    /// <summary><c>self:method(args)</c>, resolved through the Lua class like any Lua call on the object.</summary>
    protected dynamic CallLua(string method, params object?[] args) =>
        First(LuaHost.Required.CallMethod(Class.Mod, Table, method, args))!;

    /// <summary>The Lua base class's own method, like Lua's <c>Base.method(self, args)</c>; null if it has none.</summary>
    protected dynamic CallBase(string method, params object?[] args) => Class.CallBase(Table, method, args)!;

    /// <summary>Stops routing Lua calls to this object and releases its table; later calls go to the Lua base class.</summary>
    protected void Release() => LuaClass.Release(this);

    internal void DropTable()
    {
        _table?.Dispose();
        _table = null;
    }

    internal static object? First(object?[] results) => results.Length > 0 ? results[0] : null;

    private protected static bool IsTruthy(object? value) => value is not (null or false);
}
