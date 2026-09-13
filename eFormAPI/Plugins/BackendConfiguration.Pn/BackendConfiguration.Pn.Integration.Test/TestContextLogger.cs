using System.Text;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// #1253 — the integration-test replacement for <c>NullLogger</c>.
///
/// Every fixture used to hand the services under test a <c>NullLogger</c>, so the
/// exception a service caught and logged was discarded and a CI failure showed only
/// the localized error key (<c>ErrorWhileUpdatingTask</c>, …). This writes instead to
/// <c>TestContext.Out</c> — the per-test writer, which is the correct sink
/// under the <c>[Parallelizable(ParallelScope.Fixtures)]</c> this project puts on
/// ~30 fixtures; <c>Console.Out</c> can be misattributed across parallel fixtures.
///
/// Two deliberate properties:
/// <list type="bullet">
/// <item><description><c>IsEnabled</c> returns <c>true</c> for every level
/// including <c>Trace</c>, because the catch blocks send the stack trace through
/// <c>LogTrace</c>. (Verified there are no <c>IsEnabled(</c> guards in
/// <c>BackendConfiguration.Pn</c>, so this cannot switch on a guarded
/// expensive-computation branch.)</description></item>
/// <item><description>The exception is rendered with <c>ToString()</c>, which unwinds
/// <c>InnerException</c> — the whole point: an EF <c>DbUpdateException</c> says only
/// "See the inner exception for details", while the <c>MySqlException</c> naming the
/// violated constraint lives inside.</description></item>
/// </list>
///
/// Note the sink is only visible in CI under <em>failed</em> tests: the workflow runs
/// classic VSTest (<c>dotnet test -v n</c>), which prints the
/// <c>Standard Output Messages:</c> block for failures only. That is the case this
/// exists for; it is a failure-diagnosis aid, not general tracing.
/// </summary>
internal static class TestContextLoggerSink
{
    /// <summary>
    /// Formats one entry and writes it to the current test's output. Every step is
    /// wrapped: a logger must never be the reason a test fails, so a formatter that
    /// throws or a missing test context is swallowed rather than propagated into the
    /// catch block that called it.
    /// </summary>
    internal static void Write<TState>(
        string? category,
        LogLevel logLevel,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string>? formatter)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append('[').Append(logLevel).Append(']');

            if (!string.IsNullOrEmpty(category))
            {
                builder.Append(' ').Append(category);
            }

            string? message = null;
            try
            {
                message = formatter is not null
                    ? formatter(state, exception)
                    : state?.ToString();
            }
            catch (Exception formatterFailure)
            {
                message = $"<log message formatter threw: {formatterFailure.GetType().Name}>";
            }

            if (!string.IsNullOrEmpty(message))
            {
                builder.Append(' ').Append(message);
            }

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            TestContext.Out.WriteLine(builder.ToString());
        }
        catch
        {
            // Intentionally swallowed — see the summary above.
        }
    }
}

/// <inheritdoc cref="TestContextLoggerSink"/>
internal sealed class TestContextLogger : ILogger
{
    /// <summary>Drop-in for <c>NullLogger.Instance</c>.</summary>
    public static TestContextLogger Instance { get; } = new();

    private TestContextLogger()
    {
    }

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => TestContextLoggerSink.Write(category: null, logLevel, state, exception, formatter);
}

/// <inheritdoc cref="TestContextLoggerSink"/>
/// <typeparam name="T">The service the log entries are attributed to.</typeparam>
internal sealed class TestContextLogger<T> : ILogger<T>
{
    /// <summary>Drop-in for <c>NullLogger&lt;T&gt;.Instance</c>.</summary>
    public static TestContextLogger<T> Instance { get; } = new();

    private static readonly string Category = typeof(T).Name;

    private TestContextLogger()
    {
    }

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => TestContextLoggerSink.Write(Category, logLevel, state, exception, formatter);
}
