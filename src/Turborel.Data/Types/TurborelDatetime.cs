// A datetime as the protocol carries it: whether it is zoned, its
// precision, the zone's displacement in minutes and the instant in UTC
// microseconds [PROTOCOL Values].

namespace Turborel.Data.Types;

/// <summary>A date, time or timestamp, its instant in UTC.</summary>
/// <param name="Moment">Whether it is a date, a time or a timestamp.</param>
/// <param name="UtcMicroseconds">The instant, microseconds in UTC.</param>
/// <param name="ZoneMinutes">
/// The zone's displacement in minutes: of a type WITH TIME ZONE its own,
/// else the one the value is shown in.
/// </param>
/// <param name="Zoned">Whether its type is WITH TIME ZONE.</param>
/// <param name="Precision">The digits of its fraction of a second.</param>
internal readonly record struct TurborelDatetime(
    DatetimeMoment Moment,
    long UtcMicroseconds,
    short ZoneMinutes,
    bool Zoned,
    byte Precision);
