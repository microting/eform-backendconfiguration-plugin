using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Messages;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.WordService;
using eFormCore;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Helpers;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Rebus.Handlers;
using File = System.IO.File;
using KeyValuePair = Microting.eForm.Dto.KeyValuePair;

namespace BackendConfiguration.Pn.Handlers;

public class WorkOrderCreatedHandler(
    BackendConfigurationDbContextHelper backendConfigurationDbContextHelper,
    ChemicalDbContextHelper chemicalDbContextHelper,
    Core sdkCore,
    IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
    : IHandleMessages<WorkOrderCreated>
{
    private readonly ChemicalDbContextHelper _chemicalDbContextHelper = chemicalDbContextHelper;

    public async Task Handle(WorkOrderCreated message)
    {
        // var pdfHash = await GeneratePdf(message.PicturesOfTasks).ConfigureAwait(false);
        await DeployEform(
            message.PropertyWorkers,
            message.EformId,
            message.FolderId,
            message.Description,
            CaseStatusesEnum.Ongoing,
            message.WorkorderCaseId,
            message.NewDescription,
            message.DeviceUsersGroupId,
            // pdfHash,
            message.AssignedToSite,
            message.PushMessageBody,
            message.PushMessageTitle,
            message.AreaName,
            message.CreatedByUserId,
            message.PropertyName,
            message.FolderIdForOngoingTasks,
            message.FolderIdForTasks,
            message.FolderIdForCompletedTasks,
            message.HasImages,
            message.PicturesOfTasksList).ConfigureAwait(false);
    }

    private async Task DeployEform(
        List<KeyValuePair<int, int>> propertyWorkers,
        int eformId,
        int folderId,
        string description,
        CaseStatusesEnum status,
        int workorderCaseId,
        string newDescription,
        int? deviceUsersGroupId,
        // string pdfHash,
        Site assignedToSite,
        string pushMessageBody,
        string pushMessageTitle,
        string areaNameb,
        int createdByUserId,
        string propertyName,
        int folderIdForOngoingTasks,
        int folderIdForTasks,
        int folderIdForCompletedTasks,
        bool hasImages,
        List<KeyValuePair<string, string>> picturesOfTasks
        )
    {
        var backendConfigurationPnDbContext = backendConfigurationDbContextHelper.GetDbContext();
        var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

        var workOrderCase = await backendConfigurationPnDbContext.WorkorderCases.FirstAsync(x => x.Id == workorderCaseId);
        DateTime startDate = new DateTime(2022, 12, 5);
        var displayOrder = (int)(DateTime.UtcNow - startDate).TotalSeconds;

        foreach (var propertyWorker in propertyWorkers)
        {
            var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.Value).ConfigureAwait(false);
            var unit = await sdkDbContext.Units.FirstAsync(x => x.SiteId == site.Id);
            var siteLanguage = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(siteLanguage.LanguageCode);
            var priorityText = "";

            switch (workOrderCase.Priority)
            {
                case "1":
                    displayOrder = 100_000_000 + displayOrder;
                    priorityText = $"<strong>{backendConfigurationLocalizationService.GetString("Priority")}:</strong> {backendConfigurationLocalizationService.GetString("Urgent")}<br>";
                    break;
                case "2":
                    displayOrder = 200_000_000 + displayOrder;
                    priorityText = $"<strong>{backendConfigurationLocalizationService.GetString("Priority")}:</strong> {backendConfigurationLocalizationService.GetString("High")}<br>";
                    break;
                case "3":
                    displayOrder = 300_000_000 + displayOrder;
                    priorityText = $"<strong>{backendConfigurationLocalizationService.GetString("Priority")}:</strong> {backendConfigurationLocalizationService.GetString("Medium")}<br>";
                    break;
                case "4":
                    displayOrder = 400_000_000 + displayOrder;
                    priorityText = $"<strong>{backendConfigurationLocalizationService.GetString("Priority")}:</strong> {backendConfigurationLocalizationService.GetString("Low")}<br>";
                    break;
            }

            var textStatus = workOrderCase.CaseStatusesEnum switch
            {
                CaseStatusesEnum.Ongoing => backendConfigurationLocalizationService.GetString("Ongoing"),
                CaseStatusesEnum.Completed => backendConfigurationLocalizationService.GetString("Completed"),
                CaseStatusesEnum.Awaiting => backendConfigurationLocalizationService.GetString("Awaiting"),
                CaseStatusesEnum.Ordered => backendConfigurationLocalizationService.GetString("Ordered"),
                _ => ""
            };

            var assignedTo = site.Name == assignedToSite.Name ? "" : $"<strong>{backendConfigurationLocalizationService.GetString("AssignedTo")}:</strong> {assignedToSite.Name}<br>";

            var areaName = !string.IsNullOrEmpty(workOrderCase.SelectedAreaName)
                ? $"<strong>{backendConfigurationLocalizationService.GetString("Area")}:</strong> {workOrderCase.SelectedAreaName}<br>"
                : "";

            var outerDescription = $"<strong>{backendConfigurationLocalizationService.GetString("Location")}:</strong> {propertyName}<br>" +
                                   areaName +
                                   $"<strong>{backendConfigurationLocalizationService.GetString("Description")}:</strong> {newDescription}<br>" +
                                   priorityText +
                                   $"<strong>{backendConfigurationLocalizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByName}<br>" +
                                   (!string.IsNullOrEmpty(workOrderCase.CreatedByText)
                                       ? $"<strong>{backendConfigurationLocalizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByText}<br>"
                                       : "") +
                                   assignedTo +
                                   $"<strong>{backendConfigurationLocalizationService.GetString("Status")}:</strong> {textStatus}<br><br>";

            var mainElement = await sdkCore.ReadeForm(eformId, siteLanguage).ConfigureAwait(false);
            // mainElement.CheckListFolderName = await sdkDbContext.Folders
            //     .Where(x => x.Id == folderId)
            //     .Select(x => x.MicrotingUid.ToString())
            //     .FirstOrDefaultAsync().ConfigureAwait(false);
            mainElement.Label = " ";
            mainElement.ElementList[0].QuickSyncEnabled = true;
            mainElement.EnableQuickSync = true;
            mainElement.ElementList[0].Label = " ";
            mainElement.ElementList[0].Description.InderValue = outerDescription.Replace("\n", "<br>");
            mainElement.DisplayOrder = displayOrder; // Lowest value is the top of the list
            if (site.Name == assignedToSite.Name)
            {
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == (workOrderCase.Priority != "1" ? folderIdForOngoingTasks : folderIdForTasks))
                    .MicrotingUid.ToString();
                folderId = folderIdForOngoingTasks;
                mainElement.PushMessageTitle = pushMessageTitle;
                mainElement.PushMessageBody = pushMessageBody;
                mainElement.BadgeCountEnabled = true;
            }
            else
            {
                folderId = folderIdForCompletedTasks;
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == folderIdForCompletedTasks)
                    .MicrotingUid.ToString();
            }
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description.Replace("\r\n", "<br>").Replace("\n", "<br>");
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Color = Constants.FieldColors.Yellow;
            // ((ShowPdf)((DataElement)mainElement.ElementList[0]).DataItemList[1]).Value = pdfHash;
            List<Microting.eForm.Dto.KeyValuePair> kvpList = ((SingleSelect) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).KeyValuePairList;
            var newKvpList = new List<KeyValuePair>();
            foreach (var keyValuePair in kvpList)
            {
                if (keyValuePair.Key == workOrderCase.Priority)
                {
                    keyValuePair.Selected = true;
                }
                newKvpList.Add(keyValuePair);
            }
            ((SingleSelect) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).KeyValuePairList = newKvpList;

            if (deviceUsersGroupId != null)
            {
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Source = (int)deviceUsersGroupId;
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Mandatory = true;
                ((Comment)((DataElement)mainElement.ElementList[0]).DataItemList[3]).Value = newDescription;
                ((SingleSelect)((DataElement)mainElement.ElementList[0]).DataItemList[6]).Mandatory = true;
                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.Repeated = 1;
            }
            else
            {
                mainElement.EndDate = DateTime.Now.AddDays(30).ToUniversalTime();
                mainElement.ElementList[0].DoneButtonEnabled = false;
                mainElement.Repeated = 1;
            }

            mainElement.StartDate = DateTime.Now.ToUniversalTime();
            // if (hasImages == false)
            // {
            ((DataElement) mainElement.ElementList[0]).DataItemList.RemoveAt(1);
            // }
            // unit.eFormVersion ??= "1.0.0";
            // if (int.Parse(unit.eFormVersion.Replace(".","")) > 3212)
            // {
            if (hasImages)
            {
                // ((DataElement) mainElement.ElementList[0]).DataItemList.RemoveAt(1);
                // add a new show picture element for each picture in the picturesOfTasks list
                int j = 0;
                foreach (var picture in picturesOfTasks)
                {
                    var showPicture = new ShowPicture(j, false, false, "", "", "", 0, false, "");
                    var storageResult = sdkCore.GetFileFromS3Storage(picture.Key).GetAwaiter().GetResult();

                    await sdkCore.PngUpload(storageResult.ResponseStream, picture.Value, picture.Key);
                    showPicture.Value = picture.Value;
                    ((DataElement) mainElement.ElementList[0]).DataItemList.Add(showPicture);
                    j++;
                }
            }
            // }
            var caseId = await sdkCore.CaseCreate(mainElement, "", (int)site.MicrotingUid!, folderId).ConfigureAwait(false);
            var newWorkOrderCase = new WorkorderCase
            {
                CaseId = (int)caseId!,
                PropertyWorkerId = propertyWorker.Key,
                CaseStatusesEnum = status,
                ParentWorkorderCaseId = workorderCaseId,
                SelectedAreaName = workOrderCase.SelectedAreaName,
                CreatedByName = workOrderCase.CreatedByName,
                CreatedByText = workOrderCase.CreatedByText,
                Description = newDescription,
                CaseInitiated = DateTime.UtcNow,
                LastAssignedToName = site.Name,
                AssignedToSdkSiteId = site.Id,
                LastUpdatedByName = "",
                LeadingCase = false,
                Priority = workOrderCase.Priority,
                CreatedByUserId = createdByUserId,
                UpdatedByUserId = createdByUserId
            };
            await newWorkOrderCase.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
        }
    }

    private async Task<string> InsertImage(string imageName, string itemsHtml, int imageSize, int imageWidth,
        string basePicturePath)
    {
        // var filePath = Path.Combine(basePicturePath, imageName);
        Stream stream;
        var storageResult = await sdkCore.GetFileFromS3Storage(imageName).ConfigureAwait(false);
        stream = storageResult.ResponseStream;

        using (var image = new MagickImage(stream))
        {
            var profile = image.GetExifProfile();
            // Write all values to the console
            try
            {
                foreach (var value in profile.Values)
                {
                    Console.WriteLine("{0}({1}): {2}", value.Tag, value.DataType, value);
                }
            }
            catch (Exception)
            {
                // Console.WriteLine(e);
            }

            image.Rotate(90);
            var base64String = image.ToBase64();
            itemsHtml +=
                $@"<p><img src=""data:image/png;base64,{base64String}"" width=""{imageWidth}px"" alt="""" /></p>";
        }

        await stream.DisposeAsync().ConfigureAwait(false);

        return itemsHtml;
    }

    private async Task<string> GeneratePdf(List<string> picturesOfTasks)
    {
        var resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.page.html";
        var assembly = Assembly.GetExecutingAssembly();
        string html;
        await using (var resourceStream = assembly.GetManifestResourceStream(resourceString))
        {
            using var reader = new StreamReader(resourceStream ??
                                                throw new InvalidOperationException(
                                                    $"{nameof(resourceStream)} is null"));
            html = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        // Read docx stream
        resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx";
        var docxFileResourceStream = assembly.GetManifestResourceStream(resourceString);
        if (docxFileResourceStream == null)
        {
            throw new InvalidOperationException($"{nameof(docxFileResourceStream)} is null");
        }

        var docxFileStream = new MemoryStream();
        await docxFileResourceStream.CopyToAsync(docxFileStream).ConfigureAwait(false);
        await docxFileResourceStream.DisposeAsync().ConfigureAwait(false);
        string basePicturePath = Path.Combine(Path.GetTempPath(), "pictures", "workorders");
        Directory.CreateDirectory(basePicturePath);
        var word = new WordProcessor(docxFileStream);
        string imagesHtml = "";

        foreach (var imagesName in picturesOfTasks)
        {
            Console.WriteLine($"Trying to insert image into document : {imagesName}");
            imagesHtml = await InsertImage(imagesName, imagesHtml, 700, 650, basePicturePath).ConfigureAwait(false);
        }

        html = html.Replace("{%Content%}", imagesHtml);

        word.AddHtml(html);
        word.Dispose();
        docxFileStream.Position = 0;

        // Build docx
        string downloadPath = Path.Combine(Path.GetTempPath(), "reports", "results");
        Directory.CreateDirectory(downloadPath);
        string timeStamp = DateTime.UtcNow.ToString("yyyyMMdd") + "_" + DateTime.UtcNow.ToString("hhmmss");
        Random rnd = new Random();
        var rndNumber = rnd.Next(0, 1000);
        string docxFileName = $"{timeStamp}{rndNumber}_temp.docx";
        string tempPDFFileName = $"{timeStamp}{rndNumber}_temp.pdf";
        string tempPDFFilePath = Path.Combine(downloadPath, tempPDFFileName);
        var docxFile = new FileStream(Path.Combine(Path.GetTempPath(), "reports", "results", docxFileName),
            FileMode.Create, FileAccess.Write);
        await using (docxFile.ConfigureAwait(false))
        {
            docxFileStream.WriteTo(docxFile);
        }

        // Convert to PDF
        ReportHelper.ConvertToPdf(Path.Combine(Path.GetTempPath(), "reports", "results", docxFileName), downloadPath);
        File.Delete(Path.Combine(Path.GetTempPath(), "reports", "results", docxFileName));

        // Upload PDF
        string hash = await sdkCore.PdfUpload(tempPDFFilePath).ConfigureAwait(false);
        if (hash != null)
        {
            //rename local file
            FileInfo fileInfo = new FileInfo(tempPDFFilePath);
            fileInfo.CopyTo(downloadPath + "/" + hash + ".pdf", true);
            fileInfo.Delete();
            await sdkCore.PutFileToStorageSystem(Path.Combine(downloadPath, $"{hash}.pdf"), $"{hash}.pdf").ConfigureAwait(false);
        }

        return hash;
    }
}