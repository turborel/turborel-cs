// An SQL/JSON item, as a JSON value's stored form holds it: the null, a
// truth value, a number of one of three kinds, a string, a datetime, an
// array or an object (turborel's RULES.md D89).

using System.Collections.Generic;
using Turborel.Data.Types;

namespace Turborel.Data.Json;

/// <summary>An SQL/JSON item.</summary>
internal abstract record JsonItem
{
    /// <summary>The SQL/JSON null.</summary>
    internal sealed record NullItem : JsonItem;

    /// <summary>A truth value.</summary>
    /// <param name="Value">The value.</param>
    internal sealed record BooleanItem(bool Value) : JsonItem;

    /// <summary>An exact number.</summary>
    /// <param name="Value">The number.</param>
    internal sealed record ExactItem(TurborelDecimal Value) : JsonItem;

    /// <summary>A decimal floating-point number.</summary>
    /// <param name="Value">The number.</param>
    internal sealed record DecFloatItem(TurborelDecFloat Value) : JsonItem;

    /// <summary>An approximate number.</summary>
    /// <param name="Value">The number.</param>
    internal sealed record ApproximateItem(double Value) : JsonItem;

    /// <summary>A character string.</summary>
    /// <param name="Value">The string.</param>
    internal sealed record StringItem(string Value) : JsonItem;

    /// <summary>A datetime, which no JSON text holds.</summary>
    /// <param name="Value">The datetime.</param>
    internal sealed record DatetimeItem(TurborelDatetime Value) : JsonItem;

    /// <summary>An array.</summary>
    /// <param name="Elements">Its elements, in order.</param>
    internal sealed record ArrayItem(IReadOnlyList<JsonItem> Elements) : JsonItem;

    /// <summary>An object, its members in the order written, those of one key all.</summary>
    /// <param name="Members">Its members.</param>
    internal sealed record ObjectItem(IReadOnlyList<JsonMember> Members) : JsonItem;
}
