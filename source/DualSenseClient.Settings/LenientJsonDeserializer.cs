using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DualSenseClient.Settings;

/// <summary>
/// Provides error-tolerant JSON deserialization for settings objects.
/// Starts with default values, then overlays valid values from the JSON file.
/// Invalid or malformed values are kept as their defaults.
/// </summary>
public abstract class LenientJsonDeserializer
{
    /// <summary>
    /// Deserializes JSON by starting with default values and only applying valid JSON values.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize to. Must have a parameterless constructor.</typeparam>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use.</param>
    /// <returns>The deserialized object with invalid values kept as defaults.</returns>
    public static T Deserialize<T>(string json, JsonSerializerOptions options) where T : class, new()
    {
        T defaults = new T();

        try
        {
            JsonNode? rootNode = JsonNode.Parse(json);
            if (rootNode == null)
            {
                return defaults;
            }

            MergeValues(defaults, rootNode, typeof(T), options);
            return defaults;
        }
        catch
        {
            return defaults;
        }
    }

    /// <summary>
    /// Merges valid JSON values into the target object, skipping invalid entries.
    /// </summary>
    /// <param name="target">The target object to merge values into (pre-populated with defaults).</param>
    /// <param name="node">The JSON node to read values from.</param>
    /// <param name="targetType">The type of the target object.</param>
    /// <param name="options">The serializer options to use.</param>
    private static void MergeValues(object target, JsonNode node, Type targetType, JsonSerializerOptions options)
    {
        if (node is not JsonObject jsonObject)
        {
            return;
        }

        PropertyInfo[] properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (PropertyInfo property in properties)
        {
            if (!property.CanWrite)
            {
                continue;
            }

            string jsonName = GetJsonPropertyName(property);

            if (!jsonObject.TryGetPropertyValue(jsonName, out JsonNode? propertyNode) || propertyNode == null)
            {
                continue;
            }

            try
            {
                object? currentValue = property.GetValue(target);

                if (currentValue != null && propertyNode is JsonObject childObject && ShouldDeserializeAsObject(property.PropertyType))
                {
                    MergeValues(currentValue, childObject, property.PropertyType, options);
                }
                else
                {
                    object? newValue = TryDeserializeValue(propertyNode, property.PropertyType, options);
                    if (newValue != null)
                    {
                        property.SetValue(target, newValue);
                    }
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Attempts to deserialize a single JSON node value to the target type.
    /// Returns <c>null</c> if deserialization fails.
    /// </summary>
    /// <param name="node">The JSON node to deserialize.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="options">The serializer options to use.</param>
    /// <returns>The deserialized value, or <c>null</c> if it fails.</returns>
    private static object? TryDeserializeValue(JsonNode node, Type targetType, JsonSerializerOptions options)
    {
        try
        {
            string json = node.ToJsonString(options);
            return JsonSerializer.Deserialize(json, targetType, options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Determines whether a property type should be treated as a nested object to merge recursively.
    /// Returns <c>true</c> for types with a parameterless constructor that are not primitives, enums, or strings.
    /// </summary>
    /// <param name="type">The property type to check.</param>
    /// <returns><c>true</c> if the type should be recursively merged; otherwise, <c>false</c>.</returns>
    private static bool ShouldDeserializeAsObject(Type type)
    {
        return !type.IsPrimitive
               && !type.IsEnum
               && type != typeof(string)
               && Nullable.GetUnderlyingType(type) == null
               && type.GetConstructor(Type.EmptyTypes) != null;
    }

    /// <summary>
    /// Gets the JSON property name for a given property, respecting the <see cref="JsonPropertyNameAttribute"/>.
    /// </summary>
    /// <param name="property">The property to get the JSON name for.</param>
    /// <returns>The JSON property name, or the CLR property name if no attribute is present.</returns>
    private static string GetJsonPropertyName(PropertyInfo property)
    {
        JsonPropertyNameAttribute? attr = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? property.Name;
    }
}