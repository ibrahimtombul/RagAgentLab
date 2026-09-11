using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel;

namespace RagAgentLab.Tools;

/// <summary>
/// Business-day arithmetic exposed to the agent.
/// <para>
/// This tool exists to make multi-step reasoning visible in the demo: several HR rules are
/// expressed in working days ("requests must be filed at least 10 working days in advance"),
/// so answering a question like "when is the latest I can file?" forces the agent to first
/// look the rule up with the policy tool and then do calendar maths here. It also gives the
/// model a way to know today's date, which it otherwise cannot.
/// </para>
/// </summary>
public sealed class WorkdayTool
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Returns today's date.</summary>
    /// <returns>Today's date in <c>yyyy-MM-dd</c> format, with the weekday name.</returns>
    [KernelFunction("get_today")]
    [Description("Returns today's date. Use it whenever the question involves 'today', 'now', " +
                 "'this week' or any other relative date.")]
    public string GetToday() =>
        $"{DateTime.Today.ToString(DateFormat, CultureInfo.InvariantCulture)} ({DateTime.Today.DayOfWeek})";

    /// <summary>Adds or subtracts business days, skipping Saturdays and Sundays.</summary>
    /// <param name="startDate">Start date in <c>yyyy-MM-dd</c> format.</param>
    /// <param name="businessDays">Number of business days; negative values move backwards.</param>
    /// <returns>The resulting date, or a human-readable error.</returns>
    [KernelFunction("add_business_days")]
    [Description("Adds business days (weekends excluded) to a date and returns the resulting date. " +
                 "Pass a negative number to go backwards in time, for example to find the deadline " +
                 "for a request that must be filed a number of working days in advance. " +
                 "If the number of days comes from a company rule, look that rule up with " +
                 "search_hr_policy first instead of assuming a number.")]
    public string AddBusinessDays(
        [Description("Start date in yyyy-MM-dd format, for example '2026-10-15'.")] string startDate,
        [Description("Number of business days to add; negative to subtract.")] int businessDays)
    {
        if (!DateTime.TryParseExact(startDate, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return $"ERROR: '{startDate}' is not a date in yyyy-MM-dd format.";
        }

        if (Math.Abs(businessDays) > 3650)
        {
            return "ERROR: businessDays must be between -3650 and 3650.";
        }

        var step = businessDays >= 0 ? 1 : -1;
        var remaining = Math.Abs(businessDays);

        while (remaining > 0)
        {
            date = date.AddDays(step);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                remaining--;
            }
        }

        return $"{date.ToString(DateFormat, CultureInfo.InvariantCulture)} ({date.DayOfWeek})";
    }
}
