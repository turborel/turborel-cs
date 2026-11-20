// The three kinds of datetime a value is [PROTOCOL Values].

namespace Turborel.Data.Types;

/// <summary>Whether a datetime is a date, a time or a timestamp.</summary>
public enum DatetimeMoment
{
    /// <summary>A date, value tag 8.</summary>
    Date,

    /// <summary>A time, value tag 9.</summary>
    Time,

    /// <summary>A timestamp, value tag 11.</summary>
    Timestamp,
}
