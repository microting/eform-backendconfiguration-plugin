/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System.Reflection;
using BackendConfiguration.Pn.Services.WordService;
using ImageMagick;
using Microting.eForm.Helpers;

namespace BackendConfiguration.Pn.Services.BackendConfigurationTaskManagementService;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackendConfigurationLocalizationService;
using Infrastructure.Helpers;
using Infrastructure.Models.TaskManagement;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

public class BackendConfigurationTaskManagementService : IBackendConfigurationTaskManagementService
{
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IUserService _userService;

    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

    public BackendConfigurationTaskManagementService(
        IBackendConfigurationLocalizationService localizationService,
        IEFormCoreService coreHelper, IUserService userService,
        // ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext
    )
    {
        _localizationService = localizationService;
        _coreHelper = coreHelper;
        _userService = userService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
    }

    public async Task<List<WorkorderCaseModel>> GetReport(TaskManagementFiltersModel filtersModel)
    {
        try
        {
            var query = _backendConfigurationPnDbContext.WorkorderCases
                .Include(x => x.PropertyWorker)
                .ThenInclude(x => x.Property)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyWorker.Property.WorkorderEnable)
                .Where(x => x.LeadingCase == true)
                .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask);

            if (filtersModel.PropertyId != -1)
            {
                query = query
                    .Where(x => x.PropertyWorker.PropertyId == filtersModel.PropertyId);
            }

            if (filtersModel.Status != null)
            {
                query = filtersModel.Status switch
                {
                    -1 => query.Where(x =>
                        x.CaseStatusesEnum == CaseStatusesEnum.Ongoing ||
                        x.CaseStatusesEnum == CaseStatusesEnum.Completed),
                    1 => query.Where(x => x.CaseStatusesEnum == CaseStatusesEnum.Ongoing),
                    2 => query.Where(x => x.CaseStatusesEnum == CaseStatusesEnum.Completed),
                    _ => query
                };
            }

            if (!string.IsNullOrEmpty(filtersModel.AreaName))
            {
                query = query.Where(x => filtersModel.AreaName == x.SelectedAreaName);
            }

            if (!string.IsNullOrEmpty(filtersModel.CreatedBy))
            {
                query = query.Where(x => x.CreatedByName == filtersModel.CreatedBy);
            }

            if (filtersModel.DateFrom.HasValue && filtersModel.DateTo.HasValue)
            {
                query = query
                    .Where(x => x.CaseInitiated >= filtersModel.DateFrom.Value)
                    .Where(x => x.CaseInitiated <= new DateTime(filtersModel.DateTo.Value.Year,
                        filtersModel.DateTo.Value.Month, filtersModel.DateTo.Value.Day, 23, 59, 59));
            }

            var excludeSort = new List<string>
            {
                "PropertyName",
            };
            query = QueryHelper.AddFilterAndSortToQuery(query, filtersModel, new List<string>(), excludeSort);

            var workorderCasesFromDb = await query
                .Select(x => new WorkorderCaseModel
                {
                    AreaName = x.SelectedAreaName,
                    CreatedByName = x.CreatedByName,
                    CreatedByText = x.CreatedByText,
                    CaseInitiated = x.CaseInitiated,
                    Id = x.Id,
                    Status = x.CaseStatusesEnum.ToString(),
                    Description = x.Description,
                    PropertyName = x.PropertyWorker.Property.Name,
                    LastUpdateDate = x.UpdatedAt,
                    //x.CaseId,
                    LastUpdatedBy = x.LastUpdatedByName,
                    LastAssignedTo = x.LastAssignedToName,
                    ParentWorkorderCaseId = x.ParentWorkorderCaseId
                })
                .ToListAsync();

            if (!string.IsNullOrEmpty(filtersModel.LastAssignedTo))
            {
                workorderCasesFromDb = workorderCasesFromDb.Where(x => x.LastAssignedTo == filtersModel.LastAssignedTo)
                    .ToList();
            }

            if (excludeSort.Contains(filtersModel.Sort))
            {
                workorderCasesFromDb = QueryHelper
                    .AddFilterAndSortToQuery(workorderCasesFromDb.AsQueryable(), filtersModel, new List<string>())
                    .ToList();
            }

            return workorderCasesFromDb;
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            throw;
        }
    }

    public async Task<OperationDataResult<WorkOrderCaseReadModel>> GetTaskById(int workOrderCaseId)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var task = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == workOrderCaseId)
                .Include(x => x.PropertyWorker)
                .Select(x => new
                {
                    x.SelectedAreaName,
                    x.Description,
                    x.Id,
                    x.CaseId,
                    x.ParentWorkorderCaseId,
                    x.PropertyWorker.PropertyId,
                }).FirstOrDefaultAsync();
            if (task == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            var uploadIds = await _backendConfigurationPnDbContext.WorkorderCaseImages
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.WorkorderCaseId == task.ParentWorkorderCaseId)
                .Select(x => x.UploadedDataId)
                .ToListAsync();

            var fileNames = new List<string>();
            foreach (var uploadId in uploadIds)
            {
                var fileName = await sdkDbContext.UploadedDatas
                    .Where(x => x.Id == uploadId)
                    .Select(x => x.FileName)
                    .FirstOrDefaultAsync();
                fileNames.Add(fileName);
            }

            var assignedSiteId = await sdkDbContext.Cases
                .Where(x => x.MicrotingUid == task.CaseId || x.Id == task.CaseId)
                .Include(x => x.Site)
                .Select(x => x.Site.Id)
                .FirstOrDefaultAsync();

            var taskForReturn = new WorkOrderCaseReadModel
            {
                AreaName = task.SelectedAreaName,
                AssignedSiteId = assignedSiteId,
                Description = task.Description,
                Id = task.Id,
                PictureNames = fileNames,
                PropertyId = task.PropertyId,
            };
            return new OperationDataResult<WorkOrderCaseReadModel>(true, taskForReturn);
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationDataResult<WorkOrderCaseReadModel>(false,
                $"{_localizationService.GetString("ErrorWhileReadTask")}: {e.Message}");
        }
    }

    public async Task<OperationDataResult<List<string>>> GetItemsEntityListByPropertyId(int propertyId)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var entityListId = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == propertyId)
                .Select(x => x.EntitySelectListAreas)
                .FirstOrDefaultAsync();
            if (entityListId == null)
            {
                return new OperationDataResult<List<string>>(false,
                    _localizationService.GetString("PropertyNotFoundOrEntityListNotCreated"));
            }

            var items = await sdkDbContext.EntityItems
                .Where(x => x.EntityGroupId == entityListId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderBy(x => x.DisplayIndex)
                .Select(x => x.Name)
                .ToListAsync();
            return new OperationDataResult<List<string>>(true, items);
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationDataResult<List<string>>(false,
                $"{_localizationService.GetString("ErrorWhileReadEntityList")}: {e.Message}");
        }
    }

    public async Task<OperationResult> DeleteTaskById(int workOrderCaseId)
    {
        var core = await _coreHelper.GetCore();
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        try
        {
            var workOrderCase = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == workOrderCaseId)
                .Include(x => x.PropertyWorker)
                .FirstOrDefaultAsync();

            if (workOrderCase == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("TaskNotFound"));
            }

            if (workOrderCase.CaseId != 0)
            {
                await core.CaseDelete(workOrderCase.CaseId);
            }

            await workOrderCase.Delete(_backendConfigurationPnDbContext);

            var allChildTasks = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.ParentWorkorderCaseId == workOrderCase.Id)
                .ToListAsync();

            foreach (var childTask in allChildTasks)
            {
                childTask.UpdatedByUserId = _userService.UserId;
                await childTask.Delete(_backendConfigurationPnDbContext);


                if (workOrderCase.CaseId != 0)
                {
                    await core.CaseDelete(workOrderCase.CaseId);
                }

                await workOrderCase.Delete(_backendConfigurationPnDbContext);
            }

            if (workOrderCase.ParentWorkorderCaseId != null)
            {
                var parentTasks = await _backendConfigurationPnDbContext.WorkorderCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == workOrderCase.ParentWorkorderCaseId)
                    .FirstOrDefaultAsync();

                if (parentTasks != null)
                {
                    parentTasks.UpdatedByUserId = _userService.UserId;
                    await parentTasks.Delete(_backendConfigurationPnDbContext);

                    await core.CaseDelete(workOrderCase.CaseId);
                    await workOrderCase.Delete(_backendConfigurationPnDbContext);
                }
            }

            return new OperationResult(true, _localizationService.GetString("TaskDeletedSuccessful"));
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationResult(false,
                $"{_localizationService.GetString("ErrorWhileDeleteTask")}: {e.Message}");
        }
    }

    private async Task RetractEform(WorkorderCase workOrderCase)
    {
        var core = await _coreHelper.GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
    }

    public async Task<OperationResult> CreateTask(WorkOrderCaseCreateModel createModel)
    {
        try
        {
            var core = await _coreHelper.GetCore();
            var sdkDbContext = core.DbContextHelper.GetDbContext();
            var property = await _backendConfigurationPnDbContext.Properties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == createModel.PropertyId)
                .Include(x => x.PropertyWorkers)
                .FirstOrDefaultAsync();
            if (property == null)
            {
                return new OperationDataResult<WorkOrderCaseReadModel>(false,
                    _localizationService.GetString("PropertyNotFound"));
            }

            var propertyWorkers = property.PropertyWorkers
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToList();

            var site = await sdkDbContext.Sites
                .Where(x => x.Id == createModel.AssignedSiteId)
                .FirstOrDefaultAsync();

            createModel.Description = string.IsNullOrEmpty(createModel.Description)
                ? ""
                : createModel.Description.Replace("<div>", "").Replace("</div>", "");
            var newWorkorderCase = new WorkorderCase
            {
                ParentWorkorderCaseId = null,
                CaseId = 0,
                PropertyWorkerId = propertyWorkers.First().Id,
                SelectedAreaName = createModel.AreaName,
                CreatedByName = await _userService.GetCurrentUserFullName(),
                CreatedByText = "",
                CaseStatusesEnum = CaseStatusesEnum.Ongoing,
                Description = createModel.Description,
                CaseInitiated = DateTime.UtcNow,
                LeadingCase = true,
                LastAssignedToName = site.Name
            };
            await newWorkorderCase.Create(_backendConfigurationPnDbContext);

            var picturesOfTasks = new List<string>();
            if (createModel.Files.Any())
            {
                foreach (var picture in createModel.Files)
                {
                    MemoryStream baseMemoryStream = new MemoryStream();
                    await picture.CopyToAsync(baseMemoryStream);
                    var hash = "";
                    using (var md5 = MD5.Create())
                    {
                        var grr = await md5.ComputeHashAsync(baseMemoryStream);
                        hash = BitConverter.ToString(grr).Replace("-", "").ToLower();
                    }

                    var uploadData = new Microting.eForm.Infrastructure.Data.Entities.UploadedData
                    {
                        Checksum = hash,
                        FileName = picture.FileName,
                        FileLocation = "",
                        Extension = picture.ContentType.Split("/")[1]
                    };
                    await uploadData.Create(sdkDbContext);

                    var fileName = $"{uploadData.Id}_{hash}.{picture.ContentType.Split("/")[1]}";
                    string smallFilename = $"{uploadData.Id}_300_{hash}.{picture.ContentType.Split("/")[1]}";
                    string bigFilename = $"{uploadData.Id}_700_{hash}.{picture.ContentType.Split("/")[1]}";
                    baseMemoryStream.Seek(0, SeekOrigin.Begin);
                    MemoryStream s3Stream = new MemoryStream();
                    await baseMemoryStream.CopyToAsync(s3Stream);
                    s3Stream.Seek(0, SeekOrigin.Begin);
                    await core.PutFileToS3Storage(s3Stream, fileName);
                    baseMemoryStream.Seek(0, SeekOrigin.Begin);
                    using (var image = new MagickImage(baseMemoryStream))
                    {
                        decimal currentRation = image.Height / (decimal)image.Width;
                        int newWidth = 300;
                        int newHeight = (int)Math.Round((currentRation * newWidth));

                        image.Resize(newWidth, newHeight);
                        image.Crop(newWidth, newHeight);
                        MemoryStream memoryStream = new MemoryStream();
                        await image.WriteAsync(memoryStream);
                        await core.PutFileToS3Storage(memoryStream, smallFilename);
                        await memoryStream.DisposeAsync();
                        memoryStream.Close();
                        image.Dispose();
                        baseMemoryStream.Seek(0, SeekOrigin.Begin);
                    }

                    using (var image = new MagickImage(baseMemoryStream))
                    {
                        decimal currentRation = image.Height / (decimal)image.Width;
                        int newWidth = 700;
                        int newHeight = (int)Math.Round((currentRation * newWidth));

                        image.Resize(newWidth, newHeight);
                        image.Crop(newWidth, newHeight);
                        MemoryStream memoryStream = new MemoryStream();
                        await image.WriteAsync(memoryStream);
                        await core.PutFileToS3Storage(memoryStream, bigFilename);
                        await memoryStream.DisposeAsync();
                        memoryStream.Close();
                        image.Dispose();
                    }

                    await baseMemoryStream.DisposeAsync();
                    baseMemoryStream.Close();
                    
                    var workOrderCaseImage = new WorkorderCaseImage
                    {
                        WorkorderCaseId = newWorkorderCase.Id,
                        UploadedDataId = uploadData.Id
                    };
                    picturesOfTasks.Add($"{uploadData.Id}_700_{uploadData.Checksum}.{uploadData.Extension}");
                    await workOrderCaseImage.Create(_backendConfigurationPnDbContext);
                }
            }

            var pdfHash = await GeneratePdf(picturesOfTasks);

            var eformIdForOngoingTasks = await sdkDbContext.CheckListTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Text == "02. Ongoing task")
                .Select(x => x.CheckListId)
                .FirstOrDefaultAsync();

            var deviceUsersGroupMicrotingUid = await sdkDbContext.EntityGroups
                .Where(x => x.Id == property.EntitySelectListDeviceUsers)
                .Select(x => int.Parse(x.MicrotingUid))
                .FirstAsync();

            var description = $"<strong>{_localizationService.GetString("AssignedTo")}:</strong> {site.Name}<br>" +
                              $"<strong>{_localizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                              $"<strong>{_localizationService.GetString("Area")}:</strong> {createModel.AreaName}<br>" +
                              $"<strong>{_localizationService.GetString("Description")}:</strong> {createModel.Description}<br><br>" +
                              $"<strong>{_localizationService.GetString("CreatedBy")}:</strong> {await _userService.GetCurrentUserFullName()}<br>" +
                              $"<strong>{_localizationService.GetString("CreatedDate")}:</strong> {DateTime.UtcNow: dd.MM.yyyy}<br><br>" +
                              $"<strong>{_localizationService.GetString("Status")}:</strong> {_localizationService.GetString("Ongoing")}<br><br>";

            var pushMessageTitle = !string.IsNullOrEmpty(createModel.AreaName)
                ? $"{property.Name}; {createModel.AreaName}"
                : $"{property.Name}";
            var pushMessageBody = createModel.Description;

            // deploy eform to ongoing status
            await DeployEform(
                propertyWorkers,
                eformIdForOngoingTasks,
                (int)property.FolderIdForOngoingTasks,
                description,
                CaseStatusesEnum.Ongoing,
                createModel.Description,
                deviceUsersGroupMicrotingUid,
                pdfHash,
                pushMessageBody,
                pushMessageTitle,
                createModel.AreaName);

            return new OperationResult(true, _localizationService.GetString("TaskCreatedSuccessful"));
        }
        catch (Exception e)
        {
            Log.LogException(e.Message);
            Log.LogException(e.StackTrace);
            return new OperationResult(false,
                $"{_localizationService.GetString("ErrorWhileCreateTask")}: {e.Message}");
        }
    }

    private async Task DeployEform(
        List<PropertyWorker> propertyWorkers,
        int eformId,
        int folderId,
        string description,
        CaseStatusesEnum status,
        string newDescription,
        int? deviceUsersGroupId,
        string pdfHash,
        string pushMessageBody,
        string pushMessageTitle,
        string areaName)
    {
        var core = await _coreHelper.GetCore();
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();
        foreach (var propertyWorker in propertyWorkers)
        {
            var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.WorkerId);
            var siteLanguage = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId);
            var mainElement = await core.ReadeForm(eformId, siteLanguage);
            mainElement.CheckListFolderName = await sdkDbContext.Folders
                .Where(x => x.Id == folderId)
                .Select(x => x.MicrotingUid.ToString())
                .FirstOrDefaultAsync();
            mainElement.Label = " ";
            mainElement.ElementList[0].QuickSyncEnabled = true;
            mainElement.EnableQuickSync = true;
            mainElement.ElementList[0].Label = " ";
            mainElement.ElementList[0].Description.InderValue =
                description + "<center><strong>******************</strong></center>";
            mainElement.PushMessageTitle = pushMessageTitle;
            mainElement.PushMessageBody = pushMessageBody;
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description;
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Color = Constants.FieldColors.Yellow;
            ((ShowPdf)((DataElement)mainElement.ElementList[0]).DataItemList[1]).Value = pdfHash;
            if (deviceUsersGroupId != null)
            {
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[4]).Source =
                    (int)deviceUsersGroupId;
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[4]).Mandatory = true;
                ((Comment)((DataElement)mainElement.ElementList[0]).DataItemList[3]).Value = newDescription;
                ((SingleSelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Mandatory = true;
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
            var caseId = await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId);
            var workOrderCase = new WorkorderCase
            {
                CaseId = (int)caseId,
                PropertyWorkerId = propertyWorker.Id,
                CaseStatusesEnum = status,
                SelectedAreaName = areaName,
                CreatedByName = site.Name,
                Description = newDescription,
                CaseInitiated = DateTime.UtcNow,
                CreatedByUserId = _userService.UserId,
                LastAssignedToName = site.Name,
                LeadingCase = false
            };
            await workOrderCase.Create(_backendConfigurationPnDbContext);
        }
    }

    private async Task<string> InsertImage(string imageName, string itemsHtml, int imageSize, int imageWidth,
        string basePicturePath)
    {
        var core = await _coreHelper.GetCore();
        // var filePath = Path.Combine(basePicturePath, imageName);
        Stream stream;
        var storageResult = await core.GetFileFromS3Storage(imageName);
        stream = storageResult.ResponseStream;

        using (var image = new MagickImage(stream))
        {
            var profile = image.GetExifProfile();
            // Write all values to the console
            try
            {
                foreach (var value in profile.Values)
                {
                    Console.WriteLine("{0}({1}): {2}", value.Tag, value.DataType, value.ToString());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            image.Rotate(90);
            var base64String = image.ToBase64();
            itemsHtml +=
                $@"<p><img src=""data:image/png;base64,{base64String}"" width=""{imageWidth}px"" alt="""" /></p>";
        }

        await stream.DisposeAsync();

        return itemsHtml;
    }

    private async Task<string> GeneratePdf(List<string> picturesOfTasks)
    {
        var core = await _coreHelper.GetCore();
        var resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.page.html";
        var assembly = Assembly.GetExecutingAssembly();
        string html;
        await using (var resourceStream = assembly.GetManifestResourceStream(resourceString))
        {
            using var reader = new StreamReader(resourceStream ??
                                                throw new InvalidOperationException(
                                                    $"{nameof(resourceStream)} is null"));
            html = await reader.ReadToEndAsync();
        }

        // Read docx stream
        resourceString = "BackendConfiguration.Pn.Resources.Templates.WordExport.file.docx";
        var docxFileResourceStream = assembly.GetManifestResourceStream(resourceString);
        if (docxFileResourceStream == null)
        {
            throw new InvalidOperationException($"{nameof(docxFileResourceStream)} is null");
        }

        var docxFileStream = new MemoryStream();
        await docxFileResourceStream.CopyToAsync(docxFileStream);
        await docxFileResourceStream.DisposeAsync();
        string basePicturePath = Path.Combine(Path.GetTempPath(), "pictures", "workorders");
        Directory.CreateDirectory(basePicturePath);
        var word = new WordProcessor(docxFileStream);
        string imagesHtml = "";

        foreach (var imagesName in picturesOfTasks)
        {
            Console.WriteLine($"Trying to insert image into document : {imagesName}");
            imagesHtml = await InsertImage(imagesName, imagesHtml, 700, 650, basePicturePath);
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
        await using (var docxFile = new FileStream(Path.Combine(Path.GetTempPath(), "reports", "results", docxFileName),
                         FileMode.Create, FileAccess.Write))
        {
            docxFileStream.WriteTo(docxFile);
        }

        // Convert to PDF
        ReportHelper.ConvertToPdf(Path.Combine(Path.GetTempPath(), "reports", "results", docxFileName), downloadPath);
        File.Delete(Path.Combine(Path.GetTempPath(), "reports", "results", docxFileName));

        // Upload PDF
        string hash = await core.PdfUpload(tempPDFFilePath);
        if (hash != null)
        {
            //rename local file
            FileInfo fileInfo = new FileInfo(tempPDFFilePath);
            fileInfo.CopyTo(downloadPath + "/" + hash + ".pdf", true);
            fileInfo.Delete();
            await core.PutFileToStorageSystem(Path.Combine(downloadPath, $"{hash}.pdf"), $"{hash}.pdf");
        }

        return hash;
    }
}