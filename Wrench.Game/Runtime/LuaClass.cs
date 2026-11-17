using System.Reflection;
using System.Runtime.ExceptionServices;
using Wrench;

namespace Zomboid;

/// <summary>The Lua class behind one <see cref="LuaObject"/> subclass, rebuilt when PZ reloads its Lua.</summary>
internal sealed class LuaClass
{
    private const string IdField = "_wrenchObject";
    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Dictionary<Type, LuaClass> Classes = new();
    private static readonly Dictionary<long, LuaObject> Instances = new();
    private static long _lastId;

    private readonly string _baseName;
    private readonly GameObject _base;
    private readonly GameObject _derived;

    private LuaClass(Type type, string baseName, GameObject baseClass)
    {
        Mod = Host.CurrentMod ?? throw new InvalidOperationException(
            $"{type.Name} must be created by a mod, in an event handler or a function called from Lua");
        _baseName = baseName;
        _base = baseClass.Keep();
        var derived = LuaObject.First(Host.CallMethod(Mod, baseClass, "derive", [$"{Mod.Id}.{type.Name}"])) as GameObject
                      ?? throw new LuaException($"{baseName}:derive returned no table");
        _derived = derived.Keep();
        foreach (var (name, method) in DispatchedMethods(type))
            Host.SetIndex(Mod, _derived, name, Host.CreateFunction(Mod, Dispatcher(name, method)));
    }

    public ModInstance Mod { get; }

    public static LuaClass For(Type type)
    {
        var baseName = BaseClassName(type);
        var current = Host.GetGlobal(baseName) as GameObject
                      ?? throw new LuaException($"Lua class '{baseName}' isn't loaded");
        if (Classes.TryGetValue(type, out var cached))
        {
            if (cached._base == current)
                return cached;
            cached.Discard();
        }
        return Classes[type] = new LuaClass(type, baseName, current);
    }

    public GameObject Instantiate(LuaObject instance, object?[] args)
    {
        var create = Host.Index(_base, "new") ?? throw new LuaException($"{_baseName}.new is nil");
        var table = LuaObject.First(Host.Call(Mod, create, [_derived, .. args])) as GameObject
                    ?? throw new LuaException($"{_baseName}.new returned no table");
        var id = ++_lastId;
        Host.SetIndex(Mod, table, IdField, (double)id);
        instance.Id = id;
        Instances[id] = instance;
        return table.Keep();
    }

    public object? CallBase(GameObject self, string method, object?[] args) =>
        Host.Index(_base, method) is { } function ? LuaObject.First(Host.Call(Mod, function, [self, .. args])) : null;

    public static void Release(LuaObject instance)
    {
        Instances.Remove(instance.Id);
        instance.DropTable();
    }

    // After a Lua reload the old tables are unreachable from the new state, so their objects are let go.
    private void Discard()
    {
        foreach (var instance in Instances.Values.Where(i => i.Class == this).ToList())
            Release(instance);
        _base.Dispose();
        _derived.Dispose();
    }

    private Func<object?[], object?> Dispatcher(string name, MethodInfo method)
    {
        var parameters = method.GetParameters();
        return args =>
        {
            if (args is not [GameObject self, ..] || Find(self) is not { } target)
                return Host.Index(_base, name) is { } inherited ? LuaObject.First(Host.Call(Mod, inherited, args)) : null;

            var bound = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                bound[i] = JavaConvert.To(i + 1 < args.Length ? args[i + 1] : null, parameters[i].ParameterType);
            try
            {
                return method.Invoke(target, bound);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                ExceptionDispatchInfo.Throw(e.InnerException);
                throw;
            }
        };
    }

    private static LuaObject? Find(GameObject self) =>
        Host.Index(self, IdField) is double id && Instances.TryGetValue((long)id, out var instance) ? instance : null;

    private static string BaseClassName(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.GetCustomAttribute<LuaClassAttribute>() is { } attribute)
                return attribute.BaseClass;
        }
        throw new InvalidOperationException($"{type.Name} has no [LuaClass] naming the Lua class it extends");
    }

    private static Dictionary<string, MethodInfo> DispatchedMethods(Type type)
    {
        var methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        for (var t = type; t is not null && t != typeof(LuaObject); t = t.BaseType)
        {
            foreach (var method in t.GetMethods(Declared))
            {
                if (method.GetCustomAttribute<LuaMethodAttribute>() is not { } attribute || methods.ContainsKey(attribute.Name))
                    continue;
                if (!method.IsVirtual || method.IsAbstract || IsOverridden(type, method))
                    methods[attribute.Name] = method;
            }
        }
        return methods;
    }

    private static bool IsOverridden(Type type, MethodInfo method)
    {
        for (var t = type; t is not null && t != method.DeclaringType; t = t.BaseType)
        {
            if (t.GetMethods(Declared).Any(m => m.GetBaseDefinition() == method))
                return true;
        }
        return false;
    }

    private static ILuaHost Host => LuaHost.Required;
}
