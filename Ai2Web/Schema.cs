namespace Ai2Web;

/// <summary>Result of validating a value against a schema.</summary>
public sealed record SchemaResult(bool Valid, List<string> Errors);

/// <summary>Minimal JSON-Schema-subset validator for action input schemas. Port of
/// @ai2web/core validateSchema: pragmatic (object with typed/required properties, primitives,
/// arrays, enum) rather than the whole of JSON Schema. Used by Server to validate incoming
/// requests against an action's declared input_schema.</summary>
public static class Schema
{
    public static SchemaResult Validate(object? value, object? schema, string path = "input")
    {
        var errors = new List<string>();
        if (H.Map(schema) is not { Count: > 0 } sm)
            return new SchemaResult(true, errors);

        var declared = sm.GetValueOrDefault("type") as string;
        if (declared != null)
        {
            var ok = declared == "integer" ? IsInteger(value) : TypeOf(value) == declared;
            if (!ok)
            {
                errors.Add($"{path}: expected {declared}, got {TypeOf(value)}");
                return new SchemaResult(false, errors); // wrong base type: stop
            }
        }

        if (H.List(sm.GetValueOrDefault("enum")) is { } enumVals && !enumVals.Any(e => Equals(e, value)))
            errors.Add($"{path}: value is not one of the allowed options");

        if ((declared == "object" || (declared == null && TypeOf(value) == "object")) && H.Map(value) is { } obj)
        {
            foreach (var req in H.List(sm.GetValueOrDefault("required")) ?? new())
                if (req is string key && !obj.ContainsKey(key))
                    errors.Add($"{path}.{key}: required");

            foreach (var kv in H.Map(sm.GetValueOrDefault("properties")) ?? new())
                if (obj.TryGetValue(kv.Key, out var v))
                    errors.AddRange(Validate(v, kv.Value, $"{path}.{kv.Key}").Errors);
        }

        if ((declared == "array" || (declared == null && TypeOf(value) == "array"))
            && sm.GetValueOrDefault("items") is { } items && H.List(value) is { } arr)
        {
            for (var i = 0; i < arr.Count; i++)
                errors.AddRange(Validate(arr[i], items, $"{path}[{i}]").Errors);
        }

        return new SchemaResult(errors.Count == 0, errors);
    }

    private static string TypeOf(object? v) => v switch
    {
        null => "null",
        bool => "boolean",
        int or long or double or float => "number",
        string => "string",
        List<object?> => "array",
        Dictionary<string, object?> => "object",
        _ => "unknown",
    };

    private static bool IsInteger(object? v) => v switch
    {
        int or long => true,
        double d => d == Math.Truncate(d),
        float f => f == Math.Truncate(f),
        _ => false,
    };
}
