using System.Reflection;
using System.Runtime.ExceptionServices;
using Wrench;
using Zomboid.Characters;

namespace Zomboid.ContextMenus;

/// <summary>Finds a mod's commands, fillers and overrides when it loads and runs them whenever PZ fills a context menu.</summary>
internal sealed class ContextMenuRegistry
{
    private const BindingFlags AnyMethod =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly ModInstance _owner;
    private readonly MenuDispatch _dispatch;
    private readonly List<IInventoryCommand> _commands = [];
    private readonly List<InventoryMenuFiller> _inventoryFillers = [];
    private readonly List<WorldMenuFiller> _worldFillers = [];
    private readonly List<(MenuKind Menu, string Text, Action<OptionClick> Handler)> _overrides = [];

    private ContextMenuRegistry(ModInstance owner)
    {
        _owner = owner;
        _dispatch = new MenuDispatch(owner);
    }

    /// <summary>Called by the loader once an assembly's mods have started; the first one owns the assembly's menu code.</summary>
    public static void Discover(Assembly assembly, IReadOnlyList<ModInstance> mods)
    {
        if (mods.Count == 0)
            return;
        var registry = new ContextMenuRegistry(mods[0]);
        foreach (var mod in mods)
            registry.AddMethods(mod.GetType(), mod);
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            if (IsCommandType(type))
                registry._commands.Add((IInventoryCommand)Activator.CreateInstance(type, nonPublic: true)!);
            else if (type.IsSubclassOf(typeof(InventoryMenuFiller)))
                registry._inventoryFillers.Add((InventoryMenuFiller)Activator.CreateInstance(type, nonPublic: true)!);
            else if (type.IsSubclassOf(typeof(WorldMenuFiller)))
                registry._worldFillers.Add((WorldMenuFiller)Activator.CreateInstance(type, nonPublic: true)!);
            else if (!type.IsSubclassOf(typeof(ModInstance)))
                registry.AddMethods(type, null);
        }

        var host = registry._owner.Host;
        if (registry._commands.Count + registry._inventoryFillers.Count > 0 || registry._overrides.Any(o => o.Menu == MenuKind.Inventory))
            host.RegisterLuaEvent(registry._owner, LuaEvents.OnFillInventoryObjectContextMenu, registry.FillInventory);
        if (registry._worldFillers.Count > 0 || registry._overrides.Any(o => o.Menu == MenuKind.World))
            host.RegisterLuaEvent(registry._owner, LuaEvents.OnFillWorldObjectContextMenu, registry.FillWorld);
    }

    private static bool IsCommandType(Type type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(InventoryCommand<>))
                return true;
        }
        return false;
    }

    // Instance methods only on mod classes, where there's an instance to call them on; static ones anywhere.
    private void AddMethods(Type type, ModInstance? instance)
    {
        foreach (var method in type.GetMethods(AnyMethod))
        {
            if (!method.IsStatic && instance is null)
                continue;
            var target = method.IsStatic ? null : instance;
            if (method.GetCustomAttribute<InventoryCommandAttribute>() is { } command)
                _commands.Add(new MethodCommand(method, target, command));
            foreach (var option in method.GetCustomAttributes<OverrideOptionAttribute>())
            {
                if (method.GetParameters() is not [{ ParameterType: var p }] || p != typeof(OptionClick))
                    throw new InvalidOperationException($"[OverrideOption] {type.Name}.{method.Name} must take one OptionClick");
                _overrides.Add((option.Menu, option.Text, click => Invoke(method, target, [click])));
            }
        }
    }

    private void FillInventory(LuaEventArgs args)
    {
        var player = args.Get<int>(0);
        _dispatch.BeginMenu(player);
        var menu = new InventoryMenu((GameObject)args[1]!, _dispatch, player, InventoryMenu.Representatives((GameObject)args[2]!));

        IsoPlayer? character = null;
        foreach (var command in _commands)
        {
            Guard(command.GetType().Name, () =>
            {
                character ??= menu.Player;
                var (item, state) = menu.ItemObjects.Where(command.Accepts)
                    .Select(i => (Item: i, State: command.CanExecute(i, character)))
                    .FirstOrDefault(c => c.State.IsVisible);
                if (item is null)
                    return;
                var option = menu.AddOption(Globals.GetText(command.Text), click => command.Execute((GameObject)click.Args[0]!, click.Player), item);
                if (!state.IsEnabled)
                {
                    option.Enabled = false;
                    if (state.Reason is not null)
                        option.Tooltip = Globals.GetText(state.Reason);
                }
            });
        }
        foreach (var filler in _inventoryFillers)
            Guard(filler.GetType().Name, () => filler.Fill(menu));
        ApplyOverrides(MenuKind.Inventory, menu);
    }

    private void FillWorld(LuaEventArgs args)
    {
        // The "test" pass only asks whether there would be options; vanilla's handlers add none during it.
        if (args.Count > 3 && args[3] is true)
            return;
        var player = args.Get<int>(0);
        _dispatch.BeginMenu(player);
        var menu = new WorldMenu((GameObject)args[1]!, _dispatch, player, (GameObject)args[2]!);
        foreach (var filler in _worldFillers)
            Guard(filler.GetType().Name, () => filler.Fill(menu));
        ApplyOverrides(MenuKind.World, menu);
    }

    private void ApplyOverrides(MenuKind kind, ContextMenu menu)
    {
        foreach (var (_, text, handler) in _overrides.Where(o => o.Menu == kind))
            Guard($"[OverrideOption(\"{text}\")]", () => menu.Find(Globals.GetText(text))?.Override(handler));
    }

    // One failing command or filler mustn't take the rest of the mod's menu with it.
    private void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _owner.Host.Log(_owner, LogLevel.Error, $"Context menu {what} failed", e is TargetInvocationException { InnerException: { } inner } ? inner : e);
        }
    }

    internal static object? Invoke(MethodInfo method, object? target, object?[] args)
    {
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(e.InnerException);
            throw;
        }
    }

    /// <summary>An <see cref="InventoryCommandAttribute"/> method, with its optional CanExecute method.</summary>
    private sealed class MethodCommand : IInventoryCommand
    {
        private readonly MethodInfo _execute;
        private readonly MethodInfo? _canExecute;
        private readonly object? _target;
        private readonly Type _itemType;
        private readonly string? _itemFilter;

        public MethodCommand(MethodInfo execute, object? target, InventoryCommandAttribute attribute)
        {
            _execute = execute;
            _target = target;
            Text = attribute.Text;
            _itemFilter = attribute.Item;
            var where = $"[InventoryCommand] {execute.DeclaringType?.Name}.{execute.Name}";
            var parameters = execute.GetParameters();
            if (parameters.Length is < 1 or > 2 || !typeof(Inventory.InventoryItem).IsAssignableFrom(parameters[0].ParameterType)
                || parameters.Length == 2 && !parameters[1].ParameterType.IsAssignableFrom(typeof(IsoPlayer)))
                throw new InvalidOperationException($"{where} must take an InventoryItem type and optionally the player");
            _itemType = parameters[0].ParameterType;

            if (attribute.CanExecute is { } name)
            {
                _canExecute = execute.DeclaringType!.GetMethod(name, AnyMethod, parameters.Select(p => p.ParameterType).ToArray())
                              ?? throw new InvalidOperationException($"{where}: no {name} with the same parameters");
                if (_canExecute.ReturnType != typeof(CommandState) && _canExecute.ReturnType != typeof(bool))
                    throw new InvalidOperationException($"{where}: {name} must return CommandState or bool");
            }
        }

        public string Text { get; }

        public bool Accepts(GameObject item)
        {
            var wrapper = JavaConvert.To<Inventory.InventoryItem>(item);
            return _itemType.IsInstanceOfType(wrapper) && (_itemFilter is null || wrapper.GetFullType() == _itemFilter);
        }

        public CommandState CanExecute(GameObject item, IsoPlayer player) =>
            _canExecute is null
                ? CommandState.Enabled
                : Invoke(_canExecute, _target, Arguments(item, player)) switch
                {
                    CommandState state => state,
                    bool visible => visible,
                    _ => CommandState.Hidden,
                };

        public void Execute(GameObject item, IsoPlayer player) => Invoke(_execute, _target, Arguments(item, player));

        private object?[] Arguments(GameObject item, IsoPlayer player)
        {
            var typed = JavaConvert.To(item, _itemType);
            return _execute.GetParameters().Length == 2 ? [typed, player] : [typed];
        }
    }
}
