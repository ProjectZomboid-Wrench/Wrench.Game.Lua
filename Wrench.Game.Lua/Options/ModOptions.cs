using System.Reflection;
using Wrench;
using Zomboid.Lua.Umbrella;

namespace Zomboid.Lua.Options;

/// <summary>
/// A mod's page in the game's Mod Options. Declare the options as fields or get-only properties, in the order they
/// should appear; each one's member name is its id, so renaming it resets the player's saved value:
/// <code>
/// internal sealed class Options() : ModOptions("UI_MyMod_Title")
/// {
///     public static readonly TickBox Enabled = new("UI_MyMod_Enabled", true);
/// }
/// </code>
/// Found and created when the mod loads; the page is rebuilt whenever PZ reloads its Lua. Read values with
/// <see cref="ModOption{T}.Value"/> wherever they're needed, since the player can change them at any time.
/// </summary>
public abstract class ModOptions(string title)
{
    private const BindingFlags AnyField =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly List<ModOption> _options = [];

    /// <summary>The page's heading, as a translation key or plain text.</summary>
    public string Title { get; } = title;

    /// <summary>The id PZ stores the page's values under in <c>ModOptions.ini</c>.</summary>
    public string Id { get; private set; } = "";

    /// <summary>Called by the loader once an assembly's mods have started; the first one owns the assembly's options.</summary>
    public static void Discover(Assembly assembly, IReadOnlyList<ModInstance> mods)
    {
        if (mods.Count == 0)
            return;
        var owner = mods[0];
        var pages = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(ModOptions))).ToList();
        foreach (var type in pages)
        {
            var page = (ModOptions)Activator.CreateInstance(type, nonPublic: true)!;
            page.Id = pages.Count == 1 ? owner.Id : $"{owner.Id}.{type.Name}";
            // Field order is declaration order, and auto-properties are backed by fields, so this covers both.
            foreach (var field in type.GetFields(AnyField).Where(f => typeof(ModOption).IsAssignableFrom(f.FieldType)).OrderBy(f => f.MetadataToken))
            {
                if (field.GetValue(field.IsStatic ? null : page) is not ModOption option)
                    continue;
                option.Bind(page, BackedName(field.Name));
                page._options.Add(option);
            }
            owner.Host.RegisterLuaEvent(owner, LuaEvents.OnGameBoot, _ => page.Create());
        }
    }

    internal GameObject? Element(string id) =>
        PZAPI.ModOptions is GameObject modOptions
        && LuaObject.First(LuaHost.Required.CallMethod(null, modOptions, "getOptions", [Id])) is GameObject options
        && LuaHost.Required.Index(options, "dict") is GameObject dict
            ? LuaHost.Required.Index(dict, id) as GameObject
            : null;

    private void Create()
    {
        var modOptions = PZAPI.ModOptions as GameObject ?? throw new LuaException("PZAPI.ModOptions isn't loaded");
        var options = LuaObject.First(LuaHost.Required.CallMethod(null, modOptions, "create", [Id, Title])) as GameObject
                      ?? throw new LuaException("PZAPI.ModOptions:create returned nothing");
        foreach (var option in _options)
            option.Add(options);
    }

    private static string BackedName(string field) =>
        field.StartsWith('<') && field.IndexOf('>') is > 1 and var end ? field[1..end] : field;
}

/// <summary>One entry on a <see cref="ModOptions"/> page.</summary>
public abstract class ModOption
{
    private protected ModOption()
    {
    }

    private protected ModOptions? Page { get; private set; }

    private protected string Id { get; private set; } = "";

    internal void Bind(ModOptions page, string id)
    {
        Page = page;
        Id = id;
    }

    internal abstract void Add(GameObject options);

    private protected static void Call(GameObject options, string method, params object?[] args) =>
        LuaHost.Required.CallMethod(null, options, method, args);
}

/// <summary>An option with a value the player sets.</summary>
public abstract class ModOption<T> : ModOption
{
    private protected ModOption(string name, T defaultValue, string? tooltip)
    {
        Name = name;
        Default = defaultValue;
        Tooltip = tooltip;
    }

    /// <summary>The option's label, as a translation key or plain text.</summary>
    public string Name { get; }

    public T Default { get; }

    public string? Tooltip { get; }

    /// <summary>The player's current choice; <see cref="Default"/> until the page exists.</summary>
    public T Value =>
        (Page ?? throw new InvalidOperationException($"{GetType().Name} '{Name}' isn't declared on a ModOptions class"))
        .Element(Id) is { } element
            ? Read(element)
            : Default;

    private protected abstract T Read(GameObject element);
}

public sealed class TickBox(string name, bool defaultValue, string? tooltip = null) : ModOption<bool>(name, defaultValue, tooltip)
{
    internal override void Add(GameObject options) => Call(options, "addTickBox", Id, Name, Default, Tooltip);

    private protected override bool Read(GameObject element) => element.AsLua<ModOptionsTickBox>().GetValue();
}

public sealed class Slider(string name, double min, double max, double step, double defaultValue, string? tooltip = null)
    : ModOption<double>(name, defaultValue, tooltip)
{
    public double Min { get; } = min;

    public double Max { get; } = max;

    public double Step { get; } = step;

    internal override void Add(GameObject options) => Call(options, "addSlider", Id, Name, Min, Max, Step, Default, Tooltip);

    private protected override double Read(GameObject element) => element.AsLua<ModOptionsSlider>().GetValue();
}

/// <summary>A key the player can rebind; the value is an LWJGL key code, as <c>OnKeyPressed</c> reports.</summary>
public sealed class KeyBind(string name, int defaultKey, string? tooltip = null) : ModOption<int>(name, defaultKey, tooltip)
{
    internal override void Add(GameObject options) => Call(options, "addKeyBind", Id, Name, Default, Tooltip);

    private protected override int Read(GameObject element) => element.AsLua<ModOptionsKeybind>().GetValue();
}

/// <summary>A choice from a list; the value is the chosen item's 0-based index into <see cref="Items"/>.</summary>
public sealed class ComboBox(string name, IReadOnlyList<string> items, int defaultIndex = 0, string? tooltip = null)
    : ModOption<int>(name, defaultIndex, tooltip)
{
    /// <summary>The items, as translation keys or plain text.</summary>
    public IReadOnlyList<string> Items { get; } = items;

    internal override void Add(GameObject options)
    {
        var box = (GameObject)LuaObject.First(LuaHost.Required.CallMethod(null, options, "addComboBox", [Id, Name, Tooltip]))!;
        for (var i = 0; i < Items.Count; i++)
            Call(box, "addItem", Items[i], i == Default);
    }

    private protected override int Read(GameObject element) => element.AsLua<ModOptionsComboBox>().GetValue() - 1;
}

public sealed class TextEntry(string name, string defaultValue, string? tooltip = null) : ModOption<string>(name, defaultValue, tooltip)
{
    internal override void Add(GameObject options) => Call(options, "addTextEntry", Id, Name, Default, Tooltip);

    private protected override string Read(GameObject element) => element.AsLua<ModOptionsTextEntry>().GetValue();
}

/// <summary>A heading between groups of options.</summary>
public sealed class Heading(string text) : ModOption
{
    internal override void Add(GameObject options) => Call(options, "addTitle", text);
}

/// <summary>A paragraph of text, as a translation key or plain text.</summary>
public sealed class Description(string text) : ModOption
{
    internal override void Add(GameObject options) => Call(options, "addDescription", text);
}

public sealed class Separator : ModOption
{
    internal override void Add(GameObject options) => Call(options, "addSeparator");
}
