using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Wrench;
using Zomboid.Java;

namespace Zomboid.Lua;

/// <summary>Converts what Lua hands back to the declared type: Lua wrappers, sequences, maps and unions, then Java's rules.</summary>
internal static class LuaConvert
{
    private static readonly ConcurrentDictionary<Type, Func<GameObject, object>> Factories = new();

    public static T To<T>(object? value)
    {
        if (value is T typed)
            return typed;
        if (value is double number && JavaConvert.TryNumber(number, out T converted))
            return converted;
        return (T)To(value, typeof(T))!;
    }

    public static object? To(object? value, Type type)
    {
        if (value is null)
            return type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(object) || target.IsInstanceOfType(value) && !typeof(IUnion).IsAssignableFrom(target))
            return value;
        if (typeof(IUnion).IsAssignableFrom(target))
            return ToUnion(value, target);
        if (value is GameObject handle)
        {
            if (typeof(LuaObject).IsAssignableFrom(target))
                return LuaTypes.Create(target, handle);
            if (target.IsGenericType && target.GetGenericTypeDefinition() is var definition
                && (definition == typeof(LuaSequence<>) || definition == typeof(LuaMap<,>)))
                return Factories.GetOrAdd(target, CreateFactory)(handle);
        }
        return JavaConvert.To(value, target);
    }

    // The first case the value converts to: exact type matches first, then Lua/Java wrappers, then numbers and strings.
    private static object ToUnion(object value, Type union)
    {
        var cases = union.GetConstructors().Select(c => c.GetParameters()).Where(p => p.Length == 1).Select(p => p[0].ParameterType).ToList();
        var chosen = cases.FirstOrDefault(c => c.IsInstanceOfType(value))
                     ?? (value is GameObject handle ? cases.FirstOrDefault(c => Fits(handle, c)) : null)
                     ?? cases.FirstOrDefault(c => value is IConvertible && (Nullable.GetUnderlyingType(c) ?? c).IsPrimitive && value is not string and not bool)
                     ?? cases.FirstOrDefault(c => c == typeof(string))
                     ?? throw new InvalidCastException($"{value.GetType().Name} from Lua fits none of {union.Name}'s cases");
        return union.GetConstructor([chosen])!.Invoke([To(value, chosen)]);
    }

    private static bool Fits(GameObject handle, Type type)
    {
        if (handle.IsTable)
            return typeof(LuaObject).IsAssignableFrom(type) ? LuaTypes.IsInstance(handle, type) : type.IsGenericType;
        return typeof(JavaObject).IsAssignableFrom(type) && type.IsInstanceOfType(JavaTypes.CreateExact(handle));
    }

    private static Func<GameObject, object> CreateFactory(Type type)
    {
        var constructor = type.GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
        return handle => constructor.Invoke([handle]);
    }
}
