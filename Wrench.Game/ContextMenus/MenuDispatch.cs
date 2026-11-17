using Wrench;

namespace Zomboid.ContextMenus;

/// <summary>
/// One mod's option handlers. Every option it adds or overrides gets the same Lua function as <c>onSelect</c> and a
/// handler id as its <c>target</c>, so no Lua function is created per menu; handlers last until that player's next menu.
/// </summary>
internal sealed class MenuDispatch(ModInstance mod)
{
    private sealed record Handler(int Player, Action<OptionClick> Invoke, object? OriginalSelect, object? OriginalTarget);

    private readonly Dictionary<long, Handler> _handlers = new();
    private GameObject? _function;
    private long _lastId;

    public ModInstance Mod => mod;

    public GameObject Function => _function ??= LuaHost.Required.CreateFunction(mod, (object?[] args) => Dispatch(args)).Keep();

    public void BeginMenu(int player)
    {
        foreach (var (id, handler) in _handlers.Where(h => h.Value.Player == player).ToList())
        {
            _handlers.Remove(id);
            (handler.OriginalSelect as GameObject)?.Dispose();
            (handler.OriginalTarget as GameObject)?.Dispose();
        }
    }

    public double Add(int player, Action<OptionClick> invoke, object? originalSelect = null, object? originalTarget = null)
    {
        var id = ++_lastId;
        _handlers[id] = new Handler(player, invoke, Keep(originalSelect), Keep(originalTarget));
        return id;
    }

    private object? Dispatch(object?[] args)
    {
        if (args is not [double id, ..] || !_handlers.TryGetValue((long)id, out var handler))
            return null;
        handler.Invoke(new OptionClick(mod, handler.Player, args[1..], handler.OriginalSelect, handler.OriginalTarget));
        return null;
    }

    private static object? Keep(object? value) => value is GameObject handle ? handle.Keep() : value;
}
