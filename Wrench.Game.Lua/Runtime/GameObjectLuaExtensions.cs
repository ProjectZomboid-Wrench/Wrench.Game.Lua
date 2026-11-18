using Wrench;

namespace Zomboid.Lua;

public static class GameObjectLuaExtensions
{
    /// <summary>
    /// Views a Lua table as a generated Lua class, the Lua counterpart of <c>As&lt;T&gt;()</c>: as its own class when its
    /// <c>Type</c> field names one that fits, else as <typeparamref name="T"/>. Same lifetime as the table.
    /// </summary>
    /// <exception cref="InvalidCastException">The value isn't a table, or its <c>Type</c> names a class that isn't a <typeparamref name="T"/>.</exception>
    public static T AsLua<T>(this GameObject table) where T : LuaObject
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!table.IsTable)
            throw new InvalidCastException($"{table.TypeName} is a Java object, not a Lua table; use As<T>() for Java wrappers");
        var wrapper = LuaTypes.Create<T>(table);
        if (LuaHost.Required.Index(table, "Type") is string type && !LuaTypes.IsInstance(table, typeof(T)) && LuaTypes.IsKnown(type))
            throw new InvalidCastException($"The table is a {type}, not a {typeof(T).Name}");
        return wrapper;
    }

    /// <summary>Views a Lua table as a 1-based sequence (0-based in C#).</summary>
    public static LuaSequence<T> AsSequence<T>(this GameObject table) => LuaConvert.To<LuaSequence<T>>(table);

    /// <summary>Views a Lua table as a map.</summary>
    public static LuaMap<TKey, TValue> AsMap<TKey, TValue>(this GameObject table) where TKey : notnull =>
        LuaConvert.To<LuaMap<TKey, TValue>>(table);
}
