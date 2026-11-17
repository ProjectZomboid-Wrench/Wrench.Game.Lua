// Regenerates Wrench.Game.Lua/Generated/ from vanilla's Lua classes:
//   dotnet run tools/GenerateLuaClasses.cs -- <pz-lua-stubdata checkout> <projectzomboid dir>
// Types come from pz-lua-stubdata (github.com/PZ-Umbrella/pz-lua-stubdata, CC BY 4.0) plus tools/luaclasses/overlay.yml;
// the game's own media/lua decides which classes and methods exist and their parameter names, since the data can lag.
#:package YamlDotNet@16.3.0
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

var stubdata = Path.GetFullPath(args[0]);
var gameLua = Path.Combine(Path.GetFullPath(args[1]), "media", "lua");
var repo = Directory.GetCurrentDirectory();
var output = Path.Combine(repo, "Wrench.Game.Lua", "Generated");

var model = new LuaModel();
foreach (var file in Directory.EnumerateFiles(Path.Combine(stubdata, "yml"), "*.yml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    model.Load(file, Path.GetRelativePath(Path.Combine(stubdata, "yml"), file));
var source = GameSource.Scan(gameLua);
var report = model.Reconcile(source);
// After reconciling, so the overlay can add what the game's Lua doesn't define (complete() on the base action).
model.Load(Path.Combine(repo, "tools", "luaclasses", "overlay.yml"), null, overlay: true);

var javaTypes = JavaNames.Load(Path.Combine(repo, "Wrench.Game.Java", "Generated", "JavaTypes.Registry.cs"));
if (Directory.Exists(output))
    Directory.Delete(output, recursive: true);
Directory.CreateDirectory(output);
var commit = GitCommit(stubdata);
var files = new LuaGenerator(model, javaTypes, commit).Write(output);
Console.WriteLine($"{model.Classes.Count(c => c.Value.IsGenerated)} classes, {model.Tables.Count} tables, {model.Functions.Count} functions -> {files} files in {output}");
Console.WriteLine(report);

static string GitCommit(string checkout)
{
    try
    {
        var start = new ProcessStartInfo("git", ["-C", checkout, "rev-parse", "--short", "HEAD"]) { RedirectStandardOutput = true };
        using var process = Process.Start(start)!;
        var text = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? text : "(unknown commit)";
    }
    catch (Exception)
    {
        return "(unknown commit)";
    }
}

sealed record LParam(string Name, string? Type, bool Nullable);

sealed record LFunc(string Name, List<LParam> Params, string? Return, bool ReturnNullable, string? Notes);

sealed record LField(string Type, bool Nullable, string? Notes);

sealed class LClass(string name)
{
    public string Name { get; } = name;
    public string? Extends { get; set; }
    public bool Local { get; set; }
    public bool IsTable { get; set; }
    public string? File { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, LField> Fields { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, LField> StaticFields { get; } = new(StringComparer.Ordinal);
    public List<LFunc> Methods { get; set; } = [];
    public List<LFunc> StaticMethods { get; set; } = [];
    public List<List<LParam>> Constructors { get; set; } = [];

    public bool IsShape => Name.StartsWith("umbrella.", StringComparison.Ordinal);

    // Classes reachable as Lua globals, plus Umbrella's named table shapes.
    public bool IsGenerated => !Local && (IsShape || Regex.IsMatch(Name, "^[A-Za-z_][A-Za-z0-9_]*$"));
}

sealed class LuaModel
{
    public Dictionary<string, LClass> Classes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, LClass> Tables { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, LFunc> Functions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

    public void Load(string path, string? relative, bool overlay = false)
    {
        var yaml = new YamlStream();
        using (var reader = File.OpenText(path))
            yaml.Load(reader);
        if (yaml.Documents.Count == 0 || Y.Child(Y.Child(yaml.Documents[0].RootNode, "languages"), "lua") is not { } lua)
            return;
        var folder = relative is null ? null : Path.GetDirectoryName(relative)!.Replace('\\', '/');
        LoadClasses(Y.Child(lua, "classes"), Classes, folder, overlay, isTable: false);
        LoadClasses(Y.Child(lua, "tables"), Tables, folder, overlay, isTable: true);
        if (Y.Child(lua, "functions") is YamlSequenceNode functions)
        {
            foreach (var function in functions.Children)
                Functions[Y.Text(function, "name")!] = Func(function);
        }
        if (Y.Child(lua, "aliases") is YamlMappingNode aliases)
        {
            foreach (var (name, types) in aliases.Children)
                Aliases[name.ToString()] = string.Join(" | ", ((YamlSequenceNode)types).Children.Select(t => Y.Text(t, "type") ?? "unknown"));
        }
    }

    private static void LoadClasses(YamlNode? node, Dictionary<string, LClass> into, string? folder, bool overlay, bool isTable)
    {
        if (node is not YamlMappingNode classes)
            return;
        foreach (var (key, body) in classes.Children)
        {
            var name = key.ToString();
            if (!into.TryGetValue(name, out var type))
            {
                if (overlay)
                    continue;
                into[name] = type = new LClass(name) { IsTable = isTable, File = folder };
            }
            type.Extends = Y.Text(body, "extends") ?? type.Extends;
            type.Local |= Y.Text(body, "local") == "true";
            type.Notes = Y.Text(body, "notes") ?? type.Notes;
            Fields(Y.Child(body, "fields"), type.Fields);
            Fields(Y.Child(body, "staticFields"), type.StaticFields);
            Merge(type.Methods, Y.Child(body, "methods"), overlay);
            Merge(type.StaticMethods, Y.Child(body, "staticMethods"), overlay);
            if (Y.Child(body, "constructors") is YamlSequenceNode constructors)
                type.Constructors = [.. constructors.Children.Select(c => Params(Y.Child(c, "parameters")))];
        }
    }

    private static void Fields(YamlNode? node, Dictionary<string, LField> into)
    {
        if (node is not YamlMappingNode fields)
            return;
        foreach (var (name, body) in fields.Children)
            into[name.ToString()] = new LField(Y.Text(body, "type") ?? FromDefault(Y.Text(body, "defaultValue")),
                Y.Text(body, "nullable") == "true", Y.Text(body, "notes"));
    }

    // Static fields often only record their initial value: IDMax = 1, Type = "ISBaseTimedAction".
    private static string FromDefault(string? value) => value switch
    {
        null => "unknown",
        "true" or "false" => "boolean",
        ['"', ..] or ['\'', ..] => "string",
        _ when double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _) => "number",
        _ => "unknown",
    };

    // An overlay entry only replaces the parts it gives: a corrected return type keeps the existing parameters.
    private static void Merge(List<LFunc> into, YamlNode? node, bool overlay)
    {
        if (node is not YamlSequenceNode functions)
            return;
        foreach (var entry in functions.Children)
        {
            var function = Func(entry);
            var index = into.FindIndex(f => f.Name == function.Name);
            if (index >= 0 && overlay)
            {
                var existing = into[index];
                function = function with
                {
                    Params = Y.Child(entry, "parameters") is null ? existing.Params : function.Params,
                    Return = Y.Child(entry, "return") is null ? existing.Return : function.Return,
                    ReturnNullable = Y.Child(entry, "return") is null ? existing.ReturnNullable : function.ReturnNullable,
                    Notes = function.Notes ?? existing.Notes,
                };
            }
            if (index >= 0)
                into[index] = function;
            else
                into.Add(function);
        }
    }

    private static LFunc Func(YamlNode node)
    {
        var returns = Y.Child(node, "return") as YamlSequenceNode;
        var first = returns?.Children.FirstOrDefault();
        return new LFunc(Y.Text(node, "name") ?? "", Params(Y.Child(node, "parameters")), first is null ? null : Y.Text(first, "type") ?? "unknown",
            first is not null && Y.Text(first, "nullable") == "true", Y.Text(node, "notes"));
    }

    private static List<LParam> Params(YamlNode? node) =>
        node is YamlSequenceNode parameters
            ? [.. parameters.Children.Select(p => new LParam(Y.Text(p, "name") ?? "arg", Y.Text(p, "type"), Y.Text(p, "nullable") == "true"))]
            : [];

    /// <summary>
    /// Makes the data match the game: the game's Lua decides which classes and methods exist and what their parameters
    /// are called; the data contributes types. Members the data doesn't know come out untyped.
    /// </summary>
    public string Reconcile(GameSource source)
    {
        int droppedClasses = 0, droppedMethods = 0, addedClasses = 0, addedMethods = 0;
        foreach (var (name, extends) in source.Derived)
        {
            if (Classes.ContainsKey(name) || Tables.ContainsKey(name))
                continue;
            Classes[name] = new LClass(name) { Extends = extends, File = source.Files.GetValueOrDefault(name) };
            addedClasses++;
        }
        foreach (var type in Classes.Values.Concat(Tables.Values).Where(c => c.IsGenerated && !c.IsShape).ToList())
        {
            if (!source.Defined.Contains(type.Name))
            {
                (type.IsTable ? Tables : Classes).Remove(type.Name);
                droppedClasses++;
                continue;
            }
            var methods = source.Methods.GetValueOrDefault(type.Name) ?? [];
            var statics = source.Statics.GetValueOrDefault(type.Name) ?? [];
            if (methods.TryGetValue("new", out var constructor))
                type.Constructors = [Retype(constructor, type.Constructors.FirstOrDefault(c => c.Count == constructor.Count) ?? type.Constructors.FirstOrDefault() ?? [])];
            droppedMethods += Rebuild(type.Methods, methods, statics, ref addedMethods);
            droppedMethods += Rebuild(type.StaticMethods, statics, methods, ref addedMethods);
        }
        return $"Reconciled with the game's Lua: {addedClasses} classes and {addedMethods} methods added untyped, " +
               $"{droppedClasses} classes and {droppedMethods} methods dropped as gone";
    }

    private static int Rebuild(List<LFunc> functions, Dictionary<string, List<string>> defined, Dictionary<string, List<string>> other, ref int added)
    {
        var dropped = functions.RemoveAll(f => f.Name == "new" || !defined.ContainsKey(f.Name) && !other.ContainsKey(f.Name));
        foreach (var (name, parameters) in defined.Where(d => d.Key != "new"))
        {
            var index = functions.FindIndex(f => f.Name == name);
            if (index < 0)
            {
                functions.Add(new LFunc(name, [.. parameters.Select(p => new LParam(p, null, true))], "unknown", true, null));
                added++;
            }
            else
            {
                functions[index] = functions[index] with { Params = Retype(parameters, functions[index].Params) };
            }
        }
        return dropped;
    }

    // The game's parameter names and arity, with the data's types where a name (or failing that a position) matches.
    private static List<LParam> Retype(List<string> names, List<LParam> typed) =>
        [.. names.Select((name, i) => typed.FirstOrDefault(p => p.Name == name)
                                     ?? (i < typed.Count && typed.Count == names.Count ? typed[i] with { Name = name } : new LParam(name, null, true)))];
}

/// <summary>What the game's own Lua defines: <c>X = Base:derive(...)</c>, <c>function X:m(...)</c>, <c>function X.f(...)</c>.</summary>
sealed class GameSource
{
    public Dictionary<string, string> Derived { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Defined { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, List<string>>> Methods { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, List<string>>> Statics { get; } = new(StringComparer.Ordinal);

    private static readonly Regex Derive = new(@"^([A-Za-z_]\w*)\s*=\s*([A-Za-z_][\w.]*)\s*:\s*derive\s*\(", RegexOptions.Compiled);
    private static readonly Regex Assigned = new(@"^([A-Za-z_]\w*)\s*=", RegexOptions.Compiled);
    private static readonly Regex Method = new(@"^\s*function\s+([A-Za-z_]\w*)\s*:\s*([A-Za-z_]\w*)\s*\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex Static = new(@"^\s*function\s+([A-Za-z_]\w*)\.([A-Za-z_]\w*)\s*\(([^)]*)\)", RegexOptions.Compiled);
    // local ListBox = AnimationClipViewer_ListBox, then function ListBox:render(): methods defined through a file-local alias.
    private static readonly Regex Alias = new(@"^\s*local\s+([A-Za-z_]\w*)\s*=\s*([A-Za-z_]\w*)\s*(--.*)?$", RegexOptions.Compiled);
    private static readonly Regex LocalDerive = new(@"^\s*local\s+([A-Za-z_]\w*)\s*=\s*[A-Za-z_][\w.]*\s*:\s*derive\s*\(\s*[""']([A-Za-z_]\w*)[""']", RegexOptions.Compiled);
    private static readonly Regex StaticAssigned = new(@"^\s*([A-Za-z_]\w*)\.([A-Za-z_]\w*)\s*=\s*function\s*\(([^)]*)\)", RegexOptions.Compiled);

    public static GameSource Scan(string luaRoot)
    {
        var source = new GameSource();
        foreach (var file in Directory.EnumerateFiles(luaRoot, "*.lua", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var folder = Path.GetDirectoryName(Path.GetRelativePath(luaRoot, file))!.Replace('\\', '/');
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(file))
            {
                if (Alias.Match(line) is { Success: true } alias && alias.Groups[2].Value is not ("nil" or "true" or "false"))
                    aliases[alias.Groups[1].Value] = alias.Groups[2].Value;
                else if (LocalDerive.Match(line) is { Success: true } local)
                    aliases[local.Groups[1].Value] = local.Groups[2].Value;
                if (Derive.Match(line) is { Success: true } derive)
                {
                    source.Derived.TryAdd(derive.Groups[1].Value, derive.Groups[2].Value);
                    source.Files.TryAdd(derive.Groups[1].Value, folder);
                }
                if (Assigned.Match(line) is { Success: true } assigned)
                    source.Defined.Add(assigned.Groups[1].Value);
                if (Method.Match(line) is { Success: true } method)
                    source.Add(source.Methods, method, aliases);
                else if (Static.Match(line) is { Success: true } function)
                    source.Add(source.Statics, function, aliases);
                else if (StaticAssigned.Match(line) is { Success: true } assignedFunction)
                    source.Add(source.Statics, assignedFunction, aliases);
            }
        }
        return source;
    }

    private void Add(Dictionary<string, Dictionary<string, List<string>>> into, Match match, Dictionary<string, string> aliases)
    {
        var owner = aliases.GetValueOrDefault(match.Groups[1].Value, match.Groups[1].Value);
        Defined.Add(owner);
        var parameters = match.Groups[3].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Regex.IsMatch(p, @"^[A-Za-z_]\w*$|^\.\.\.$")).ToList();
        if (!into.TryGetValue(owner, out var functions))
            into[owner] = functions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        functions.TryAdd(match.Groups[2].Value, parameters);
    }
}

/// <summary>Java simple names (as stubdata writes them) to the generated Java wrappers, where unambiguous.</summary>
static class JavaNames
{
    public static Dictionary<string, string> Load(string registry)
    {
        var all = Regex.Matches(File.ReadAllText(registry), @"byJavaName\[""([^""]+)""\] = h => new (global::[\w.]+)\(h\);")
            .Select(m => (Java: m.Groups[1].Value, Cs: m.Groups[2].Value)).ToList();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in all.SelectMany(t => Keys(t.Java).Select(k => (Key: k, t.Cs))).GroupBy(t => t.Key))
        {
            var distinct = group.Select(t => t.Cs).Distinct().ToList();
            if (distinct.Count == 1)
                names[group.Key] = distinct[0];
        }
        return names;
    }

    private static IEnumerable<string> Keys(string javaName)
    {
        var simple = javaName[(javaName.LastIndexOf('.') + 1)..];
        yield return simple.Replace('$', '.');
        if (simple.Contains('$'))
            yield return simple[(simple.LastIndexOf('$') + 1)..];
    }
}

static class Y
{
    public static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode map && map.Children.TryGetValue(key, out var value) ? value : null;

    public static string? Text(YamlNode? node, string key) =>
        Child(node, key) is YamlScalarNode { Value: { Length: > 0 } value } ? value : null;
}

sealed record CsType(string Name, bool IsValueType, bool IsDynamic = false)
{
    public static readonly CsType Dynamic = new("dynamic", false, IsDynamic: true);
    public static readonly CsType Object = new("object", false, IsDynamic: true);

    public string Nullable(bool nullable) => IsDynamic || !nullable || Name.EndsWith('?') ? Name : Name + "?";
}

sealed class LuaGenerator
{
    private const string Ns = "Zomboid.Lua";
    private const string ShapeNs = "Zomboid.Lua.Umbrella";

    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while", "union", "dynamic",
    ];

    // LuaObject's own members and the runtime's type names, which generated members and classes must not take.
    private static readonly HashSet<string> Reserved =
    [
        "Table", "IsReleased", "IsSubclassInstance", "Get", "Set", "CallLua", "CallBase", "Invoke", "InvokeVoid",
        "Release", "Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone", "Finalize", "ReferenceEquals",
        "New", "Class", "Id", "Mod", "IsTruthy", "First", "DropTable",
    ];

    private static readonly HashSet<string> RuntimeTypes =
    [
        "LuaObject", "LuaClass", "LuaClassAttribute", "LuaMethodAttribute", "LuaWrap", "LuaNew", "LuaConvert", "LuaTypes",
        "LuaStatic", "LuaSequence", "LuaMap", "OneOf", "LuaGlobals", "ContextMenus", "TimedActions", "TimedAction", "Umbrella",
    ];

    private readonly LuaModel _model;
    private readonly Dictionary<string, string> _java;
    private readonly string _commit;
    private readonly Dictionary<string, string> _csNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, Member>> _members = new(StringComparer.Ordinal);

    private sealed record Member(string Kind, string Signature, string Type);

    public LuaGenerator(LuaModel model, Dictionary<string, string> java, string commit)
    {
        _model = model;
        _java = java;
        _commit = commit;
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in Generated.OrderBy(c => c.IsShape).ThenBy(c => c.Name, StringComparer.Ordinal))
        {
            var simple = type.IsShape ? string.Concat(type.Name["umbrella.".Length..].Split('.').Select(Pascal)) : type.Name;
            if (RuntimeTypes.Contains(simple) || Keywords.Contains(simple))
                simple += "_";
            while (!used.Add((type.IsShape ? ShapeNs : Ns) + "." + simple))
                simple += "_";
            _csNames[type.Name] = $"global::{(type.IsShape ? ShapeNs : Ns)}.{simple}";
        }
    }

    private IEnumerable<LClass> Generated => _model.Classes.Values.Where(c => c.IsGenerated);

    // (topmost class declaring it, C# name, parameter types) -> the return type every override in the hierarchy uses.
    private readonly Dictionary<string, string> _slotReturns = new(StringComparer.Ordinal);

    private string Return(LFunc method) => method.Return is null ? "void" : Map(method.Return).Nullable(method.ReturnNullable);

    private string SlotKey(LFunc method) =>
        Identifier(method.Name) is { } identifier ? $"{Pascal(identifier)}({Parameters(method.Params).Types})" : "";

    private string Slot(LClass type, LFunc method)
    {
        var key = SlotKey(method);
        var root = type;
        for (var t = Base(type); t is not null; t = Base(t))
        {
            if (t.Methods.Any(m => SlotKey(m) == key))
                root = t;
        }
        return root.Name + "." + key;
    }

    // C# can't override across return types, but Lua doesn't care: onMouseDown returns nothing on ISUIElement and a
    // boolean on some panels. One type per slot keeps every override chain intact.
    private void UnifyReturns()
    {
        var entries = Generated.Where(c => !c.IsShape)
            .SelectMany(c => c.Methods.Select(m => (Slot: Slot(c, m), Return: Return(m), IsRoot: Slot(c, m) == c.Name + "." + SlotKey(m))));
        foreach (var slot in entries.GroupBy(x => x.Slot))
        {
            var returns = slot.Select(x => x.Return).Distinct().ToList();
            if (returns.Count == 1)
                continue;
            // The topmost declaration's type wins when it has one: subclasses typed "unknown" or with no return are
            // usually gaps in the data, not a different contract.
            var root = slot.FirstOrDefault(x => x.IsRoot).Return;
            if (root is not null and not "dynamic")
            {
                _slotReturns[slot.Key] = root;
                continue;
            }
            var concrete = returns.Where(r => r is not "void" and not "dynamic").Select(r => r.TrimEnd('?')).Distinct().ToList();
            _slotReturns[slot.Key] = concrete.Count switch
            {
                0 => returns.Contains("dynamic") ? "dynamic" : "void",
                1 => concrete[0] + "?",
                _ => "dynamic",
            };
        }
    }

    public int Write(string directory)
    {
        UnifyReturns();
        // Every base is written before its subclasses, whatever file they land in, so override/new sees its members.
        var classes = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        foreach (var type in Ordered(Generated))
        {
            WriteClass(classes[type.Name] = new StringBuilder(), type);
        }
        var files = 0;
        foreach (var group in Generated.GroupBy(c => c.IsShape ? "umbrella" : (c.File ?? "other").Replace('/', '.')))
        {
            var code = Header();
            code.AppendLine($"namespace {(group.Key == "umbrella" ? ShapeNs : Ns)};").AppendLine();
            foreach (var type in group.OrderBy(c => c.Name, StringComparer.Ordinal))
                code.Append(classes[type.Name]);
            File.WriteAllText(Path.Combine(directory, group.Key + ".cs"), code.ToString());
            files++;
        }

        var tables = Header().AppendLine($"namespace {Ns};").AppendLine();
        foreach (var table in _model.Tables.Values.Where(t => t.IsGenerated && !_csNames.ContainsKey(t.Name)).OrderBy(t => t.Name, StringComparer.Ordinal))
            WriteTable(tables, table);
        WriteGlobals(tables);
        File.WriteAllText(Path.Combine(directory, "Tables.cs"), tables.ToString());

        var registry = Header().AppendLine($"namespace {Ns};").AppendLine()
            .AppendLine("internal static partial class LuaTypes")
            .AppendLine("{")
            .AppendLine("    static partial void Register(Dictionary<string, Func<LuaWrap, LuaObject>> byLuaName,")
            .AppendLine("                                 Dictionary<Type, Func<LuaWrap, LuaObject>> byType, Dictionary<Type, string> luaNames)")
            .AppendLine("    {");
        foreach (var type in Generated.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var cs = _csNames[type.Name];
            if (!type.IsShape)
            {
                registry.AppendLine($"        byLuaName[\"{type.Name}\"] = w => new {cs}(w);");
                registry.AppendLine($"        luaNames[typeof({cs})] = \"{type.Name}\";");
            }
            registry.AppendLine($"        byType[typeof({cs})] = w => new {cs}(w);");
        }
        registry.AppendLine("    }").AppendLine("}");
        File.WriteAllText(Path.Combine(directory, "LuaTypes.Registry.cs"), registry.ToString());
        return files + 2;
    }

    private IEnumerable<LClass> Ordered(IEnumerable<LClass> types) => types.OrderBy(Depth).ThenBy(c => c.Name, StringComparer.Ordinal);

    private int Depth(LClass type)
    {
        var depth = 0;
        for (var t = Base(type); t is not null && depth < 50; t = Base(t))
            depth++;
        return depth;
    }

    private LClass? Base(LClass type) =>
        !type.IsShape && type.Extends is { } name && _model.Classes.TryGetValue(name, out var found) && found.IsGenerated && found != type ? found : null;

    private StringBuilder Header() =>
        new StringBuilder()
            .AppendLine("// <auto-generated>tools/GenerateLuaClasses.cs from vanilla's Lua classes. Don't edit.</auto-generated>")
            .AppendLine($"// Types: pz-lua-stubdata {_commit} (https://github.com/PZ-Umbrella/pz-lua-stubdata), CC BY 4.0.")
            .AppendLine("#nullable enable")
            .AppendLine("#pragma warning disable CS0108, CS0114")
            .AppendLine();

    private void WriteClass(StringBuilder code, LClass type)
    {
        var cs = _csNames[type.Name];
        var name = cs[(cs.LastIndexOf('.') + 1)..];
        var baseType = Base(type) is { } b ? _csNames[b.Name] : "global::Zomboid.Lua.LuaObject";
        var inherited = Base(type) is { } bt ? new Dictionary<string, Member>(Members(bt)) : new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (var reserved in Reserved)
            inherited.TryAdd(reserved, new Member("reserved", "", ""));
        var own = new Dictionary<string, Member>(StringComparer.Ordinal);
        _members[type.Name] = own;
        var claimed = new HashSet<string>(StringComparer.Ordinal) { name };

        var summary = type.IsShape ? $"The table shape Umbrella calls <c>{type.Name}</c>." : $"Lua class <c>{type.Name}</c>{(type.File is { } f ? $" (<c>{f}</c>)" : "")}.";
        code.AppendLine($"/// <summary>{summary}{Doc(type.Notes, " ")}</summary>");
        if (!type.IsShape)
            code.AppendLine($"[global::Zomboid.Lua.LuaClass(\"{type.Name}\")]");
        code.AppendLine($"public partial class {name} : {baseType}")
            .AppendLine("{")
            .AppendLine($"    protected internal {name}(global::Zomboid.Lua.LuaWrap wrap) : base(wrap)")
            .AppendLine("    {")
            .AppendLine("    }");
        if (!type.IsShape)
        {
            code.AppendLine()
                .AppendLine("    /// <summary>For C# subclasses: runs the class's <c>new</c> with these arguments.</summary>")
                .AppendLine($"    protected {name}(global::Zomboid.Lua.LuaNew construct) : base(construct)")
                .AppendLine("    {")
                .AppendLine("    }");
            var constructors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var constructor in type.Constructors)
            {
                var parameters = Parameters(constructor);
                if (!constructors.Add(parameters.Types) || parameters.Types is "global::Zomboid.Lua.LuaNew" or "global::Zomboid.Lua.LuaWrap")
                    continue;
                code.AppendLine()
                    .AppendLine($"    protected {name}({parameters.Declaration}) : this(new global::Zomboid.Lua.LuaNew({parameters.Arguments}))")
                    .AppendLine("    {")
                    .AppendLine("    }")
                    .AppendLine()
                    .AppendLine($"    /// <summary>A new <c>{type.Name}</c>, as <c>{type.Name}:new(...)</c>.</summary>")
                    .AppendLine($"    public static {name} New({parameters.Declaration}) => global::Zomboid.Lua.LuaStatic.New<{name}>(\"{type.Name}\"{Prefix(parameters.Arguments)});");
            }
        }

        foreach (var (field, info) in type.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (MemberName(field, claimed) is not { } property)
                continue;
            var csType = Map(info.Type).Nullable(info.Nullable);
            var hides = inherited.ContainsKey(property) ? "new " : "";
            own[property] = new Member("property", "", csType);
            code.AppendLine();
            Summary(code, info.Notes);
            code.AppendLine($"    public {hides}{csType} {property}")
                .AppendLine("    {")
                .AppendLine($"        get => Get<{csType}>(\"{field}\");")
                .AppendLine($"        set => Set(\"{field}\", value);")
                .AppendLine("    }");
        }
        if (!type.IsShape)
        {
            foreach (var (field, info) in type.StaticFields.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                if (MemberName(field, claimed) is not { } property)
                    continue;
                var csType = Map(info.Type).Nullable(info.Nullable);
                var hides = inherited.ContainsKey(property) ? "new " : "";
                own[property] = new Member("static", "", csType);
                code.AppendLine();
                Summary(code, info.Notes);
                code.AppendLine($"    public static {hides}{csType} {property}")
                    .AppendLine("    {")
                    .AppendLine($"        get => global::Zomboid.Lua.LuaStatic.Get<{csType}>(\"{type.Name}\", \"{field}\");")
                    .AppendLine($"        set => global::Zomboid.Lua.LuaStatic.Set(\"{type.Name}\", \"{field}\", value);")
                    .AppendLine("    }");
            }
        }

        var methodNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var method in type.Methods.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var parameters = Parameters(method.Params);
            var csName = MethodName(method.Name, parameters.Types, claimed, methodNames);
            if (csName is null)
                continue;
            var returns = _slotReturns.GetValueOrDefault(Slot(type, method)) ?? Return(method);
            var signature = $"{csName}({parameters.Types})";
            string modifier;
            if (inherited.TryGetValue(signature, out var baseMethod) && baseMethod.Kind == "method")
                modifier = baseMethod.Type == returns ? "override" : "new virtual";
            else
                modifier = inherited.ContainsKey(csName) && inherited[csName].Kind != "methodname" ? "new virtual" : "virtual";
            own[signature] = new Member("method", signature, returns);
            own.TryAdd(csName, new Member("methodname", "", ""));
            var call = returns == "void"
                ? $"InvokeVoid(\"{type.Name}\", \"{method.Name}\"{Prefix(parameters.Arguments)})"
                : $"Invoke<{returns}>(\"{type.Name}\", \"{method.Name}\"{Prefix(parameters.Arguments)})";
            code.AppendLine();
            Summary(code, method.Notes);
            if (!type.IsShape)
                code.AppendLine($"    [global::Zomboid.Lua.LuaMethod(\"{method.Name}\")]");
            code.AppendLine($"    public {modifier} {returns} {csName}({parameters.Declaration}) => {call};");
        }
        if (!type.IsShape)
            WriteStatics(code, type, type.StaticMethods, claimed, inherited, own);
        code.AppendLine("}").AppendLine();
    }

    // Dot functions (colon = false) are called as Class.f(...); a table's colon methods as Table:m(...).
    private void WriteStatics(StringBuilder code, LClass type, List<LFunc> functions, HashSet<string> claimed,
                              Dictionary<string, Member> inherited, Dictionary<string, Member> own, bool colon = false,
                              Dictionary<string, string>? sharedNames = null)
    {
        var names = sharedNames ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var helper = colon ? "CallMethod" : "Call";
        foreach (var function in functions.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var parameters = Parameters(function.Params);
            var csName = MethodName(function.Name, parameters.Types, claimed, names);
            if (csName is null)
                continue;
            var returns = function.Return is null ? "void" : Map(function.Return).Nullable(function.ReturnNullable);
            var hides = inherited.ContainsKey(csName) || inherited.ContainsKey($"{csName}({parameters.Types})") ? "new " : "";
            own[$"{csName}({parameters.Types})"] = new Member("static", "", returns);
            var call = returns == "void"
                ? $"global::Zomboid.Lua.LuaStatic.{helper}Void(\"{type.Name}\", \"{function.Name}\"{Prefix(parameters.Arguments)})"
                : $"global::Zomboid.Lua.LuaStatic.{helper}<{returns}>(\"{type.Name}\", \"{function.Name}\"{Prefix(parameters.Arguments)})";
            code.AppendLine();
            Summary(code, function.Notes);
            code.AppendLine($"    public static {hides}{returns} {csName}({parameters.Declaration}) => {call};");
        }
    }

    private void WriteTable(StringBuilder code, LClass table)
    {
        var name = table.Name;
        if (RuntimeTypes.Contains(name) || Keywords.Contains(name))
            name += "_";
        code.AppendLine($"/// <summary>Lua table <c>{table.Name}</c>{(table.File is { } f ? $" (<c>{f}</c>)" : "")}.</summary>")
            .AppendLine($"public static partial class {name}")
            .AppendLine("{");
        var claimed = new HashSet<string>(StringComparer.Ordinal) { name };
        foreach (var (field, info) in table.StaticFields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (MemberName(field, claimed) is not { } property)
                continue;
            var csType = Map(info.Type).Nullable(info.Nullable);
            code.AppendLine($"    public static {csType} {property}")
                .AppendLine("    {")
                .AppendLine($"        get => global::Zomboid.Lua.LuaStatic.Get<{csType}>(\"{table.Name}\", \"{field}\");")
                .AppendLine($"        set => global::Zomboid.Lua.LuaStatic.Set(\"{table.Name}\", \"{field}\", value);")
                .AppendLine("    }");
        }
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        WriteStatics(code, table, table.StaticMethods, claimed, [], [], sharedNames: names);
        WriteStatics(code, table, table.Methods, claimed, [], [], colon: true, sharedNames: names);
        code.AppendLine("}").AppendLine();
    }

    private void WriteGlobals(StringBuilder code)
    {
        code.AppendLine("/// <summary>Global functions defined by vanilla's Lua (Java's are in <see cref=\"global::Zomboid.Java.Globals\"/>).</summary>")
            .AppendLine("public static partial class LuaGlobals")
            .AppendLine("{");
        var claimed = new HashSet<string>(StringComparer.Ordinal) { "LuaGlobals" };
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var function in _model.Functions.Values.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var parameters = Parameters(function.Params);
            var csName = MethodName(function.Name, parameters.Types, claimed, names);
            if (csName is null)
                continue;
            var returns = function.Return is null ? "void" : Map(function.Return).Nullable(function.ReturnNullable);
            var call = returns == "void"
                ? $"global::Zomboid.Lua.LuaStatic.CallGlobalVoid(\"{function.Name}\"{Prefix(parameters.Arguments)})"
                : $"global::Zomboid.Lua.LuaStatic.CallGlobal<{returns}>(\"{function.Name}\"{Prefix(parameters.Arguments)})";
            Summary(code, function.Notes);
            code.AppendLine($"    public static {returns} {csName}({parameters.Declaration}) => {call};");
        }
        code.AppendLine("}");
    }

    private Dictionary<string, Member> Members(LClass type)
    {
        var all = Base(type) is { } b ? new Dictionary<string, Member>(Members(b), StringComparer.Ordinal) : new Dictionary<string, Member>(StringComparer.Ordinal);
        if (_members.TryGetValue(type.Name, out var own))
        {
            foreach (var (key, member) in own)
                all[key] = member;
        }
        return all;
    }

    private (string Declaration, string Arguments, string Types) Parameters(List<LParam> parameters)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var declarations = new List<string>();
        var names = new List<string>();
        var types = new List<string>();
        // Only a run of nullable parameters at the end can be optional.
        var optionalFrom = parameters.Count;
        while (optionalFrom > 0 && parameters[optionalFrom - 1].Nullable && parameters[optionalFrom - 1].Name != "...")
            optionalFrom--;
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.Name == "...")
            {
                if (i != parameters.Count - 1)
                    continue;
                var rest = Unique("args", used);
                declarations.Add($"params object?[] {rest}");
                names.Add(".. " + rest);
                types.Add("object?[]");
                break;
            }
            var type = parameter.Type is null ? CsType.Object : Map(parameter.Type);
            var csType = type.IsDynamic ? "object?" : type.Nullable(parameter.Nullable);
            var name = Escape(Unique(Camel(Identifier(parameter.Name) ?? "arg"), used));
            declarations.Add(i >= optionalFrom ? $"{csType} {name} = default" : $"{csType} {name}");
            names.Add(name);
            // T? is part of a C# signature only for value types; reference nullability is an annotation.
            types.Add(type.IsValueType ? csType : csType.TrimEnd('?'));
        }
        // A spread needs the arguments as one collection: [a, b, .. args].
        var arguments = names.Any(n => n.StartsWith("..", StringComparison.Ordinal))
            ? $"[{string.Join(", ", names)}]"
            : string.Join(", ", names);
        return (string.Join(", ", declarations), arguments, string.Join(",", types));
    }

    private static string Prefix(string arguments) => arguments.Length == 0 ? "" : ", " + arguments;

    private CsType Map(string luaType) => MapUnion(TypeParser.Parse(luaType), new HashSet<string>(StringComparer.Ordinal));

    private CsType MapUnion(TypeNode node, HashSet<string> seenAliases)
    {
        var cases = node is TypeNode.Union union ? union.Cases : [node];
        var nullable = cases.Any(c => c is TypeNode.Name { Text: "nil" or "false" });
        var mapped = cases.Where(c => c is not TypeNode.Name { Text: "nil" or "false" })
            .Select(c => MapSingle(c, seenAliases)).Distinct().ToList();
        if (mapped.Any(m => m.Name == "double"))
            mapped.RemoveAll(m => m.Name == "int");
        CsType result;
        if (mapped.Count == 0)
            result = CsType.Dynamic;
        else if (mapped.Count == 1)
            result = mapped[0];
        else if (mapped.Count <= 5 && mapped.All(m => !m.IsDynamic && !m.Name.EndsWith('?')))
            result = new CsType($"global::Zomboid.Lua.OneOf<{string.Join(", ", mapped.Select(m => m.Name))}>", true);
        else
            result = CsType.Dynamic;
        return nullable && !result.IsDynamic && !result.Name.EndsWith('?') ? result with { Name = result.Name + "?", IsValueType = result.IsValueType } : result;
    }

    private CsType MapSingle(TypeNode node, HashSet<string> seenAliases)
    {
        switch (node)
        {
            case TypeNode.Union union:
                return MapUnion(union, seenAliases);
            case TypeNode.Array array:
                var element = MapUnion(array.Element, seenAliases);
                return new CsType($"global::Zomboid.Lua.LuaSequence<{Argument(element)}>", false);
            case TypeNode.Function:
                return CsType.Object;
            case TypeNode.Literal literal:
                return literal.IsString ? new CsType("string", false) : new CsType("double", true);
            case TypeNode.Name { Text: var text, Args: var args }:
                switch (text)
                {
                    case "number": return new CsType("double", true);
                    case "integer": return new CsType("int", true);
                    case "string": return new CsType("string", false);
                    case "boolean" or "true": return new CsType("bool", true);
                    case "table" when args.Count == 2:
                        var key = MapUnion(args[0], seenAliases);
                        var keyName = key.IsDynamic || key.Name.EndsWith('?') ? "object" : key.Name;
                        return new CsType($"global::Zomboid.Lua.LuaMap<{keyName}, {Argument(MapUnion(args[1], seenAliases))}>", false);
                    case "table" or "function" or "userdata" or "thread" or "metatable":
                        return new CsType("global::Wrench.GameObject", false);
                    case "unknown" or "any" or "nil" or "self":
                        return CsType.Dynamic;
                    case "ArrayList" or "List" or "Stack" or "LinkedList" when args.Count == 1:
                        return new CsType($"global::Zomboid.Java.JavaList<{Argument(MapUnion(args[0], seenAliases))}>", false);
                }
                if (_csNames.TryGetValue(text, out var lua))
                    return new CsType(lua, false);
                if (_model.Aliases.TryGetValue(text, out var alias) && seenAliases.Add(text))
                    return MapUnion(TypeParser.Parse(alias), seenAliases);
                if (_java.TryGetValue(text, out var java))
                    return new CsType(java, false);
                return CsType.Dynamic;
            default:
                return CsType.Dynamic;
        }
    }

    private static string Argument(CsType type) => type.IsDynamic ? "dynamic" : type.Name;

    private string? MemberName(string luaName, HashSet<string> claimed)
    {
        if (Identifier(luaName) is not { } identifier)
            return null;
        var name = Pascal(identifier);
        if (Reserved.Contains(name) || Keywords.Contains(name))
            name += "_";
        while (!claimed.Add(name))
            name += "_";
        return name;
    }

    // Overloads may share a name; anything else gets a suffix.
    private string? MethodName(string luaName, string types, HashSet<string> claimed, Dictionary<string, string> methods)
    {
        if (Identifier(luaName) is not { } identifier)
            return null;
        var name = Pascal(identifier);
        if (Reserved.Contains(name) || Keywords.Contains(name))
            name += "_";
        if (methods.TryGetValue(name, out var existing) && existing != types)
            return name;
        while (!claimed.Add(name))
            name += "_";
        methods[name] = types;
        return name;
    }

    private static string? Identifier(string luaName)
    {
        var trimmed = luaName.TrimStart('_');
        return Regex.IsMatch(trimmed, "^[A-Za-z][A-Za-z0-9_]*$") ? trimmed : null;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++)
            candidate = name + i;
        return candidate;
    }

    private static string Escape(string name) => Keywords.Contains(name) ? "@" + name : name;

    private static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static void Summary(StringBuilder code, string? notes)
    {
        if (Doc(notes, "") is { Length: > 0 } text)
            code.AppendLine($"    /// <summary>{text}</summary>");
    }

    private static string Doc(string? notes, string prefix) =>
        string.IsNullOrWhiteSpace(notes)
            ? ""
            : prefix + Regex.Replace(notes.Trim(), @"\s+", " ").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>Stubdata's EmmyLua type strings: <c>A | B</c>, <c>X[]</c>, <c>table&lt;K, V&gt;</c>, <c>fun(...): R</c>, literals.</summary>
abstract record TypeNode
{
    public sealed record Name(string Text, List<TypeNode> Args) : TypeNode;
    public sealed record Union(List<TypeNode> Cases) : TypeNode;
    public sealed record Array(TypeNode Element) : TypeNode;
    public sealed record Function : TypeNode;
    public sealed record Literal(bool IsString) : TypeNode;
}

sealed class TypeParser
{
    private readonly string _text;
    private int _pos;

    private TypeParser(string text) => _text = text;

    public static TypeNode Parse(string text)
    {
        try
        {
            var parser = new TypeParser(text);
            var node = parser.ParseUnion();
            return node;
        }
        catch (Exception)
        {
            return new TypeNode.Name("unknown", []);
        }
    }

    private TypeNode ParseUnion()
    {
        var cases = new List<TypeNode> { ParsePostfix() };
        while (Peek() == '|')
        {
            _pos++;
            cases.Add(ParsePostfix());
        }
        var flat = cases.SelectMany(c => c is TypeNode.Union u ? u.Cases : [c]).ToList();
        return flat.Count == 1 ? flat[0] : new TypeNode.Union(flat);
    }

    private TypeNode ParsePostfix()
    {
        var node = ParsePrimary();
        while (true)
        {
            SkipSpace();
            if (_pos + 1 < _text.Length && _text[_pos] == '[' && _text[_pos + 1] == ']')
            {
                _pos += 2;
                node = new TypeNode.Array(node);
            }
            else if (_pos < _text.Length && _text[_pos] == '?')
            {
                _pos++;
                node = new TypeNode.Union([node, new TypeNode.Name("nil", [])]);
            }
            else
            {
                return node;
            }
        }
    }

    private TypeNode ParsePrimary()
    {
        SkipSpace();
        var c = _text[_pos];
        if (c == '(')
        {
            _pos++;
            var inner = ParseUnion();
            Expect(')');
            return inner;
        }
        if (c is '"' or '\'')
        {
            var end = _text.IndexOf(c, _pos + 1);
            _pos = end + 1;
            return new TypeNode.Literal(true);
        }
        if (char.IsDigit(c) || c == '-')
        {
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] is '.' or '-'))
                _pos++;
            return new TypeNode.Literal(false);
        }
        if (c == '{')
        {
            SkipBalanced('{', '}');
            return new TypeNode.Name("table", []);
        }
        var start = _pos;
        while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] is '_' or '.'))
            _pos++;
        var name = _text[start.._pos];
        if (name.Length == 0)
            throw new FormatException(_text);
        if (name == "fun")
        {
            SkipSpace();
            if (Peek() == '(')
                SkipBalanced('(', ')');
            SkipSpace();
            // A return type list ends the function type: fun(): integer?, unknown?
            if (Peek() == ':')
            {
                _pos++;
                ParsePostfix();
                while (Peek() == ',')
                {
                    _pos++;
                    ParsePostfix();
                }
            }
            return new TypeNode.Function();
        }
        var args = new List<TypeNode>();
        SkipSpace();
        if (Peek() == '<')
        {
            _pos++;
            args.Add(ParseUnion());
            while (Peek() == ',')
            {
                _pos++;
                args.Add(ParseUnion());
            }
            Expect('>');
        }
        return new TypeNode.Name(name, args);
    }

    private void SkipBalanced(char open, char close)
    {
        var depth = 0;
        do
        {
            if (_text[_pos] == open)
                depth++;
            else if (_text[_pos] == close)
                depth--;
            _pos++;
        } while (depth > 0);
    }

    private char Peek()
    {
        SkipSpace();
        return _pos < _text.Length ? _text[_pos] : '\0';
    }

    private void Expect(char c)
    {
        if (Peek() != c)
            throw new FormatException(_text);
        _pos++;
    }

    private void SkipSpace()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            _pos++;
    }
}
