using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BackendConfiguration.Pn.Services.BackendConfigurationComplianceExportService;

/// <summary>
/// docx → PDF via LibreOffice, server-side (#1160 decision 4).
///
/// <para>
/// <b>Why this does not simply call <c>ReportHelper.ConvertToPdf</c>
/// (<c>eform-sdk ReportHelper.cs:260-284</c>).</b> #1169's pitfall list requires a
/// timeout "at the call site rather than editing the SDK", and that method offers
/// no call site to put one at: it blocks in <c>WaitForExit()</c> with no overload,
/// no cancellation and no kill, so a wedged <c>soffice</c> holds the request thread
/// for the lifetime of the process. A compliance report is the plausible first
/// caller to make that visible. The SDK method's shape is reproduced here — same
/// binary, same <c>--headless --convert-to pdf --outdir</c> arguments — with three
/// fixes:
/// </para>
/// <list type="number">
///   <item><b>A timeout</b> (<see cref="ConversionTimeout"/>), after which the whole process tree is killed and the export fails cleanly instead of hanging.</item>
///   <item><b>No string interpolation into a command line.</b> Arguments go through <c>ProcessStartInfo.ArgumentList</c>, so a temp path containing a space or a quote cannot alter the invocation. The SDK builds one interpolated <c>Arguments</c> string.</item>
///   <item><b>Deterministic output naming.</b> The SDK's caller derives the PDF path with an UNANCHORED <c>.Replace("docx", "pdf")</c> (<c>BackendConfigurationReportService.cs:1052</c>), which corrupts any path containing the substring "docx". Here the docx is written into a private per-export directory under a GUID name and the sibling <c>.pdf</c> is located by extension.</item>
///   <item><b>A private LibreOffice user profile per invocation</b> (<c>-env:UserInstallation=</c>). Two concurrent headless <c>soffice</c> runs sharing the DEFAULT profile is the classic LibreOffice failure: the second either blocks on the profile lock or exits 0 having produced nothing, and the tolerant output scan below then finds no PDF and returns <c>null</c> → a 400 for a request that was perfectly valid. The export endpoint is bare <c>[Authorize]</c> with no claim gating, so two users pressing "Download PDF" at the same moment — or an export overlapping the existing <c>GET report/reports/file</c> PDF path — is ordinary traffic, not an edge case. The profile lives inside the per-export temp directory and is deleted with it.</item>
/// </list>
///
/// <para>
/// <b>Temp files are cleaned up.</b> Every existing generator writes into
/// <c>Path.GetTempPath()/results</c> and leaves the file there forever. This one
/// owns a per-export subdirectory and deletes it in a <c>finally</c>, whatever
/// happens — the PDF bytes are read into memory first, so nothing on disk has to
/// outlive the call.
/// </para>
///
/// <para>
/// <b>When <c>soffice</c> is unavailable</b> — not installed, not on <c>PATH</c>,
/// or wedged past the timeout — this returns <c>null</c> after logging. The
/// calling service turns that into a failed <c>OperationDataResult</c> carrying
/// the existing <c>ErrorWhileGeneratingReportFile</c> message, so the user gets a
/// 400 with an explanation rather than a truncated or empty download. CSV and
/// XLSX are unaffected: neither shells out.
/// </para>
///
/// <para>
/// <b>A CLIENT ABORT is not a failure.</b> The timeout is a token linked to the
/// request's own <see cref="CancellationToken"/>, so a user who navigates away
/// mid-download cancels the same wait a wedged converter would. The two are told
/// apart by <c>cancellationToken.IsCancellationRequested</c>: a real timeout is
/// logged as an error and returns <c>null</c>, whereas a client abort is logged at
/// Information and the <see cref="OperationCanceledException"/> is allowed to
/// PROPAGATE — past the calling service's catch-all, which would otherwise send a
/// routine navigation to Sentry as an application error.
/// </para>
/// </summary>
public static class ComplianceExportPdfConverter
{
    /// <summary>
    /// Ceiling on one <c>soffice</c> invocation. Generous, because the documents
    /// this converts are genuinely large (a completed-work quarter is on the order
    /// of a hundred A4 sheets, more with an image appendix), but finite — the point
    /// is that a wedged converter cannot hold a request thread forever.
    /// </summary>
    public static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long the killed process's stdout/stderr are given to finish draining
    /// before the diagnostics are given up on. Short: the process is already dead
    /// by the time this is used, so the reads normally complete immediately.
    /// </summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Converts a docx stream to PDF bytes, or returns <c>null</c> when the
    /// conversion could not be performed. Never throws for an environment problem;
    /// only a genuinely unexpected failure propagates — plus
    /// <see cref="OperationCanceledException"/> when the CALLER's token is
    /// cancelled, which is deliberate (see the type comment).
    /// </summary>
    public static async Task<byte[]> ConvertAsync(
        Stream docxStream, ILogger logger, CancellationToken cancellationToken = default)
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(), "results", $"compliance-export-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(workingDirectory);

            // A private LibreOffice user installation for THIS invocation only, so
            // two concurrent exports cannot contend on one profile lock. It has to
            // exist before soffice runs; it lives under the per-export directory, so
            // the finally below removes it with everything else.
            var profileDirectory = Path.Combine(workingDirectory, "lo-profile");
            Directory.CreateDirectory(profileDirectory);

            var docxPath = Path.Combine(workingDirectory, "export.docx");

            await using (var fileStream = File.Create(docxPath))
            {
                if (docxStream.CanSeek) docxStream.Seek(0, SeekOrigin.Begin);
                await docxStream.CopyToAsync(fileStream, cancellationToken);
            }

            if (!await RunSofficeAsync(
                    docxPath, workingDirectory, profileDirectory, logger, cancellationToken))
            {
                return null;
            }

            var pdfPath = Path.Combine(workingDirectory, "export.pdf");
            if (!File.Exists(pdfPath))
            {
                // soffice names the output after the input's stem, but be tolerant:
                // any single .pdf in a directory we own is the one we asked for.
                var produced = Directory.GetFiles(workingDirectory, "*.pdf");
                if (produced.Length == 0)
                {
                    logger.LogError(
                        "ComplianceExportPdfConverter: soffice exited without producing a PDF in {Directory}.",
                        workingDirectory);
                    return null;
                }

                pdfPath = produced[0];
            }

            return await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        }
        finally
        {
            TryDelete(workingDirectory, logger);
        }
    }

    /// <summary>
    /// The <c>file://</c> URL form LibreOffice's <c>-env:UserInstallation</c>
    /// expects. <c>Uri.AbsoluteUri</c> is used rather than string concatenation so
    /// the three slashes and any escaping (a temp path containing a space) are the
    /// framework's problem: <c>/tmp/results/compliance-export-…/lo-profile</c>
    /// becomes <c>file:///tmp/results/compliance-export-…/lo-profile</c>.
    /// </summary>
    public static string ToFileUrl(string path) => new Uri(path).AbsoluteUri;

    private static async Task<bool> RunSofficeAsync(
        string docxPath, string outputDirectory, string profileDirectory,
        ILogger logger, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = "soffice";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        // ArgumentList, never an interpolated Arguments string: the working
        // directory is a path we built, but it still travels through a shell-free
        // exec where quoting rules differ per platform.
        // MUST come before the other switches: soffice reads -env: bootstrap
        // arguments while starting up.
        process.StartInfo.ArgumentList.Add($"-env:UserInstallation={ToFileUrl(profileDirectory)}");
        process.StartInfo.ArgumentList.Add("--headless");
        process.StartInfo.ArgumentList.Add("--convert-to");
        process.StartInfo.ArgumentList.Add("pdf");
        process.StartInfo.ArgumentList.Add("--outdir");
        process.StartInfo.ArgumentList.Add(outputDirectory);
        process.StartInfo.ArgumentList.Add(docxPath);

        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            // The "soffice is not installed" path — by far the most likely
            // environment failure, and the one CI hits.
            logger.LogError(e,
                "ComplianceExportPdfConverter: could not start 'soffice'. LibreOffice is required for "
                + "server-side PDF export; CSV and Excel export are unaffected.");
            return false;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ConversionTimeout);

        // Drain both pipes concurrently with the wait: soffice writes to stdout,
        // and a full pipe buffer deadlocks a process that nobody is reading from.
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            // Kill first: that closes the pipes, so the two reads below complete
            // instead of being abandoned. Abandoning them would dispose the readers
            // out from under pending reads (an unobserved exception) AND throw away
            // the only diagnostics explaining why soffice wedged.
            TryKill(process, logger);
            var (killedOut, killedErr) = await DrainAsync(stdOutTask, stdErrTask);

            if (cancellationToken.IsCancellationRequested)
            {
                // The CLIENT went away — a user navigating away mid-download. This
                // is not a converter failure, it is not an error, and it must not
                // reach Sentry, so it is logged quietly and the cancellation is
                // allowed to propagate to the caller.
                logger.LogInformation(
                    "ComplianceExportPdfConverter: the request was cancelled by the client; "
                    + "soffice was killed. stdout={StdOut} stderr={StdErr}",
                    killedOut, killedErr);
                throw;
            }

            logger.LogError(
                "ComplianceExportPdfConverter: soffice did not finish within {Timeout}; killed it. "
                + "stdout={StdOut} stderr={StdErr}",
                ConversionTimeout, killedOut, killedErr);
            return false;
        }

        // BOTH reads are awaited together. Awaiting them one after the other means
        // that if the first faults the second is never awaited at all, and an
        // abandoned faulted read resurfaces as an UnobservedTaskException on the
        // finalizer thread — the same hole the kill path's DrainAsync closes, but
        // on the SUCCESS path.
        await Task.WhenAll(stdOutTask, stdErrTask);
        var stdOut = stdOutTask.Result;
        var stdErr = stdErrTask.Result;

        if (process.ExitCode == 0) return true;

        logger.LogError(
            "ComplianceExportPdfConverter: soffice exited with {ExitCode}. stdout={StdOut} stderr={StdErr}",
            process.ExitCode, stdOut, stdErr);
        return false;
    }

    /// <summary>
    /// Bounded read of whatever the two pipe readers already captured. The wait is
    /// short and deliberately not the request's token: this runs on the way out of
    /// a kill, and blocking there would defeat the point of the kill.
    /// </summary>
    private static async Task<(string StdOut, string StdErr)> DrainAsync(
        Task<string> stdOutTask, Task<string> stdErrTask)
    {
        var both = Task.WhenAll(stdOutTask, stdErrTask);

        // If the drain times out we stop waiting on `both`, so its fault would
        // otherwise never be observed and would surface later as an
        // UnobservedTaskException on the finalizer thread. This observes it
        // whenever it eventually completes, and observing WhenAll observes the two
        // reads underneath it.
        _ = both.ContinueWith(
            t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await Task.WhenAny(both, Task.Delay(DrainTimeout));

        return (
            stdOutTask.IsCompletedSuccessfully ? stdOutTask.Result : string.Empty,
            stdErrTask.IsCompletedSuccessfully ? stdErrTask.Result : string.Empty);
    }

    private static void TryKill(Process process, ILogger logger)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "ComplianceExportPdfConverter: could not kill the soffice process.");
        }
    }

    private static void TryDelete(string directory, ILogger logger)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception e)
        {
            logger.LogWarning(e,
                "ComplianceExportPdfConverter: could not clean up {Directory}.", directory);
        }
    }
}
