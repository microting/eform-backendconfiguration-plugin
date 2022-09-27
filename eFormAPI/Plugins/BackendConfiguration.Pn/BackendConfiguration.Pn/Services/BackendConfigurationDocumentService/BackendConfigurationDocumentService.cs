using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Documents;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using DocumentFormat.OpenXml.Office2010.ExcelAc;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.eFormCaseTemplateBase.Infrastructure.Data.Entities;
using CommonTranslationsModel = Microting.eForm.Infrastructure.Models.CommonTranslationsModel;

namespace BackendConfiguration.Pn.Services.BackendConfigurationDocumentService;

public class BackendConfigurationDocumentService : IBackendConfigurationDocumentService
{
    private readonly CaseTemplatePnDbContext _caseTemplatePnDbContext;
    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;

    public BackendConfigurationDocumentService(CaseTemplatePnDbContext caseTemplatePnDbContext, IEFormCoreService coreHelper, IBackendConfigurationLocalizationService backendConfigurationLocalizationService, IUserService userService, BackendConfigurationPnDbContext backendConfigurationPnDbContext)
    {
        _caseTemplatePnDbContext = caseTemplatePnDbContext;
        _coreHelper = coreHelper;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
        _userService = userService;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
    }

    public async Task<OperationDataResult<Paged<BackendConfigurationDocumentModel>>> Index(BackendConfigurationDocumentRequestModel pnRequestModel)
    {
        var query = _caseTemplatePnDbContext.Documents
            .Include(x => x.DocumentTranslations)
            .Include(x => x.DocumentProperties)
            .Include(x => x.DocumentUploadedDatas)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);
        var total = await query.Select(x => x.Id).CountAsync().ConfigureAwait(false);

        var results = new List<BackendConfigurationDocumentModel>();

        if (total > 0)
        {
            results = await query
                .Select(x => new BackendConfigurationDocumentModel
                {
                    Id = x.Id,
                    StartDate = x.StartAt,
                    EndDate = x.EndAt,
                    FolderId = x.FolderId,
                    DocumentUploadedDatas = x.DocumentUploadedDatas
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => new BackendConfigurationDocumentUploadedData
                        {
                            Id = x.Id,
                            DocumentId = x.DocumentId,
                            LanguageId = x.LanguageId,
                            Name = x.Name,
                            Hash = x.Hash,
                            FileName = x.File
                        }).ToList(),
                    DocumentTranslations = x.DocumentTranslations
                        .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(y => new BackendConfigurationDocumentTranslationModel
                        {
                            Id = y.Id,
                            Name = y.Name,
                            Description = y.Description,
                            LanguageId = y.LanguageId
                        }).ToList(),
                    DocumentProperties = x.DocumentProperties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(y => new BackendConfigurationDocumentProperty
                    {
                        Id = y.Id,
                        DocumentId = y.DocumentId,
                        PropertyId = y.PropertyId
                    }).ToList()
                }).ToListAsync().ConfigureAwait(false);

            foreach (var backendConfigurationDocumentModel in results)
            {
                string propertyNames = "";
                foreach (var backendConfigurationDocumentProperty in backendConfigurationDocumentModel.DocumentProperties)
                {
                    if (propertyNames != "")
                    {
                        propertyNames += "<br>";
                    }
                    var property = await _backendConfigurationPnDbContext.Properties.FirstAsync(x =>
                        x.Id == backendConfigurationDocumentProperty.PropertyId);
                    propertyNames += property.Name;
                }
                backendConfigurationDocumentModel.PropertyNames = propertyNames;
            }
        }
        return new OperationDataResult<Paged<BackendConfigurationDocumentModel>>(true,
            new Paged<BackendConfigurationDocumentModel> { Entities = results, Total = total });
    }

    public async Task<OperationDataResult<BackendConfigurationDocumentModel>> GetDocumentAsync(int id)
    {
        var document = await _caseTemplatePnDbContext.Documents
            .Include(x => x.DocumentTranslations)
            .Include(x => x.DocumentProperties)
            .Include(x => x.DocumentUploadedDatas)
            .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);

        if (document == null)
        {
            return new OperationDataResult<BackendConfigurationDocumentModel>(false,
                _backendConfigurationLocalizationService.GetString("DocumentNotFound"));
        }

        var result = new BackendConfigurationDocumentModel
        {
            Id = document.Id,
            StartDate = document.StartAt,
            EndDate = document.EndAt,
            FolderId = document.FolderId,
            DocumentUploadedDatas = document.DocumentUploadedDatas
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => new BackendConfigurationDocumentUploadedData
                {
                    Id = x.Id,
                    DocumentId = x.DocumentId,
                    LanguageId = x.LanguageId,
                    Name = x.Name,
                    Hash = x.Hash,
                    FileName = x.File
                }).ToList(),
            DocumentTranslations = document.DocumentTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => new BackendConfigurationDocumentTranslationModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description,
                    LanguageId = x.LanguageId
                }).ToList(),
            DocumentProperties = document.DocumentProperties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(y => new BackendConfigurationDocumentProperty
                {
                    Id = y.Id,
                    DocumentId = y.DocumentId,
                    PropertyId = y.PropertyId
                }).ToList()
        };

        return new OperationDataResult<BackendConfigurationDocumentModel>(true, result);
    }

    public async Task<OperationResult> UpdateDocumentAsync(BackendConfigurationDocumentModel model)
    {
        var document = await _caseTemplatePnDbContext.Documents
            .Include(x => x.DocumentTranslations)
            .Include(x => x.DocumentProperties)
            .Include(x => x.DocumentUploadedDatas)
            .FirstOrDefaultAsync(x => x.Id == model.Id).ConfigureAwait(false);

        if (document == null)
        {
            return new OperationResult(false,
                _backendConfigurationLocalizationService.GetString("DocumentNotFound"));
        }

        // document.StartAt = model.StartDate;
        document.EndAt = model.EndDate;
        document.FolderId = model.FolderId;
        await document.Update(_caseTemplatePnDbContext).ConfigureAwait(false);

        foreach (var translation in model.DocumentTranslations)
        {
            var documentTranslation = document.DocumentTranslations
                .FirstOrDefault(x => x.Id == translation.Id);

            if (documentTranslation == null)
            {
                return new OperationResult(false,
                    _backendConfigurationLocalizationService.GetString("DocumentTranslationNotFound"));
            }

            documentTranslation.Name = translation.Name;
            documentTranslation.Description = translation.Description;

            await documentTranslation.Update(_caseTemplatePnDbContext).ConfigureAwait(false);
        }

        if (model.DocumentProperties != null)
        {
            foreach (var property in model.DocumentProperties)
            {
                var documentProperty = document.DocumentProperties
                    .FirstOrDefault(x => x.Id == property.Id);
                if (documentProperty == null)
                {
                    documentProperty = new DocumentProperty
                    {
                        DocumentId = document.Id,
                        PropertyId = property.PropertyId
                    };

                    await documentProperty.Create(_caseTemplatePnDbContext).ConfigureAwait(false);
                }
            }
        }

        var core = await _coreHelper.GetCore();
        foreach (var documentUploadedData in model.DocumentUploadedDatas)
        {
            var documentUploadedDataDb = document.DocumentUploadedDatas
                .FirstOrDefault(x => x.Id == documentUploadedData.Id);

            if (documentUploadedDataDb == null)
            {
                documentUploadedDataDb = new DocumentUploadedData
                {
                    DocumentId = document.Id,
                    LanguageId = documentUploadedData.LanguageId,
                    Name = documentUploadedData.Name,
                };

                await documentUploadedDataDb.Create(_caseTemplatePnDbContext).ConfigureAwait(false);
                MemoryStream memoryStream = new MemoryStream();

                if (documentUploadedData.File != null)
                {
                    await documentUploadedData.File.CopyToAsync(memoryStream);
                    string checkSum = "";
                    using (var md5 = MD5.Create())
                    {
                        byte[] grr = md5.ComputeHash(memoryStream.ToArray());
                        checkSum = BitConverter.ToString(grr).Replace("-", "").ToLower();
                    }

                    var fileName = checkSum + "." + documentUploadedDataDb.Name.Split(".")[1];

                    memoryStream.Seek(0, SeekOrigin.Begin);
                    await core.PutFileToS3Storage(memoryStream, fileName);
                    documentUploadedDataDb.File = fileName;
                    documentUploadedDataDb.Hash = checkSum;
                    await documentUploadedDataDb.Update(_caseTemplatePnDbContext).ConfigureAwait(false);
                }
            }
            else
            {
                MemoryStream memoryStream = new MemoryStream();

                if (documentUploadedData.File != null)
                {
                    await documentUploadedData.File.CopyToAsync(memoryStream);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    string checkSum = "";
                    using (var md5 = MD5.Create())
                    {
                        byte[] grr = md5.ComputeHash(memoryStream.ToArray());
                        checkSum = BitConverter.ToString(grr).Replace("-", "").ToLower();
                    }

                    var fileName = checkSum + "." + documentUploadedData.Name.Split(".")[1];
                    if (documentUploadedDataDb.File != fileName)
                    {
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        await core.PutFileToS3Storage(memoryStream, fileName);
                        documentUploadedDataDb.File = fileName;
                        documentUploadedDataDb.Hash = checkSum;
                        documentUploadedDataDb.Name = documentUploadedData.Name;
                        await documentUploadedDataDb.Update(_caseTemplatePnDbContext).ConfigureAwait(false);
                    }
                }
            }
        }

        var assignmentsForDelete = document.DocumentProperties
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => !model.DocumentProperties.Select(y => y.PropertyId).Contains(x.PropertyId))
            .ToList();

        foreach (var documentProperty in assignmentsForDelete)
        {
            // var property = model.DocumentProperties
            //     .FirstOrDefault(x => x.PropertyId == documentProperty.PropertyId);
            // if (property == null)
            // {
                await documentProperty.Delete(_caseTemplatePnDbContext).ConfigureAwait(false);
            // }
        }

        return new OperationResult(true,
            _backendConfigurationLocalizationService.GetString("DocumentUpdatedSuccessfully"));
    }

    public async Task<OperationResult> CreateDocumentAsync(BackendConfigurationDocumentModel model)
    {
        var core = await _coreHelper.GetCore();
        var document = new Document
        {
            // StartAt = model.StartDate,
            EndAt = model.EndDate,
            FolderId = model.FolderId
        };

        await document.Create(_caseTemplatePnDbContext).ConfigureAwait(false);

        foreach (var translation in model.DocumentTranslations)
        {
            var documentTranslation = new DocumentTranslation
            {
                Name = translation.Name,
                Description = translation.Description,
                LanguageId = translation.LanguageId,
                DocumentId = document.Id,
            };

            await documentTranslation.Create(_caseTemplatePnDbContext).ConfigureAwait(false);
        }

        foreach (var documentUploadedData in model.DocumentUploadedDatas)
        {
            var documentUploadedDataModel = new DocumentUploadedData
            {
                DocumentId = document.Id,
                LanguageId = documentUploadedData.LanguageId,
                Name = documentUploadedData.Name,
            };

            await documentUploadedDataModel.Create(_caseTemplatePnDbContext).ConfigureAwait(false);
            MemoryStream memoryStream = new MemoryStream();

            if (documentUploadedData.File != null)
            {
                await documentUploadedData.File.CopyToAsync(memoryStream);
                string checkSum = "";
                using (var md5 = MD5.Create())
                {
                    byte[] grr = md5.ComputeHash(memoryStream.ToArray());
                    checkSum = BitConverter.ToString(grr).Replace("-", "").ToLower();
                }

                var fileName = checkSum + "." + documentUploadedDataModel.Name.Split(".")[1];

                memoryStream.Seek(0, SeekOrigin.Begin);
                await core.PutFileToS3Storage(memoryStream, fileName);
                documentUploadedDataModel.File = fileName;
                documentUploadedDataModel.Hash = checkSum;
                await documentUploadedDataModel.Update(_caseTemplatePnDbContext).ConfigureAwait(false);
            }
        }

        if (model.DocumentProperties != null)
        {
            foreach (var property in model.DocumentProperties)
            {
                var documentProperty = new DocumentProperty
                {
                    DocumentId = document.Id,
                    PropertyId = property.PropertyId
                };

                await documentProperty.Create(_caseTemplatePnDbContext).ConfigureAwait(false);
            }
        }
        return new OperationResult(true,
            _backendConfigurationLocalizationService.GetString("DocumentCreatedSuccessfully"));
    }

    public async Task<OperationResult> DeleteDocumentAsync(int id)
    {
        var document = await _caseTemplatePnDbContext.Documents
            .Include(x => x.DocumentTranslations)
            .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);

        if (document == null)
        {
            return new OperationResult(false,
                _backendConfigurationLocalizationService.GetString("DocumentNotFound"));
        }

        await document.Delete(_caseTemplatePnDbContext).ConfigureAwait(false);

        foreach (var translation in document.DocumentTranslations)
        {
            await translation.Update(_caseTemplatePnDbContext).ConfigureAwait(false);
        }

        return new OperationResult(true,
            _backendConfigurationLocalizationService.GetString("DocumentDeletedSuccessfully"));
    }

    public async Task<OperationDataResult<Paged<BackendConfigurationDocumentFolderModel>>> GetFolders(BackendConfigurationDocumentFolderRequestModel pnRequestModel)
    {
        var query = _caseTemplatePnDbContext.Folders
            .Include(x => x.FolderTranslations)
            .Include(x => x.FolderProperties)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

        var total = await query.Select(x => x.Id).CountAsync().ConfigureAwait(false);

        var results = new List<BackendConfigurationDocumentFolderModel>();

        if (total > 0)
        {
            results = await query
                .Select(x => new BackendConfigurationDocumentFolderModel
                {
                    Id = x.Id,
                    DocumentFolderTranslations = x.FolderTranslations
                        .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(y => new BackendConfigurationDocumentFolderTranslationModel
                        {
                            Id = y.Id,
                            Name = y.Name,
                            Description = y.Description,
                            LanguageId = y.LanguageId
                        }).ToList(),
                    Properties = x.FolderProperties
                        .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(y => new BackendConfigurationDocumentFolderPropertyModel
                        {
                            Id = y.Id,
                            SdkFolderId = y.SdkFolderId,
                        }).ToList()
                }).ToListAsync().ConfigureAwait(false);
        }
        return new OperationDataResult<Paged<BackendConfigurationDocumentFolderModel>>(true,
            new Paged<BackendConfigurationDocumentFolderModel> { Entities = results, Total = total });
    }

    public async Task<OperationDataResult<BackendConfigurationDocumentFolderModel>> GetFolderAsync(int id)
    {
        var folder = await _caseTemplatePnDbContext.Folders
            .Include(x => x.FolderTranslations)
            .Include(x => x.FolderProperties)
            .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);

        if (folder == null)
        {
            return new OperationDataResult<BackendConfigurationDocumentFolderModel>(false,
                _backendConfigurationLocalizationService.GetString("FolderNotFound"));
        }

        var result = new BackendConfigurationDocumentFolderModel
        {
            Id = folder.Id,
            DocumentFolderTranslations = folder.FolderTranslations
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => new BackendConfigurationDocumentFolderTranslationModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description,
                    LanguageId = x.LanguageId,
                    FolderId = folder.Id
                }).ToList(),
            Properties = folder.FolderProperties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => new BackendConfigurationDocumentFolderPropertyModel
                {
                    Id = x.Id,
                    SdkFolderId = x.SdkFolderId,
                }).ToList()
        };

        return new OperationDataResult<BackendConfigurationDocumentFolderModel>(true, result);
    }

    public async Task<OperationResult> CreateFolder(BackendConfigurationDocumentFolderModel model)
    {
        var properties = await _backendConfigurationPnDbContext.Properties
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .ToListAsync();

        var folder = new Folder();
        await folder.Create(_caseTemplatePnDbContext).ConfigureAwait(false);
        var folderTranslations = new List<CommonTranslationsModel>();
        foreach (var translation in model.DocumentFolderTranslations)
        {
            var folderTranslation = new FolderTranslation
            {
                Name = translation.Name,
                Description = translation.Description,
                LanguageId = translation.LanguageId,
                FolderId = folder.Id,
            };
            folderTranslations.Add(new CommonTranslationsModel()
            {
                Description = translation.Description,
                LanguageId = translation.LanguageId,
                Name = translation.Name
            });

            await folderTranslation.Create(_caseTemplatePnDbContext).ConfigureAwait(false);
        }

        foreach (var property in properties)
        {



            // var core = await _coreHelper.GetCore();
            // var sdkFolder = await core.FolderCreate(folderTranslations, property.FolderId);
            //
            // var folderProperty = new FolderProperty
            // {
            //     SdkFolderId = sdkFolder,
            //     FolderId = folder.Id,
            // };
            //
            // await folderProperty.Create(_caseTemplatePnDbContext);
        }

        return new OperationResult(true,
            _backendConfigurationLocalizationService.GetString("FolderCreatedSuccessfully"));
    }

    public async Task<OperationResult> UpdateFolder(BackendConfigurationDocumentFolderModel model)
    {
        var folder = await _caseTemplatePnDbContext.Folders
            .Include(x => x.FolderTranslations)
            .Include(x => x.FolderProperties)
            .FirstOrDefaultAsync(x => x.Id == model.Id).ConfigureAwait(false);

        if (folder == null)
        {
            return new OperationDataResult<BackendConfigurationDocumentFolderModel>(false,
                _backendConfigurationLocalizationService.GetString("FolderNotFound"));
        }

        foreach (var backendConfigurationDocumentFolderTranslationModel in model.DocumentFolderTranslations)
        {
            var translation = folder.FolderTranslations.FirstOrDefault(x => x.Id == backendConfigurationDocumentFolderTranslationModel.Id);
            if (translation == null)
            {
                translation = new FolderTranslation
                {
                    FolderId = folder.Id,
                    Name = backendConfigurationDocumentFolderTranslationModel.Name,
                    Description = backendConfigurationDocumentFolderTranslationModel.Description,
                    LanguageId = backendConfigurationDocumentFolderTranslationModel.LanguageId
                };
                await translation.Create(_caseTemplatePnDbContext).ConfigureAwait(false);
            }

            translation.Name = backendConfigurationDocumentFolderTranslationModel.Name;
            translation.Description = backendConfigurationDocumentFolderTranslationModel.Description;
            await translation.Update(_caseTemplatePnDbContext).ConfigureAwait(false);
        }

        return new OperationResult(true,
            _backendConfigurationLocalizationService.GetString("FolderUpdatedSuccessfully"));
    }

    public async Task<OperationResult> DeleteFolder(int id)
    {
        var descriptionFolder = await _caseTemplatePnDbContext.Folders
            .FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);

        if (descriptionFolder == null)
        {
            return new OperationResult(false,
                _backendConfigurationLocalizationService.GetString("DescriptionFolderNotFound"));
        }

        await descriptionFolder.Delete(_caseTemplatePnDbContext);

        return new OperationResult(true,
            _backendConfigurationLocalizationService.GetString("DescriptionFolderDeletedSuccessfully"));
    }
}