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

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public class GoogleSheetHelper
{
    public static async Task PushToGoogleSheet(Core core, TimePlanningPnDbContext dbContext, ILogger logger)
    {
        var privateKeyId = Environment.GetEnvironmentVariable("PRIVATE_KEY_ID");
        var googleSheetId = dbContext.PluginConfigurationValues
            .Single(x => x.Name == "TimePlanningBaseSettings:GoogleSheetId").Value;
        if (string.IsNullOrEmpty(privateKeyId))
        {
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

        // Authenticate using the dynamically constructed JSON
        var credential = GoogleCredential.FromJson(serviceAccountJson)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        var service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = applicationName
        });

        try
        {
            var headerRequest = service.Spreadsheets.Values.Get(googleSheetId, $"{sheetName}!A1:1");
            var headerResponse = await headerRequest.ExecuteAsync();
            var existingHeaders = headerResponse.Values?.FirstOrDefault() ?? new List<object>();

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

            var newHeaders = existingHeaders.Cast<string>().ToList();
            foreach (var siteName in siteNames)
            {
                var timerHeader = $"{siteName} - timer";
                var textHeader = $"{siteName} - tekst";
                if (!newHeaders.Contains(timerHeader))
                {
                    newHeaders.Add(timerHeader);
                }

                if (!newHeaders.Contains(textHeader))
                {
                    newHeaders.Add(textHeader);
                }
            }

            if (!existingHeaders.Cast<string>().SequenceEqual(newHeaders))
            {
                var updateRequest = new ValueRange
                {
                    Values = new List<IList<object>> { newHeaders.Cast<object>().ToList() }
                };

                var columnLetter = GetColumnLetter(newHeaders.Count);
                updateRequest = new ValueRange
                {
                    Values = new List<IList<object>> { newHeaders.Cast<object>().ToList() }
                };
                var updateHeaderRequest =
                    service.Spreadsheets.Values.Update(updateRequest, googleSheetId, $"{sheetName}!A1:{columnLetter}1");
                updateHeaderRequest.ValueInputOption =
                    SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
                await updateHeaderRequest.ExecuteAsync();

                logger.LogInformation("Headers updated successfully.");
            }

            AutoAdjustColumnWidths(service, googleSheetId, sheetName, logger);

            try
            {
                // ... existing code ...

                var sheet = service.Spreadsheets.Get(googleSheetId).Execute().Sheets
                    .FirstOrDefault(s => s.Properties.Title == sheetName);
                if (sheet == null) throw new Exception($"Sheet '{sheetName}' not found.");

                var sheetId = sheet.Properties.SheetId;

                // ... existing code ...

                SetAlternatingColumnColors(service, googleSheetId, sheetId!.Value, newHeaders.Count, logger);

                logger.LogInformation("Headers are already up-to-date.");
            }
            catch (Exception ex)
            {
                logger.LogError($"An error occurred: {ex.Message}");
            }

            logger.LogInformation("Headers are already up-to-date.");
        }
        catch (Exception ex)
        {
            logger.LogError($"An error occurred: {ex.Message}");
        }
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
            logger.LogError($"An error occurred while auto-adjusting column widths: {ex.Message}");
        }
    }

    private static string GetColumnLetter(int columnIndex)
    {
        string columnLetter = "";
        while (columnIndex > 0)
        {
            int modulo = (columnIndex - 1) % 26;
            columnLetter = Convert.ToChar(65 + modulo) + columnLetter;
            columnIndex = (columnIndex - modulo) / 26;
        }

        return columnLetter;
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