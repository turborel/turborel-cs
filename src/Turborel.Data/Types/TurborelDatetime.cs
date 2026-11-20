// A datetime as the protocol carries it: whether it is zoned, its
// precision, the zone's displacement in minutes and the instant in UTC
// microseconds from 0001-01-01 00:00:00; a date's instant is its
// midnight's, a time's microseconds from midnight UTC, within a day
// [PROTOCOL Values].

namespace Turborel.Data.Types;

/// <summary>A date, time or timestamp, its instant in UTC.</summary>
/// <param name="Moment">Whether it is a date, a time or a timestamp.</param>
/// <param name="UtcMicroseconds">
/// The instant, microseconds in UTC from 0001-01-01 00:00:00: of a date its
/// midnight's, of a time microseconds from midnight UTC, within a day.
/// </param>
/// <param name="ZoneMinutes">
/// The zone's displacement in minutes: of a type WITH TIME ZONE its own,
/// else the one the value is shown in.
/// </param>
/// <param name="Zoned">Whether its type is WITH TIME ZONE.</param>
/// <param name="Precision">The digits of its fraction of a second.</param>
public readonly record struct TurborelDatetime(
    DatetimeMoment Moment,
    long UtcMicroseconds,
    short ZoneMinutes,
    bool Zoned,
    byte Precision)
{
    /// <summary>Shows it as turborel does: its date and time in its displacement, its fraction to its precision, and a zoned one's displacement.</summary>
    /// <returns>The text, as <c>2024-01-02 10:11:12.5</c>.</returns>
    public override string ToString() => SqlText.Datetime(this);
}
