using Wrench;
using Zomboid.Java;
using Zomboid.Java.Characters;

namespace Zomboid.Lua.ContextMenus;

/// <summary>A click on a context-menu option, with the option's arguments as they are now.</summary>
public sealed class OptionClick
{
    private readonly ModInstance _mod;
    private readonly int _player;
    private readonly object? _originalSelect;
    private readonly object? _originalTarget;

    internal OptionClick(ModInstance mod, int player, object?[] args, object? originalSelect, object? originalTarget)
    {
        _mod = mod;
        _player = player;
        Args = args;
        _originalSelect = originalSelect;
        _originalTarget = originalTarget;
    }

    public IsoPlayer Player => Globals.GetSpecificPlayer(_player);

    /// <summary>The option's parameters (<c>param1</c>…), passed to its handler by the menu.</summary>
    public IReadOnlyList<object?> Args { get; }

    /// <summary>Parameter <paramref name="index"/> (0 for <c>param1</c>) as <typeparamref name="T"/>, e.g. a wrapper type.</summary>
    public T Arg<T>(int index) => JavaConvert.To<T>(index < Args.Count ? Args[index] : null);

    /// <summary>True for an overridden option, which has an original handler.</summary>
    public bool HasOriginal => _originalSelect is not null;

    /// <summary>The overridden option's original target (often the handler's first argument).</summary>
    public dynamic? OriginalTarget => _originalTarget;

    /// <summary>Runs the handler the option had before it was overridden, with the same arguments.</summary>
    public void CallOriginal()
    {
        if (_originalSelect is null)
            throw new InvalidOperationException("This option has no original handler; only overridden options do.");
        LuaHost.Required.Call(_mod, _originalSelect, [_originalTarget, .. Args]);
    }
}
