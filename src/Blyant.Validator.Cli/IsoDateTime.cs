using System.Globalization;

namespace Blyant.Validator.Cli;

/// <summary>
/// Times in Blyant Validator: the clock is read in one place, through a <see cref="TimeProvider"/> (so a test can give
/// another), in UTC; a time is made local time only where a person reads it (the time a settings file was written), as
/// the last step. Never DateTime.Now: a time kept in local time is ambiguous in the hour the clocks go back, and moves
/// when the time zone changes. Adapted from Blyant's IsoDateTime.cs (only what the validator uses).
/// </summary>
internal static class IsoDateTime
{
    /// <summary>The time now, in UTC.</summary>
    internal static DateTime UtcNow(TimeProvider? clock = null) => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>A time as a person reads it: in local time (a time in UTC is converted; a local one is as it is).</summary>
    private static DateTime Local(DateTime time) => time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;

    /// <summary>A time in local time, written in a format the same in all cultures ("yyyy-MM-dd HH:mm:ss").</summary>
    internal static string Local(DateTime time, string format) => Local(time).ToString(format, CultureInfo.InvariantCulture);

    /// <summary>
    /// The time now in local time, in a format the same in all cultures. The clock is read once: read for each part, a
    /// time written at midnight could have the day of one day and the month of the next
    /// </summary>
    internal static string LocalNow(string format, TimeProvider? clock = null) => Local(UtcNow(clock), format);
}
