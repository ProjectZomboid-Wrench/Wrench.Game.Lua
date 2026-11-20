using Wrench;

namespace Zomboid.Lua;

/// <summary>Class-level access for the generated wrappers: <c>Class:new(...)</c>, <c>Class.field</c>, <c>Class.function(...)</c>.</summary>
internal static class LuaStatic
{
    public static T New<T>(string luaClass, params object?[] args) where T : LuaObject =>
        LuaConvert.To<T>(First(CallStaticMethod(luaClass, "new", args)));

    public static GameObject Construct(string luaClass, object?[] args) =>
        First(CallStaticMethod(luaClass, "new", args)) as GameObject ?? throw new LuaException($"{luaClass}:new returned no table");

    public static T Get<T>(string luaClass, string field)
    {
        try
        {
            return LuaConvert.To<T>(LuaHost.Required.GetStatic(luaClass, field));
        }
        catch (LuaException e) when (IsMissingClass(e, luaClass))
        {
            throw MissingClass(luaClass);
        }
    }

    public static void Set(string luaClass, string field, object? value) =>
        LuaHost.Required.SetIndex(LuaHost.Required.CurrentMod, Table(luaClass), field, value);

    public static T Call<T>(string luaClass, string function, params object?[] args) =>
        LuaConvert.To<T>(First(CallStatic(luaClass, function, args)));

    public static void CallVoid(string luaClass, string function, params object?[] args) => CallStatic(luaClass, function, args);

    public static T CallMethod<T>(string luaClass, string method, params object?[] args) =>
        LuaConvert.To<T>(First(CallStaticMethod(luaClass, method, args)));

    public static void CallMethodVoid(string luaClass, string method, params object?[] args) =>
        CallStaticMethod(luaClass, method, args);

    public static T GetGlobal<T>(string name, bool nullable) =>
        LuaConvert.To<T>(nullable ? LuaHost.Required.GetGlobal(name) : Global(name));

    public static void SetGlobal(string name, object? value) => LuaHost.Required.SetGlobal(
        LuaHost.Required.CurrentMod ?? throw new InvalidOperationException("Lua globals can only be set from mod code"), name, value);

    public static T CallGlobal<T>(string function, params object?[] args) =>
        LuaConvert.To<T>(First(LuaHost.Required.CallGlobal(LuaHost.Required.CurrentMod, function, args)));

    public static void CallGlobalVoid(string function, params object?[] args) =>
        LuaHost.Required.CallGlobal(LuaHost.Required.CurrentMod, function, args);

    /// <summary><c>luaClass.function(args...)</c>; with <paramref name="optional"/>, a missing function returns no results.</summary>
    internal static object?[] CallStatic(string luaClass, string function, object?[] args, ModInstance? mod = null,
        bool optional = false)
    {
        try
        {
            return LuaHost.Required.CallStatic(mod ?? LuaHost.Required.CurrentMod, luaClass, function, args, optional);
        }
        catch (LuaException e) when (IsMissingClass(e, luaClass))
        {
            throw MissingClass(luaClass);
        }
    }

    private static object?[] CallStaticMethod(string luaClass, string method, object?[] args)
    {
        try
        {
            return LuaHost.Required.CallStaticMethod(LuaHost.Required.CurrentMod, luaClass, method, args);
        }
        catch (LuaException e) when (IsMissingClass(e, luaClass))
        {
            throw MissingClass(luaClass);
        }
    }

    // The host words a missing global table as Wrench.Game.Java does; Lua classes say it in their own terms.
    private static bool IsMissingClass(LuaException e, string luaClass) => e.Message == $"global '{luaClass}' is not exposed";

    private static LuaException MissingClass(string luaClass) => new($"Lua class '{luaClass}' isn't loaded");

    private static GameObject Table(string luaClass) =>
        LuaHost.Required.GetGlobal(luaClass) as GameObject ?? throw MissingClass(luaClass);

    private static object Global(string name) =>
        LuaHost.Required.GetGlobal(name) ?? throw new LuaException($"global '{name}' is nil");

    private static object? First(object?[] results) => results.Length > 0 ? results[0] : null;
}
