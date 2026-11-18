using Wrench;
using Zomboid.Java;
using Zomboid.Java.Characters;
using Zomboid.Java.Inventory;

namespace Zomboid.Lua.ContextMenus;

internal interface IInventoryCommand
{
    string Text { get; }

    string TextFor(GameObject item);

    bool Accepts(GameObject item);

    CommandState CanExecute(GameObject item, IsoPlayer player);

    void Execute(GameObject item, IsoPlayer player);

    event EventHandler? CanExecuteChanged;
}

/// <summary>
/// Base of <see cref="InventoryCommand{TItem}"/>, so a command can be named without its item type, as in
/// <see cref="InventoryCommands.NotifyCanExecuteChanged{TCommand}"/>.
/// </summary>
public abstract class InventoryCommand
{
    private protected InventoryCommand()
    {
    }

    /// <summary>The option's text, as a translation key or plain text.</summary>
    public abstract string Text { get; }

    /// <summary>Restricts the command to one item type, e.g. <c>Base.Plate</c>.</summary>
    public virtual string? Item => null;

    /// <summary>Raised by <see cref="NotifyCanExecuteChanged"/>; open menus showing the command re-check it.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Re-checks CanExecute for this command's options in open menus: they're enabled, greyed out or removed to
    /// match. Menus that were hiding it don't gain it until they're reopened.
    /// </summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// An inventory context-menu option for items of type <typeparamref name="TItem"/>, shown for the first clicked item
/// it's visible for.
/// Found and created when the mod loads; <see cref="InventoryCommandAttribute"/> is the shorthand for a method.
/// </summary>
public abstract class InventoryCommand<TItem> : InventoryCommand, IInventoryCommand where TItem : InventoryItem
{
    /// <summary>The option's text for <paramref name="item"/>, e.g. with its name or a count; <see cref="InventoryCommand.Text"/> by default.</summary>
    public virtual string TextFor(TItem item) => Text;

    public virtual CommandState CanExecute(TItem item, IsoPlayer player) => CommandState.Enabled;

    public abstract void Execute(TItem item, IsoPlayer player);

    string IInventoryCommand.TextFor(GameObject item) => TextFor(JavaConvert.To<TItem>(item));

    bool IInventoryCommand.Accepts(GameObject item) =>
        JavaConvert.To<InventoryItem>(item) is TItem typed && (Item is null || typed.GetFullType() == Item);

    CommandState IInventoryCommand.CanExecute(GameObject item, IsoPlayer player) => CanExecute(JavaConvert.To<TItem>(item), player);

    void IInventoryCommand.Execute(GameObject item, IsoPlayer player) => Execute(JavaConvert.To<TItem>(item), player);
}

public static class InventoryCommands
{
    /// <summary>
    /// <see cref="InventoryCommand.NotifyCanExecuteChanged"/> for the instance of <typeparamref name="TCommand"/> that
    /// Wrench created when this mod loaded. Call it as <c>this.NotifyCanExecuteChanged&lt;MyCommand&gt;()</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="TCommand"/> isn't a command in this mod's assembly.</exception>
    public static void NotifyCanExecuteChanged<TCommand>(this ModInstance mod) where TCommand : InventoryCommand
    {
        ArgumentNullException.ThrowIfNull(mod);
        ContextMenuRegistry.Command(mod, typeof(TCommand)).NotifyCanExecuteChanged();
    }
}
