// The fields of an interval's qualifier, numbered as the protocol numbers
// them [PROTOCOL Data types].

namespace Turborel.Data.Types;

/// <summary>A field of an interval's qualifier.</summary>
public enum IntervalField
{
    /// <summary>YEAR, 0.</summary>
    Year = 0,

    /// <summary>MONTH, 1.</summary>
    Month = 1,

    /// <summary>DAY, 2.</summary>
    Day = 2,

    /// <summary>HOUR, 3.</summary>
    Hour = 3,

    /// <summary>MINUTE, 4.</summary>
    Minute = 4,

    /// <summary>SECOND, 5.</summary>
    Second = 5,
}
