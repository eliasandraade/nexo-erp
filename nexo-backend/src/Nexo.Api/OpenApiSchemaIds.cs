namespace Nexo.Api;

/// <summary>
/// Swagger schema ids: the type name, as Swashbuckle does by default, but nested types are prefixed
/// with their declaring type so two requests with the same short name do not collide.
/// </summary>
public static class OpenApiSchemaIds
{
    public static string For(Type type)
    {
        var name = Simple(type);
        return type.IsNested && !type.IsGenericParameter ? $"{type.DeclaringType!.Name}.{name}" : name;
    }

    private static string Simple(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var root = type.Name[..type.Name.IndexOf('`')];
        return $"{root}Of{string.Join("And", type.GetGenericArguments().Select(For))}";
    }
}
