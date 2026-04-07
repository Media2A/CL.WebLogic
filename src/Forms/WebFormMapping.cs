using System.Collections.Concurrent;
using System.Reflection;
using CL.WebLogic.Runtime;

namespace CL.WebLogic.Forms;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class WebFormMapFromAttribute : Attribute
{
    public WebFormMapFromAttribute(string sourceName)
    {
        SourceName = sourceName;
    }

    public string SourceName { get; }
}

public static class WebFormMapper
{
    private static readonly ConcurrentDictionary<(Type Source, Type Target), PropertyMap[]> Maps = new();

    public static TTarget Map<TSource, TTarget>(TSource source)
        where TSource : class
        where TTarget : class, new()
    {
        ArgumentNullException.ThrowIfNull(source);

        var target = new TTarget();
        var maps = Maps.GetOrAdd((typeof(TSource), typeof(TTarget)), static key => BuildMap(key.Source, key.Target));
        foreach (var map in maps)
        {
            var value = map.Source.GetValue(source);
            if (value is null)
                continue;

            if (TryConvertValue(value, map.Target.PropertyType, out var converted))
            {
                map.Target.SetValue(target, converted);
            }
        }

        return target;
    }

    private static PropertyMap[] BuildMap(Type sourceType, Type targetType)
    {
        var sourceProperties = sourceType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.CanRead)
            .ToDictionary(static property => property.Name, StringComparer.OrdinalIgnoreCase);

        return targetType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.CanWrite)
            .Select(targetProperty =>
            {
                var sourceName = targetProperty.GetCustomAttribute<WebFormMapFromAttribute>()?.SourceName ?? targetProperty.Name;
                return sourceProperties.TryGetValue(sourceName, out var sourceProperty)
                    ? new PropertyMap(sourceProperty, targetProperty)
                    : null;
            })
            .Where(static map => map is not null)
            .Cast<PropertyMap>()
            .ToArray();
    }

    private static bool TryConvertValue(object value, Type destinationType, out object? converted)
    {
        if (destinationType.IsAssignableFrom(value.GetType()))
        {
            converted = value;
            return true;
        }

        if (value is WebUploadedFile uploadedFile)
        {
            if (destinationType == typeof(WebUploadedFile))
            {
                converted = uploadedFile;
                return true;
            }

            if (destinationType == typeof(string))
            {
                converted = uploadedFile.FileName;
                return true;
            }
        }

        var targetType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        try
        {
            if (targetType.IsEnum)
            {
                if (Enum.TryParse(targetType, value.ToString(), true, out var enumValue))
                {
                    converted = enumValue;
                    return true;
                }
            }

            converted = Convert.ChangeType(value, targetType);
            return true;
        }
        catch
        {
            converted = null;
            return false;
        }
    }

    private sealed record PropertyMap(PropertyInfo Source, PropertyInfo Target);
}
