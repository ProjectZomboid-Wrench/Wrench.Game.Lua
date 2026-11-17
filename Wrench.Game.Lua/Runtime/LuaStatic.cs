using Wrench;

namespace Zomboid.Lua;

/// <summary>Class-level access for the generated wrappers: <c>Class:new(...)</c>, <c>Class.field</c>, <c>Class.function(...)</c>.</summary>
internal static class LuaStatic
{
    public static T New<T>(string luaClass, params object?[] args) where T : LuaObject =>
        LuaConvert.To<T>(First(LuaHost.Required.CallMethod(LuaHost.Required.CurrentMod, Table(luaClass), "new", args)));

    public static T Get<T>(string luaClass, string field) => LuaConvert.To<T>(LuaHost.Required.Index(Table(luaClass), field));

    public static void Set(string luaClass, string field, object? value) =>
        LuaHost.Required.SetIndex(LuaHost.Required.CurrentMod, Table(luaClass), field, value);

    public static T Call<T>(string luaClass, string function, params object?[] args) =>
        LuaConvert.To<T>(First(LuaHost.Required.Call(LuaHost.Required.CurrentMod, Function(luaClass, function), args)));

    public static void CallVoid(string luaClass, string function, params object?[] args) =>
        LuaHost.Required.Call(LuaHost.Required.CurrentMod, Function(luaClass, function), args);

    public static T CallMethod<T>(string luaClass, string method, params object?[] args) =>
        LuaConvert.To<T>(First(LuaHost.Required.CallMethod(LuaHost.Required.CurrentMod, Table(luaClass), method, args)));

    public static void CallMethodVoid(string luaClass, string method, params object?[] args) =>
        LuaHost.Required.CallMethod(LuaHost.Required.CurrentMod, Table(luaClass), method, args);

    public static T CallGlobal<T>(string function, params object?[] args) =>
        LuaConvert.To<T>(First(LuaHost.Required.Call(LuaHost.Required.CurrentMod, Global(function), args)));

    public static void CallGlobalVoid(string function, params object?[] args) =>
        LuaHost.Required.Call(LuaHost.Required.CurrentMod, Global(function), args);

    private static GameObject Table(string luaClass) =>
        LuaHost.Required.GetGlobal(luaClass) as GameObject ?? throw new LuaException($"Lua class '{luaClass}' isn't loaded");

    private static object Function(string luaClass, string function) =>
        LuaHost.Required.Index(Table(luaClass), function) ?? throw new LuaException($"{luaClass}.{function} is nil");

    private static object Global(string function) =>
        LuaHost.Required.GetGlobal(function) ?? throw new LuaException($"global '{function}' is nil");

    private static object? First(object?[] results) => results.Length > 0 ? results[0] : null;
}
