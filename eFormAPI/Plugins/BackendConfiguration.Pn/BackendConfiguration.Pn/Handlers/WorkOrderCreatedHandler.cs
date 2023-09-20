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
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Rebus.Handlers;
using File = System.IO.File;
using KeyValuePair = Microting.eForm.Dto.KeyValuePair;

namespace BackendConfiguration.Pn.Handlers;

public class WorkOrderCreatedHandler : IHandleMessages<WorkOrderCreated>
{
    private readonly Core _sdkCore;
    private readonly BackendConfigurationDbContextHelper _backendConfigurationDbContextHelper;
    private readonly ChemicalDbContextHelper _chemicalDbContextHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;

    public WorkOrderCreatedHandler(BackendConfigurationDbContextHelper backendConfigurationDbContextHelper, ChemicalDbContextHelper chemicalDbContextHelper, Core sdkCore, IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
    {
        _backendConfigurationDbContextHelper = backendConfigurationDbContextHelper;
        _chemicalDbContextHelper = chemicalDbContextHelper;
        _sdkCore = sdkCore;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
    }

    public async Task Handle(WorkOrderCreated message)
    {
        var pdfHash = await GeneratePdf(message.PicturesOfTasks).ConfigureAwait(false);
        await DeployEform(
            message.PropertyWorkers,
            message.EformId,
            message.FolderId,
            message.Description,
            CaseStatusesEnum.Ongoing,
            message.WorkorderCaseId,
            message.NewDescription,
            message.DeviceUsersGroupId,
            pdfHash,
            message.SiteName,
            message.PushMessageBody,
            message.PushMessageTitle,
            message.AreaName,
            message.CreatedByUserId,
            message.PropertyName,
            message.FolderIdForOngoingTasks,
            message.FolderIdForTasks,
            message.FolderIdForCompletedTasks).ConfigureAwait(false);
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
        string pdfHash,
        string siteName,
        string pushMessageBody,
        string pushMessageTitle,
        string areaNameb,
        int createdByUserId,
        string propertyName,
        int FolderIdForOngoingTasks,
        int FolderIdForTasks,
        int FolderIdForCompletedTasks
        )
    {
        var backendConfigurationPnDbContext = _backendConfigurationDbContextHelper.GetDbContext();
        var sdkDbContext = _sdkCore.DbContextHelper.GetDbContext();

        var workOrderCase = await backendConfigurationPnDbContext.WorkorderCases.FirstAsync(x => x.Id == workorderCaseId);
        DateTime startDate = new DateTime(2022, 12, 5);
        var displayOrder = (int)(DateTime.UtcNow - startDate).TotalSeconds;

        foreach (var propertyWorker in propertyWorkers)
        {
            var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.Value).ConfigureAwait(false);
            var siteLanguage = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(siteLanguage.LanguageCode);
            var priorityText = "";

            switch (workOrderCase.Priority)
            {
                case "1":
                    displayOrder = 100_000_000 + displayOrder;
                    priorityText = $"<strong>{_backendConfigurationLocalizationService.GetString("Priority")}:</strong> {_backendConfigurationLocalizationService.GetString("Urgent")}<br>";
                    break;
                case "2":
                    displayOrder = 200_000_000 + displayOrder;
                    priorityText = $"<strong>{_backendConfigurationLocalizationService.GetString("Priority")}:</strong> {_backendConfigurationLocalizationService.GetString("High")}<br>";
                    break;
                case "3":
                    displayOrder = 300_000_000 + displayOrder;
                    priorityText = $"<strong>{_backendConfigurationLocalizationService.GetString("Priority")}:</strong> {_backendConfigurationLocalizationService.GetString("Medium")}<br>";
                    break;
                case "4":
                    displayOrder = 400_000_000 + displayOrder;
                    priorityText = $"<strong>{_backendConfigurationLocalizationService.GetString("Priority")}:</strong> {_backendConfigurationLocalizationService.GetString("Low")}<br>";
                    break;
            }

            var textStatus = "";

            switch (workOrderCase.CaseStatusesEnum)
            {
                case CaseStatusesEnum.Ongoing:
                    textStatus = _backendConfigurationLocalizationService.GetString("Ongoing");
                    break;
                case CaseStatusesEnum.Completed:
                    textStatus = _backendConfigurationLocalizationService.GetString("Completed");
                    break;
                case CaseStatusesEnum.Awaiting:
                    textStatus = _backendConfigurationLocalizationService.GetString("Awaiting");
                    break;
                case CaseStatusesEnum.Ordered:
                    textStatus = _backendConfigurationLocalizationService.GetString("Ordered");
                    break;
            }

            var assignedTo = site.Name == siteName ? "" : $"<strong>{_backendConfigurationLocalizationService.GetString("AssignedTo")}:</strong> {siteName}<br>";

            var areaName = !string.IsNullOrEmpty(workOrderCase.SelectedAreaName)
                ? $"<strong>{_backendConfigurationLocalizationService.GetString("Area")}:</strong> {workOrderCase.SelectedAreaName}<br>"
                : "";

            var outerDescription = $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {propertyName}<br>" +
                                   areaName +
                                   $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {newDescription}<br>" +
                                   priorityText +
                                   assignedTo +
                                   $"<strong>{_backendConfigurationLocalizationService.GetString("Status")}:</strong> {textStatus}<br><br>";

            var mainElement = await _sdkCore.ReadeForm(eformId, siteLanguage).ConfigureAwait(false);
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
            if (site.Name == siteName)
            {
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == (workOrderCase.Priority != "1" ? FolderIdForOngoingTasks : FolderIdForTasks))
                    .MicrotingUid.ToString();
                folderId = FolderIdForOngoingTasks;
                mainElement.PushMessageTitle = pushMessageTitle;
                mainElement.PushMessageBody = pushMessageBody;
            }
            else
            {
                folderId = FolderIdForCompletedTasks;
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == FolderIdForCompletedTasks)
                    .MicrotingUid.ToString();
            }
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description.Replace("\r\n", "<br>").Replace("\n", "<br>");
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Color = Constants.FieldColors.Yellow;
            ((ShowPdf)((DataElement)mainElement.ElementList[0]).DataItemList[1]).Value = pdfHash;
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
            var caseId = await _sdkCore.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId).ConfigureAwait(false);
            var newWorkOrderCase = new WorkorderCase
            {
                CaseId = (int)caseId,
                PropertyWorkerId = propertyWorker.Key,
                CaseStatusesEnum = status,
                ParentWorkorderCaseId = workorderCaseId,
                SelectedAreaName = workOrderCase.SelectedAreaName,
                CreatedByName = workOrderCase.CreatedByName,
                CreatedByText = workOrderCase.CreatedByText,
                Description = newDescription,
                CaseInitiated = DateTime.UtcNow,
                LastAssignedToName = site.Name,
                LastUpdatedByName = "",
                LeadingCase = false,
                Priority = workOrderCase.Priority,
                CreatedByUserId = createdByUserId
            };
            await newWorkOrderCase.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
        }
    }

    private async Task<string> InsertImage(string imageName, string itemsHtml, int imageSize, int imageWidth,
        string basePicturePath)
    {
        // var filePath = Path.Combine(basePicturePath, imageName);
        Stream stream;
        var storageResult = await _sdkCore.GetFileFromS3Storage(imageName).ConfigureAwait(false);
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
        string hash = await _sdkCore.PdfUpload(tempPDFFilePath).ConfigureAwait(false);
        if (hash != null)
        {
            //rename local file
            FileInfo fileInfo = new FileInfo(tempPDFFilePath);
            fileInfo.CopyTo(downloadPath + "/" + hash + ".pdf", true);
            fileInfo.Delete();
            await _sdkCore.PutFileToStorageSystem(Path.Combine(downloadPath, $"{hash}.pdf"), $"{hash}.pdf").ConfigureAwait(false);
        }

        return hash;
    }
}