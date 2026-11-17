using Wrench;
using Zomboid.Java;
using Zomboid.Java.Characters;
using Zomboid.Java.Inventory;

namespace Zomboid.Lua.ContextMenus;

internal interface IInventoryCommand
{
    string Text { get; }

    bool Accepts(GameObject item);

    CommandState CanExecute(GameObject item, IsoPlayer player);

    void Execute(GameObject item, IsoPlayer player);

    event EventHandler? CanExecuteChanged;
}

/// <summary>
/// An inventory context-menu option for items of type <typeparamref name="TItem"/>, shown for the first clicked item
/// it's visible for.
/// Found and created when the mod loads; <see cref="InventoryCommandAttribute"/> is the shorthand for a method.
/// </summary>
public abstract class InventoryCommand<TItem> : IInventoryCommand where TItem : InventoryItem
{
    /// <summary>The option's text, as a translation key or plain text.</summary>
    public abstract string Text { get; }

    /// <summary>Restricts the command to one item type, e.g. <c>Base.Plate</c>.</summary>
    public virtual string? Item => null;

    public virtual CommandState CanExecute(TItem item, IsoPlayer player) => CommandState.Enabled;

    public abstract void Execute(TItem item, IsoPlayer player);

    /// <summary>Raised by <see cref="NotifyCanExecuteChanged"/>; open menus showing the command re-check it.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Re-checks <see cref="CanExecute"/> for this command's options in open menus: they're enabled, greyed out or
    /// removed to match. Menus that were hiding it don't gain it until they're reopened.
    /// </summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    bool IInventoryCommand.Accepts(GameObject item) =>
        JavaConvert.To<InventoryItem>(item) is TItem typed && (Item is null || typed.GetFullType() == Item);

    CommandState IInventoryCommand.CanExecute(GameObject item, IsoPlayer player) => CanExecute(JavaConvert.To<TItem>(item), player);

    void IInventoryCommand.Execute(GameObject item, IsoPlayer player) => Execute(JavaConvert.To<TItem>(item), player);
}
