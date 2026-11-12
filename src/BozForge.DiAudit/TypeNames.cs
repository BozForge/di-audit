using System;
using System.Linq;

namespace BozForge.DiAudit;

internal static class TypeNames
{
    /// <summary>Readable name: <c>IRepository&lt;Order&gt;</c> instead of <c>IRepository`1</c>.</summary>
    public static string Of(Type type)
    {
        if (!type.IsGenericType) return type.Name;

        var name = type.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name.Substring(0, tick);

        var args = type.IsGenericTypeDefinition
            ? new string(',', type.GetGenericArguments().Length - 1)
            : string.Join(", ", type.GetGenericArguments().Select(Of));
        return $"{name}<{args}>";
    }
}
