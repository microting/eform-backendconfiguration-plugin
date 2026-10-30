#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using eFormCore;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microting.eForm.Infrastructure.Constants;
using Microting.TimePlanningBase.Infrastructure.Data;
using Sentry;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public class GoogleSheetHelper
{
    public static async Task PushToGoogleSheet(Core core, TimePlanningPnDbContext dbContext, ILogger logger, string? oldSiteName = null, string? newSiteName = null)
    {
        var privateKeyId = Environment.GetEnvironmentVariable("PRIVATE_KEY_ID");
        var googleSheetId = dbContext.PluginConfigurationValues
            .FirstOrDefault(x => x.Name == "TimePlanningBaseSettings:GoogleSheetId")?.Value;
        if (string.IsNullOrEmpty(googleSheetId))
        {
            // No sheet configured: this tenant does not plan from a Google Sheet.
            return;
        }

        // A configured sheet the pod has no key for used to be skipped without a
        // word, so the worker was saved and never got columns in the sheet.
        var missingCredentials = MissingCredentials(googleSheetId, privateKeyId);
        if (missingCredentials != null)
        {
            logger.LogWarning("PlanTimer sheet: {Problem}", missingCredentials);
            SentrySdk.CaptureMessage($"PlanTimer sheet: {missingCredentials}", SentryLevel.Warning);
            return;
        }

        var applicationName = "Google Sheets API Integration";
        var sheetName = "PlanTimer";

        //var core = await coreHelper.GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var privateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY"); // Replace with your private key
        var clientEmail = Environment.GetEnvironmentVariable("CLIENT_EMAIL"); // Replace with your client email
        var projectId = Environment.GetEnvironmentVariable("PROJECT_ID"); // Replace with your project ID
        var clientId = Environment.GetEnvironmentVariable("CLIENT_ID"); // Replace with your client ID

        // Construct the JSON for the service account credentials
        string serviceAccountJson = $@"
        {{
          ""type"": ""service_account"",
          ""project_id"": ""{projectId}"",
          ""private_key_id"": ""{privateKeyId}"",
          ""private_key"": ""{privateKey}"",
          ""client_email"": ""{clientEmail}"",
          ""client_id"": ""{clientId}"",
          ""auth_uri"": ""https://accounts.google.com/o/oauth2/auth"",
          ""token_uri"": ""https://oauth2.googleapis.com/token"",
          ""auth_provider_x509_cert_url"": ""https://www.googleapis.com/oauth2/v1/certs"",
          ""client_x509_cert_url"": ""https://www.googleapis.com/robot/v1/metadata/x509/{clientEmail}""
        }}";

        try
        {
            // Authenticate using the dynamically constructed JSON
#pragma warning disable CS0618 // Type or member is obsolete
            var serviceAccount = GoogleCredential.FromJson(serviceAccountJson)
                .CreateScoped(SheetsService.Scope.Spreadsheets);
#pragma warning restore CS0618 // Type or member is obsolete

            var service = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = serviceAccount,
                ApplicationName = applicationName
            });

            // The whole sheet, not just the header row: a column with a blank
            // header can still hold data, and new headers must not land on it.
            var sheetRequest = service.Spreadsheets.Values.Get(googleSheetId, sheetName);
            var sheetResponse = await sheetRequest.ExecuteAsync();
            var rows = sheetResponse.Values ?? new List<IList<object>>();
            var existingHeaders = rows.FirstOrDefault() ?? new List<object>();
            var dataRows = rows.Skip(1).ToList();

            var assignedSites = await dbContext.AssignedSites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.SiteId)
                .Distinct()
                .ToListAsync();

            var siteNames = await sdkDbContext.Sites
                .Where(x => assignedSites.Contains(x.MicrotingUid!.Value))
                .OrderBy(x => x.Name)
                .Select(x => x.Name)
                .ToListAsync();

            // A site being renamed keeps its OLD headers here; the rename block
            // below moves them over, so the planner must not treat the new name
            // as a site that still needs columns.
            var renamedKey = string.IsNullOrEmpty(oldSiteName) || string.IsNullOrEmpty(newSiteName)
                ? null
                : PlanTimerSheetColumns.NormalizeName(newSiteName);
            var plannedNames = siteNames
                .Select(x => renamedKey != null && PlanTimerSheetColumns.NormalizeName(x) == renamedKey
                    ? oldSiteName
                    : x)
                .ToList();

            // Matching a header by its exact text used to append a second column
            // whenever a header had been retyped with other spacing, a different
            // dash or other capitals. The planner matches the way the import
            // does, and only ever appends.
            var appends = PlanTimerSheetColumns.PlanAppends(existingHeaders, plannedNames, dataRows);
            foreach (var problem in appends.Problems)
            {
                logger.LogWarning("PlanTimer sheet: {Problem}", problem);
                SentrySdk.CaptureMessage($"PlanTimer sheet: {problem}", SentryLevel.Warning);
            }

            if (appends.Headers.Count > 0)
            {
                // Only the appended cells are written. Rewriting the whole row
                // would restate every existing header, so any header a human had
                // corrected would be silently reverted.
                var firstNewColumn = appends.FirstColumn;
                await EnsureColumnsAsync(service, googleSheetId, sheetName, firstNewColumn, appends.Headers.Count,
                    logger);
                var range = $"{sheetName}!" +
                            $"{PlanTimerSheetColumns.ColumnLetter(firstNewColumn)}1:" +
                            $"{PlanTimerSheetColumns.ColumnLetter(firstNewColumn + appends.Headers.Count - 1)}1";
                var updateRequest = new ValueRange
                {
                    Values = new List<IList<object>> { appends.Headers.Cast<object>().ToList() }
                };
                var updateHeaderRequest =
                    service.Spreadsheets.Values.Update(updateRequest, googleSheetId, range);
                updateHeaderRequest.ValueInputOption =
                    SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
                await updateHeaderRequest.ExecuteAsync();

                logger.LogInformation("Appended {Count} header(s) to the PlanTimer sheet at {Range}.",
                    appends.Headers.Count, range);

                // The new headers may sit in a gap inside the row, and the rename
                // below rewrites the row from existingHeaders: without them it
                // would blank the cells just written.
                while (existingHeaders.Count < firstNewColumn + appends.Headers.Count)
                {
                    existingHeaders.Add("");
                }

                for (var i = 0; i < appends.Headers.Count; i++)
                {
                    existingHeaders[firstNewColumn + i] = appends.Headers[i];
                }
            }

            // loop through all the existing headers and find the oldSiteName and rename it to newSiteName
            if (!string.IsNullOrEmpty(oldSiteName) && !string.IsNullOrEmpty(newSiteName) && newSiteName != oldSiteName)
            {
                for (int i = 0; i < existingHeaders.Count; i++)
                {
                    if (existingHeaders[i].ToString() == $"{oldSiteName} - timer")
                    {
                        existingHeaders[i] = $"{newSiteName} - timer";
                    }
                    else if (existingHeaders[i].ToString() == $"{oldSiteName} - tekst")
                    {
                        existingHeaders[i] = $"{newSiteName} - tekst";
                    }
                }
                var updateRequest = new ValueRange
                {
                    Values = new List<IList<object>> { existingHeaders }
                };
                var columnLetter = PlanTimerSheetColumns.ColumnLetter(existingHeaders.Count - 1);
                var updateHeaderRequest =
                    service.Spreadsheets.Values.Update(updateRequest, googleSheetId, $"{sheetName}!A1:{columnLetter}1");
                updateHeaderRequest.ValueInputOption =
                    SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
                await updateHeaderRequest.ExecuteAsync();
            }

            AutoAdjustColumnWidths(service, googleSheetId, sheetName, logger);
        }
        catch (Exception ex)
        {
            // The worker is saved either way, so this is the only trace of a
            // worker left without columns in the sheet: it must reach Sentry.
            logger.LogError(ex, "PlanTimer sheet: the worker headers could not be written: {Message}", ex.Message);
            SentrySdk.CaptureException(ex);
        }
    }

    /// <summary>
    /// Why a push to a configured sheet cannot run, or null when it can. A
    /// tenant without a sheet has nothing to push and is not a problem.
    /// </summary>
    public static string? MissingCredentials(string? googleSheetId, string? privateKeyId) =>
        !string.IsNullOrEmpty(googleSheetId) && string.IsNullOrEmpty(privateKeyId)
            ? "a Google Sheet is configured but PRIVATE_KEY_ID is not set, so no worker headers are written"
            : null;

    /// <summary>
    /// Grows the sheet's grid so the new headers fit; the Sheets API refuses a
    /// write beyond the last column of the grid.
    /// </summary>
    static async Task EnsureColumnsAsync(SheetsService service, string spreadsheetId, string sheetName,
        int firstColumn, int count, ILogger logger)
    {
        var spreadsheet = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();
        var sheet = spreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == sheetName)
                    ?? throw new InvalidOperationException($"Sheet '{sheetName}' not found.");
        var missing = PlanTimerSheetColumns.ColumnsToAdd(
            sheet.Properties.GridProperties?.ColumnCount ?? 0, firstColumn, count);
        if (missing == 0)
        {
            return;
        }

        var batchRequest = new BatchUpdateSpreadsheetRequest
        {
            Requests = new List<Request>
            {
                new()
                {
                    AppendDimension = new AppendDimensionRequest
                    {
                        SheetId = sheet.Properties.SheetId,
                        Dimension = "COLUMNS",
                        Length = missing
                    }
                }
            }
        };
        await service.Spreadsheets.BatchUpdate(batchRequest, spreadsheetId).ExecuteAsync();

        logger.LogInformation("Added {Count} column(s) to the PlanTimer sheet's grid.", missing);
    }

    static void AutoAdjustColumnWidths(SheetsService service, string spreadsheetId, string sheetName, ILogger logger)
    {
        try
        {
            var sheet = service.Spreadsheets.Get(spreadsheetId).Execute().Sheets
                .FirstOrDefault(s => s.Properties.Title == sheetName);
            if (sheet == null) throw new Exception($"Sheet '{sheetName}' not found.");

            var sheetId = sheet.Properties.SheetId;

            var autoResizeRequest = new Request
            {
                AutoResizeDimensions = new AutoResizeDimensionsRequest
                {
                    Dimensions = new DimensionRange
                    {
                        SheetId = sheetId,
                        Dimension = "COLUMNS",
                        StartIndex = 0, // Start from the first column
                        EndIndex = sheet.Properties.GridProperties.ColumnCount // Auto-adjust all columns
                    }
                }
            };

            var batchRequest = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request> { autoResizeRequest }
            };

            service.Spreadsheets.BatchUpdate(batchRequest, spreadsheetId).Execute();

            logger.LogInformation("Column widths auto-adjusted successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while auto-adjusting column widths: {Message}", ex.Message);
            SentrySdk.CaptureException(ex);
        }
    }

    static void SetAlternatingColumnColors(SheetsService service, string spreadsheetId, int sheetId, int columnCount,
        ILogger logger)
    {
        var requests = new List<Request>();

        for (int i = 3; i < columnCount; i += 2) // Start from column D (index 3) and increment by 2
        {
            var color1 = new Color { Red = 1, Green = 1, Blue = 1 };
            var color2 = new Color { Red = 0.9f, Green = 0.9f, Blue = 0.9f };

            var color = ((i / 2) % 2 == 0) ? color1 : color2;

            var updateCellsRequest1 = new Request
            {
                RepeatCell = new RepeatCellRequest
                {
                    Range = new GridRange
                    {
                        SheetId = sheetId,
                        StartColumnIndex = i,
                        EndColumnIndex = i + 1
                    },
                    Cell = new CellData
                    {
                        UserEnteredFormat = new CellFormat
                        {
                            BackgroundColor = color
                        }
                    },
                    Fields = "userEnteredFormat.backgroundColor"
                }
            };

            var updateCellsRequest2 = new Request
            {
                RepeatCell = new RepeatCellRequest
                {
                    Range = new GridRange
                    {
                        SheetId = sheetId,
                        StartColumnIndex = i + 1,
                        EndColumnIndex = i + 2
                    },
                    Cell = new CellData
                    {
                        UserEnteredFormat = new CellFormat
                        {
                            BackgroundColor = color
                        }
                    },
                    Fields = "userEnteredFormat.backgroundColor"
                }
            };

            requests.Add(updateCellsRequest1);
            requests.Add(updateCellsRequest2);
        }

        var batchUpdateRequest = new BatchUpdateSpreadsheetRequest
        {
            Requests = requests
        };

        service.Spreadsheets.BatchUpdate(batchUpdateRequest, spreadsheetId).Execute();

        logger.LogInformation("Alternating column colors set successfully.");
    }
}