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
    private ModInstance? _owner;
    private GameObject? _apply;

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
            page._owner = owner;
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

    /// <summary>
    /// Runs when the player applies the options screen, after each changed option's <c>Applied</c> event and before
    /// the values are saved.
    /// </summary>
    protected virtual void OnApply()
    {
    }

    internal GameObject? Element(string id) =>
        PZAPI.ModOptions is GameObject modOptions
        && LuaObject.First(LuaHost.Required.CallMethod(null, modOptions, "getOptions", [Id])) is GameObject options
            ? ElementIn(options, id)
            : null;

    internal static GameObject? ElementIn(GameObject options, string id) =>
        LuaHost.Required.Index(options, "dict") is GameObject dict ? LuaHost.Required.Index(dict, id) as GameObject : null;

    /// <summary>A .NET function owned by the page's mod, made once and kept, since pages are rebuilt on every Lua reload.</summary>
    internal GameObject Function(ref GameObject? cache, Action<object?[]> body) =>
        cache ??= LuaHost.Required.CreateFunction(_owner ?? throw new InvalidOperationException($"Mod Options page '{Title}' isn't loaded"), body).Keep();

    internal ModInstance Owner => _owner ?? throw new InvalidOperationException($"Mod Options page '{Title}' isn't loaded");

    private void Create()
    {
        var modOptions = PZAPI.ModOptions as GameObject ?? throw new LuaException("PZAPI.ModOptions isn't loaded");
        var options = LuaObject.First(LuaHost.Required.CallMethod(null, modOptions, "create", [Id, Title])) as GameObject
                      ?? throw new LuaException("PZAPI.ModOptions:create returned nothing");
        foreach (var option in _options)
            option.Add(options);
        LuaHost.Required.SetIndex(Owner, options, "apply", Function(ref _apply, _ => OnApply()));
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

    private protected static object? Call(GameObject target, string method, params object?[] args) =>
        LuaObject.First(LuaHost.Required.CallMethod(null, target, method, args));

    private protected static object? Arg(object?[] args, int index) => index < args.Length ? args[index] : null;
}

/// <summary>An option the player interacts with, which can be greyed out.</summary>
public abstract class ModOptionControl : ModOption
{
    private bool _enabled = true;

    private protected ModOptionControl(string name, string? tooltip)
    {
        Name = name;
        Tooltip = tooltip;
    }

    /// <summary>The option's label, as a translation key or plain text.</summary>
    public string Name { get; }

    public string? Tooltip { get; }

    /// <summary>False greys the option out. Takes effect on the open page and whenever the page is rebuilt.</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (Live is { } element)
                Call(element, "setEnabled", value);
        }
    }

    /// <summary>The option's table on the current page, once the page exists.</summary>
    private protected GameObject? Live => Page?.Element(Id);

    private protected ModOptions BoundPage =>
        Page ?? throw new InvalidOperationException($"{GetType().Name} '{Name}' isn't declared on a ModOptions class");

    internal sealed override void Add(GameObject options)
    {
        AddTo(options);
        var element = ModOptions.ElementIn(options, Id) ?? throw new LuaException($"Mod Options didn't add '{Id}'");
        if (!_enabled)
            Call(element, "setEnabled", false);
        Configure(element);
    }

    private protected abstract void AddTo(GameObject options);

    /// <summary>Sets up the freshly added option's table: callbacks, values the mod set early.</summary>
    private protected abstract void Configure(GameObject element);
}

/// <summary>An option with a value the player sets.</summary>
public abstract class ModOption<T> : ModOptionControl
{
    private T _pending = default!;
    private bool _hasPending;
    private GameObject? _onChange, _onChangeApply;

    private protected ModOption(string name, T defaultValue, string? tooltip) : base(name, tooltip) => Default = defaultValue;

    public T Default { get; }

    /// <summary>
    /// The player's current choice, <see cref="Default"/> until the page exists. Setting it changes the open page;
    /// PZ saves it with the player's next change, or with the mod's, if it's set before the page exists.
    /// </summary>
    public T Value
    {
        get => BoundPage.Element(Id) is { } element ? Read(element) : _hasPending ? _pending : Default;
        set
        {
            if (BoundPage.Element(Id) is { } element)
                Write(element, value);
            else
                (_pending, _hasPending) = (value, true);
        }
    }

    /// <summary>Raised as the player edits the option, before they apply it, with the value shown.</summary>
    public event Action<T>? Changed;

    /// <summary>Raised when the player applies a new value, with that value; <see cref="Value"/> may still hold the old one.</summary>
    public event Action<T>? Applied;

    private protected abstract T Read(GameObject element);

    private protected abstract void Write(GameObject element, T value);

    /// <summary>Converts the value PZ passes to <c>onChange</c>/<c>onChangeApply</c>.</summary>
    private protected abstract T FromCallback(object? value);

    private protected sealed override void Configure(GameObject element)
    {
        var page = BoundPage;
        LuaHost.Required.SetIndex(page.Owner, element, "onChange",
            page.Function(ref _onChange, args => Changed?.Invoke(FromCallback(Arg(args, 1)))));
        LuaHost.Required.SetIndex(page.Owner, element, "onChangeApply",
            page.Function(ref _onChangeApply, args => Applied?.Invoke(FromCallback(Arg(args, 1)))));
        if (_hasPending)
        {
            Write(element, _pending);
            _hasPending = false;
        }
    }
}

public sealed class TickBox(string name, bool defaultValue, string? tooltip = null) : ModOption<bool>(name, defaultValue, tooltip)
{
    private protected override void AddTo(GameObject options) => Call(options, "addTickBox", Id, Name, Default, Tooltip);

    private protected override bool Read(GameObject element) => element.AsLua<ModOptionsTickBox>().GetValue();

    private protected override void Write(GameObject element, bool value) => element.AsLua<ModOptionsTickBox>().SetValue(value);

    private protected override bool FromCallback(object? value) => value is true;
}

public sealed class Slider(string name, double min, double max, double step, double defaultValue, string? tooltip = null)
    : ModOption<double>(name, defaultValue, tooltip)
{
    public double Min { get; } = min;

    public double Max { get; } = max;

    public double Step { get; } = step;

    private protected override void AddTo(GameObject options) => Call(options, "addSlider", Id, Name, Min, Max, Step, Default, Tooltip);

    private protected override double Read(GameObject element) => element.AsLua<ModOptionsSlider>().GetValue();

    private protected override void Write(GameObject element, double value) => element.AsLua<ModOptionsSlider>().SetValue(value);

    private protected override double FromCallback(object? value) => Convert.ToDouble(value ?? Default);
}

/// <summary>A key the player can rebind; the value is an LWJGL key code, as <c>OnKeyPressed</c> reports.</summary>
public sealed class KeyBind(string name, int defaultKey, string? tooltip = null) : ModOption<int>(name, defaultKey, tooltip)
{
    private protected override void AddTo(GameObject options) => Call(options, "addKeyBind", Id, Name, Default, Tooltip);

    private protected override int Read(GameObject element) => element.AsLua<ModOptionsKeybind>().GetValue();

    private protected override void Write(GameObject element, int value) => element.AsLua<ModOptionsKeybind>().SetValue(value);

    private protected override int FromCallback(object? value) => Convert.ToInt32(value ?? Default);
}

/// <summary>
/// A choice from a list; the value is the chosen item's 0-based index into <see cref="Items"/>. Items can be added
/// after the page exists, for choices other mods register later.
/// </summary>
public sealed class ComboBox(string name, IReadOnlyList<string> items, int defaultIndex = 0, string? tooltip = null)
    : ModOption<int>(name, defaultIndex, tooltip)
{
    private readonly List<string> _items = [.. items];

    /// <summary>The items, as translation keys or plain text.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>The chosen item, or null if nothing valid is chosen.</summary>
    public string? SelectedItem => Value is var index && index >= 0 && index < _items.Count ? _items[index] : null;

    /// <summary>Adds an item at the end, to the open page and whenever the page is rebuilt.</summary>
    public void AddItem(string item)
    {
        _items.Add(item);
        if (Live is { } element)
            Call(element, "addItem", item, false);
    }

    private protected override void AddTo(GameObject options)
    {
        var box = (GameObject)Call(options, "addComboBox", Id, Name, Tooltip)!;
        for (var i = 0; i < _items.Count; i++)
            Call(box, "addItem", _items[i], i == Default);
    }

    private protected override int Read(GameObject element) => element.AsLua<ModOptionsComboBox>().GetValue() - 1;

    private protected override void Write(GameObject element, int value) => element.AsLua<ModOptionsComboBox>().SetValue(value + 1);

    private protected override int FromCallback(object? value) => value is null ? Default : Convert.ToInt32(value) - 1;
}

public sealed class TextEntry(string name, string defaultValue, string? tooltip = null) : ModOption<string>(name, defaultValue, tooltip)
{
    private protected override void AddTo(GameObject options) => Call(options, "addTextEntry", Id, Name, Default, Tooltip);

    private protected override string Read(GameObject element) => element.AsLua<ModOptionsTextEntry>().GetValue();

    private protected override void Write(GameObject element, string value) => element.AsLua<ModOptionsTextEntry>().SetValue(value);

    private protected override string FromCallback(object? value) => value?.ToString() ?? "";
}

/// <summary>A colour, each channel from 0 to 1.</summary>
public readonly record struct OptionColor(double R, double G, double B, double A = 1);

/// <summary>A colour the player picks, with alpha.</summary>
public sealed class ColorPicker(string name, OptionColor defaultColor, string? tooltip = null)
    : ModOption<OptionColor>(name, defaultColor, tooltip)
{
    private protected override void AddTo(GameObject options) =>
        Call(options, "addColorPicker", Id, Name, Default.R, Default.G, Default.B, Default.A, Tooltip);

    private protected override OptionColor Read(GameObject element) => FromCallback(Call(element, "getValue"));

    private protected override void Write(GameObject element, OptionColor value)
    {
        var color = LuaHost.Required.NewTable();
        var mod = BoundPage.Owner;
        LuaHost.Required.SetIndex(mod, color, "r", value.R);
        LuaHost.Required.SetIndex(mod, color, "g", value.G);
        LuaHost.Required.SetIndex(mod, color, "b", value.B);
        LuaHost.Required.SetIndex(mod, color, "a", value.A);
        Call(element, "setValue", color);
    }

    private protected override OptionColor FromCallback(object? value) =>
        value is GameObject color
            ? new OptionColor(Channel(color, "r"), Channel(color, "g"), Channel(color, "b"), Channel(color, "a"))
            : Default;

    private static double Channel(GameObject color, string channel) => Convert.ToDouble(LuaHost.Required.Index(color, channel) ?? 1.0);
}

/// <summary>A row of tick boxes under one label; read and set each by its 0-based index.</summary>
public sealed class MultipleTickBox(string name, IReadOnlyList<(string Name, bool Default)> boxes, string? tooltip = null)
    : ModOptionControl(name, tooltip)
{
    private readonly bool[] _enabled = [.. boxes.Select(_ => true)];
    private readonly bool?[] _pending = new bool?[boxes.Count];
    private GameObject? _onChange, _onChangeApply;

    public IReadOnlyList<(string Name, bool Default)> Boxes { get; } = boxes;

    /// <summary>Whether box <paramref name="index"/> is ticked; set before the page exists, it's applied when the page is.</summary>
    public bool this[int index]
    {
        get => Live is { } element ? element.AsLua<ModOptionsMultipleTickBox>().GetValue(index + 1) : _pending[index] ?? Boxes[index].Default;
        set
        {
            if (Live is { } element)
                element.AsLua<ModOptionsMultipleTickBox>().SetValue(index + 1, value);
            else
                _pending[index] = value;
        }
    }

    /// <summary>Greys box <paramref name="index"/> out, or back in; kept when the page is rebuilt.</summary>
    public void SetEnabled(int index, bool enabled)
    {
        _enabled[index] = enabled;
        if (Live is { } element)
            element.AsLua<ModOptionsMultipleTickBox>().SetEnabled(Boxes[index].Name, enabled);
    }

    /// <summary>Raised as the player ticks or unticks a box, before they apply it: the box's index and state.</summary>
    public event Action<int, bool>? Changed;

    /// <summary>Raised for each box the player applies a change to: its index and new state.</summary>
    public event Action<int, bool>? Applied;

    private protected override void AddTo(GameObject options)
    {
        var option = (GameObject)Call(options, "addMultipleTickBox", Id, Name, Tooltip)!;
        foreach (var (box, ticked) in Boxes)
            Call(option, "addTickBox", box, ticked);
    }

    private protected override void Configure(GameObject element)
    {
        var page = BoundPage;
        LuaHost.Required.SetIndex(page.Owner, element, "onChange",
            page.Function(ref _onChange, args => Changed?.Invoke(Convert.ToInt32(Arg(args, 1)) - 1, Arg(args, 2) is true)));
        LuaHost.Required.SetIndex(page.Owner, element, "onChangeApply",
            page.Function(ref _onChangeApply, args => Applied?.Invoke(Convert.ToInt32(Arg(args, 1)) - 1, Arg(args, 2) is true)));
        var box = element.AsLua<ModOptionsMultipleTickBox>();
        for (var i = 0; i < _enabled.Length; i++)
        {
            if (!_enabled[i])
                box.SetEnabled(Boxes[i].Name, false);
            if (_pending[i] is { } ticked)
            {
                box.SetValue(i + 1, ticked);
                _pending[i] = null;
            }
        }
    }
}

/// <summary>A button that runs <paramref name="onClick"/> when the player presses it.</summary>
public sealed class Button(string name, Action onClick, string? tooltip = null) : ModOptionControl(name, tooltip)
{
    private GameObject? _onClick;

    private protected override void AddTo(GameObject options) =>
        Call(options, "addButton", Id, Name, Tooltip, BoundPage.Function(ref _onClick, _ => onClick()), null);

    private protected override void Configure(GameObject element)
    {
    }
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
