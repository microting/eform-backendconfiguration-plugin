using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Messages;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormCaseTemplateBase.Infrastructure.Data.Entities;
using Rebus.Handlers;

namespace BackendConfiguration.Pn.Handlers;

public class DocumentUpdatedHandler : IHandleMessages<DocumentUpdated>
{
    private readonly Core _sdkCore;
    private readonly BackendConfigurationDbContextHelper _backendConfigurationDbContextHelper;
    private readonly DocumentDbContextHelper _documentDbContextHelper;

    public DocumentUpdatedHandler(BackendConfigurationDbContextHelper backendConfigurationDbContextHelper, Core sdkCore, DocumentDbContextHelper documentDbContextHelper)
    {
        _backendConfigurationDbContextHelper = backendConfigurationDbContextHelper;
        _sdkCore = sdkCore;
        _documentDbContextHelper = documentDbContextHelper;
    }

    public async Task Handle(DocumentUpdated message)
    {
        await using var sdkDbContext = _sdkCore.DbContextHelper.GetDbContext();
        await using var documentDbContext = _documentDbContextHelper.GetDbContext();
        await using var backendConfigurationDbContext = _backendConfigurationDbContextHelper.GetDbContext();
        var document = await documentDbContext.Documents
            .Include(x => x.DocumentProperties)
            .Include(x => x.DocumentTranslations)
            .Include(x => x.DocumentUploadedDatas)
            //.Include(x => x.DocumentSites)
            .FirstOrDefaultAsync(x => x.Id == message.DocumentId && x.Status == true);

        if (document == null)
        {
            return;
        }

        var documentProperties = document.DocumentProperties.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).ToList();

        foreach (var documentProperty in documentProperties)
        {
            var propertySites = await backendConfigurationDbContext.PropertyWorkers
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == documentProperty.PropertyId)
                .ToListAsync();

            foreach (var propertyWorker in propertySites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
            {
                // var documentSite = documentDbContext.DocumentSites
                //     .Where(x => x.DocumentId == document.Id)
                //     .FirstOrDefault(x => x.WorkflowState != Constants.WorkflowStates.Removed
                //                          && x.SdkSiteId == propertyWorker.WorkerId);
                //
                // if (documentSite == null)
                // {
                var documentSite = new DocumentSite
                {
                    DocumentId = document.Id,
                    SdkSiteId = propertyWorker.WorkerId,
                    PropertyId = documentProperty.PropertyId
                };
                await documentSite.Create(documentDbContext);
                //}

                var clt = await sdkDbContext.CheckListTranslations.FirstAsync(x => x.Text == "00. Info boks");
                var folderProperty = await documentDbContext.FolderProperties.FirstOrDefaultAsync(x => x.FolderId == document.FolderId && x.PropertyId == documentProperty.PropertyId);

                int folderId;
                if (folderProperty == null)
                {
                    var propertyFolderId = await backendConfigurationDbContext.Properties.AsNoTracking()
                        .Where(x => x.Id == documentProperty.PropertyId).Select(x => x.FolderId).FirstAsync();
                    // var folder = await documentDbContext.Folders.FirstAsync(x => x.Id == document.FolderId);
                    var folderTranslations = await documentDbContext.FolderTranslations
                        .Where(x => x.FolderId == document.FolderId).ToListAsync();
                    var folderTranslationList = new List<CommonTranslationsModel>();
                    foreach (var translation in folderTranslations)
                    {
                        folderTranslationList.Add(new CommonTranslationsModel
                        {
                            Description = translation.Description,
                            LanguageId = translation.LanguageId,
                            Name = translation.Name
                        });
                    }

                    var folders = await sdkDbContext.FolderTranslations.AsNoTracking().Join(sdkDbContext.Folders,
                            folderTranslation => folderTranslation.FolderId,
                            folder1 => folder1.Id,
                            (folderTranslation, folder1) => new {folderTranslation, folder1})
                        .Where(x => x.folder1.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.folderTranslation.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.folder1.ParentId == propertyFolderId)
                        .Where(x => x.folderTranslation.Name == "26. Dokumenter")
                        .Select(x => x.folder1)
                        .FirstOrDefaultAsync().ConfigureAwait(false);


                    if (folders != null)
                    {
                        folderId = folders.Id;
                    }
                    else
                    {
                        var documentFolderTranslations = new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                Description = "",
                                LanguageId = 1,
                                Name = "26. Dokumenter"
                            },
                            new()
                            {
                                Description = "",
                                LanguageId = 2,
                                Name = "26. Documents"
                            },
                            new()
                            {
                                Description = "",
                                LanguageId = 3,
                                Name = "26. Dokumenten"
                            }
                        };

                        folderId = await _sdkCore.FolderCreate(documentFolderTranslations, propertyFolderId)
                            .ConfigureAwait(false);
                    }

                    folderId = await _sdkCore.FolderCreate(folderTranslationList, folderId).ConfigureAwait(false);
                    folderProperty = new FolderProperty
                    {
                        FolderId = document.FolderId,
                        SdkFolderId = folderId,
                        PropertyId = documentProperty.PropertyId
                    };
                    await folderProperty.Create(documentDbContext);
                }

                var sdkFolder = await sdkDbContext.Folders.FirstAsync(x => x.Id == folderProperty.SdkFolderId);
                var site = await sdkDbContext.Sites.FirstAsync(x => x.Id == propertyWorker.WorkerId);
                var language = await sdkDbContext.Languages.FirstAsync(x => x.Id == site.LanguageId);

                if (!string.IsNullOrEmpty(document.DocumentTranslations.First(x => x.LanguageId == language.Id).Name) && document.DocumentUploadedDatas.First(x => x.LanguageId == language.Id && x.Extension == "pdf").Hash != null)
                {
                    var mainElement = await _sdkCore.ReadeForm(clt.CheckListId, language);
                    mainElement.CheckListFolderName = sdkFolder.MicrotingUid.ToString();
                    mainElement.EndDate = DateTime.Now.AddYears(20).ToUniversalTime();
                    mainElement.Label = document.DocumentTranslations.First(x => x.LanguageId == language.Id).Name;
                    mainElement.ElementList[0].Label = mainElement.Label;
                    mainElement.ElementList[0].DoneButtonEnabled = false;
                    mainElement.ElementList[0].Description.InderValue = document.DocumentTranslations
                        .First(x => x.LanguageId == language.Id).Description;
                    mainElement.Repeated = 0;

                    if (document.DocumentUploadedDatas.Count(x => x.Hash != null && x.LanguageId == language.Id) > 0)
                    {
                        ShowPdf showPdf = new ShowPdf(0,
                            false,
                            false,
                            mainElement.Label,
                            document.DocumentTranslations.First(x => x.LanguageId == language.Id).Description,
                            Constants.FieldColors.Default, 1, false,
                            document.DocumentUploadedDatas.First(x => x.LanguageId == language.Id).Hash);
                        ((DataElement)mainElement.ElementList[0]).DataItemList.RemoveAt(0);
                        ((DataElement)mainElement.ElementList[0]).DataItemList.Add(showPdf);
                    }
                    else
                    {
                        ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = mainElement.Label;
                        ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue =
                            document.DocumentTranslations.First(x => x.LanguageId == language.Id).Description;
                    }

                    var caseId = await _sdkCore.CaseCreate(mainElement, "", (int)site.MicrotingUid!, sdkFolder.Id);

                    documentSite.SdkCaseId = (int)caseId!;
                    await documentSite.Update(documentDbContext);
                }
                document.IsLocked = false;
                await document.Update(documentDbContext);
            }
        }
    }
}