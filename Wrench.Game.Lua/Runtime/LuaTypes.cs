using Wrench;

namespace Zomboid.Lua;

/// <summary>Picks the wrapper for a Lua table: the class its <c>Type</c> field names if known and compatible, else the type asked for.</summary>
internal static partial class LuaTypes
{
    private static readonly Dictionary<string, Func<LuaWrap, LuaObject>> ByLuaName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, Func<LuaWrap, LuaObject>> ByType = new();
    private static readonly Dictionary<Type, string> LuaNames = new();

    static LuaTypes() => Register(ByLuaName, ByType, LuaNames);

    static partial void Register(Dictionary<string, Func<LuaWrap, LuaObject>> byLuaName,
                                 Dictionary<Type, Func<LuaWrap, LuaObject>> byType, Dictionary<Type, string> luaNames);

    public static T Create<T>(GameObject table) where T : LuaObject => (T)Create(typeof(T), table);

    public static LuaObject Create(Type type, GameObject table)
    {
        if (LuaHost.Required.Index(table, "Type") is string name && ByLuaName.TryGetValue(name, out var exact))
        {
            var wrapper = exact(new LuaWrap(table));
            if (type.IsInstanceOfType(wrapper))
                return wrapper;
        }
        return ByType.TryGetValue(type, out var declared)
            ? declared(new LuaWrap(table))
            : throw new InvalidCastException($"No Lua wrapper for {type.Name}");
    }

    public static bool IsKnown(string luaName) => ByLuaName.ContainsKey(luaName);

    public static string LuaName(Type type) =>
        LuaNames.TryGetValue(type, out var name) ? name : throw new InvalidOperationException($"{type.Name} isn't a generated Lua class");

    /// <summary>Whether the table's class is <paramref name="type"/>'s Lua class or derives from it.</summary>
    public static bool IsInstance(GameObject table, Type type) =>
        LuaHost.Required.Index(table, "Type") is string name && ByLuaName.TryGetValue(name, out var exact)
            ? type.IsInstanceOfType(exact(new LuaWrap(table)))
            : false;
}
