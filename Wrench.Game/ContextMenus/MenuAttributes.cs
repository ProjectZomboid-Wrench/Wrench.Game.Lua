namespace Zomboid.ContextMenus;

/// <summary>
/// Makes a method of a mod class an inventory context-menu option: <c>void Garnish(Food plate, IsoPlayer player)</c>.
/// The first parameter's type picks the items it applies to; the player parameter is optional.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InventoryCommandAttribute(string text) : Attribute
{
    /// <summary>The option's text, as a translation key or plain text.</summary>
    public string Text { get; } = text;

    /// <summary>Restricts the command to one item type, e.g. <c>Base.Plate</c>.</summary>
    public string? Item { get; set; }

    /// <summary>
    /// The name of a method taking the same parameters and returning <see cref="CommandState"/> or <c>bool</c>
    /// (false hides the option); use <c>nameof</c>.
    /// </summary>
    public string? CanExecute { get; set; }
}

public enum MenuKind
{
    Inventory,
    World,
}

/// <summary>
/// Runs a method of a mod class, <c>void Eat(OptionClick click)</c>, instead of the handler of the option showing
/// <see cref="Text"/> (a translation key or plain text), whoever added it. <see cref="OptionClick.CallOriginal"/> runs it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class OverrideOptionAttribute(string text) : Attribute
{
    public string Text { get; } = text;

    public MenuKind Menu { get; set; } = MenuKind.Inventory;
}
