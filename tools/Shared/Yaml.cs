using YamlDotNet.RepresentationModel;

/// <summary>Lookups over YamlDotNet's representation model (pulled in with <c>#:include</c>; needs the YamlDotNet package).</summary>
static class Y
{
    public static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode map && map.Children.TryGetValue(key, out var value) ? value : null;

    public static string? Text(YamlNode? node, string key) =>
        Child(node, key) is YamlScalarNode { Value: { Length: > 0 } value } ? value : null;
}
