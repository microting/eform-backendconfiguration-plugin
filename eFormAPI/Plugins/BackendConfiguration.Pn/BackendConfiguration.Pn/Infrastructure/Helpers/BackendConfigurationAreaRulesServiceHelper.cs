using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Data.Seed.Data;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationAreaRulesServiceHelper
{
    public static async Task<OperationResult> Create(AreaRulesCreateModel createModel, eFormCore.Core core, int userId, BackendConfigurationPnDbContext _backendConfigurationPnDbContext, Language _language)
        {
            try
            {
                var areaProperty = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == createModel.PropertyAreaId)
                    .Select(x => new {x.Id, x.Area, x.GroupMicrotingUuid, x.PropertyId, x.ProperyAreaFolders})
                    .FirstAsync().ConfigureAwait(false);

                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.Id == areaProperty.PropertyId)
                    .SingleAsync().ConfigureAwait(false);

                var sdkDbContext = core.DbContextHelper.GetDbContext();

                foreach (var areaRuleCreateModel in createModel.AreaRules)
                {
                    var areaRuleType7 = new AreaRule();
                    if (areaProperty.Area.Type is AreaTypesEnum.Type7)
                    {
                        areaRuleType7 = BackendConfigurationSeedAreas.AreaRulesForType7
                            .First(x => x.AreaRuleTranslations
                                .Select(y => y.Name)
                                .Contains(areaRuleCreateModel.TranslatedNames[0].Name));
                    }
                    var areaRuleType8 = new AreaRule();
                    if (areaProperty.Area.Type is AreaTypesEnum.Type8)
                    {
                        areaRuleType8 = BackendConfigurationSeedAreas.AreaRulesForType8
                            .First(x => x.AreaRuleTranslations
                                .Select(y => y.Name)
                                .Contains(areaRuleCreateModel.TranslatedNames[0].Name));
                    }
                    var eformId = areaRuleCreateModel.TypeSpecificFields.EformId;
                    if (areaProperty.Area.Type is AreaTypesEnum.Type2 or AreaTypesEnum.Type6 or AreaTypesEnum.Type7 or AreaTypesEnum.Type8 or AreaTypesEnum.Type10)
                    {
                        var eformName = areaProperty.Area.Type switch
                        {
                            AreaTypesEnum.Type2 => "03. Kontrol konstruktion",
                            AreaTypesEnum.Type6 => "10. Varmepumpe serviceaftale",
                            AreaTypesEnum.Type7 => areaRuleType7.EformName,
                            AreaTypesEnum.Type8 => areaRuleType8.EformName,
                            AreaTypesEnum.Type10 => "01. Aflæsninger",
                            _ => ""
                        };
                        eformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == eformName)
                            .Select(x => x.CheckListId)
                            .FirstAsync().ConfigureAwait(false);
                    }

                    var areaRule = new AreaRule
                    {
                        AreaId = areaProperty.Area.Id,
                        UpdatedByUserId = userId,
                        CreatedByUserId = userId,
                        EformId = eformId,
                        PropertyId = areaProperty.PropertyId,
                    };

                    if (areaProperty.Area.Type is AreaTypesEnum.Type7)
                    {
                        areaRule.IsDefault = areaRuleType7.IsDefault;
                        // create folder
                        var pairedFolderToPropertyArea = areaProperty.ProperyAreaFolders.Select(x => x.FolderId).ToList();
                        var parentFolderId = await sdkDbContext.FolderTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.LanguageId == 2) // en
                            .Where(x => pairedFolderToPropertyArea.Contains(x.FolderId))
                            .Where(x => x.Name == areaRuleType7.FolderName)
                            .Select(x => x.FolderId)
                            .FirstAsync().ConfigureAwait(false);
                        areaRule.FolderId = parentFolderId;
                    }

                    if (areaRuleCreateModel.TypeSpecificFields != null)
                    {
                        areaRule.Type = areaRuleCreateModel.TypeSpecificFields.Type;
                        areaRule.DayOfWeek = areaRuleCreateModel.TypeSpecificFields.DayOfWeek ?? 0;
                        areaRule.Alarm = areaRuleCreateModel.TypeSpecificFields.Alarm;
                        areaRule.RepeatEvery = areaRuleCreateModel.TypeSpecificFields.RepeatEvery ?? 0;
                    }
                    areaRule.ComplianceEnabled = true;
                    areaRule.ComplianceModifiable = true;
                    areaRule.Notifications = true;
                    areaRule.NotificationsModifiable = true;

                    if (areaProperty.Area.Type is AreaTypesEnum.Type8)
                    {
                        areaRule.IsDefault = areaRuleType8.IsDefault;
                        // create folder
                        var pairedFolderToPropertyArea = areaProperty.ProperyAreaFolders.Select(x => x.FolderId).ToList();
                        var folderId = await sdkDbContext.FolderTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            // .Where(x => x.LanguageId == 2) // en
                            .Where(x => pairedFolderToPropertyArea.Contains(x.FolderId))
                            .Where(x => x.Name == areaRuleType8.FolderName)
                            .Select(x => x.FolderId)
                            .FirstAsync().ConfigureAwait(false);
                        areaRule.FolderId = folderId;
                        if (areaRuleType8.AreaRuleInitialField.RepeatEvery != null)
                        {
                            areaRule.RepeatEvery = (int) areaRuleType8.AreaRuleInitialField.RepeatEvery;
                        }
                        // areaRule.DayOfWeek = (int) areaRuleType8.AreaRuleInitialField.DayOfWeek;
                        areaRule.RepeatType = areaRuleType8.AreaRuleInitialField.RepeatType;
                        areaRule.ComplianceEnabled = areaRuleType8.AreaRuleInitialField.ComplianceEnabled;
                        areaRule.ComplianceModifiable = areaRuleType8.AreaRuleInitialField.ComplianceEnabled;
                        areaRule.Notifications = areaRuleType8.AreaRuleInitialField.Notifications;
                        areaRule.NotificationsModifiable = areaRuleType8.AreaRuleInitialField.Notifications;
                    }


                    if (eformId != 0)
                    {
                        areaRule.EformName = await sdkDbContext.CheckListTranslations
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.CheckListId == eformId)
                            .Where(x => x.LanguageId == _language.Id)
                            .Select(x => x.Text)
                            .FirstOrDefaultAsync().ConfigureAwait(false);
                    }

                    if (areaProperty.Area.Type != AreaTypesEnum.Type7 && areaProperty.Area.Type != AreaTypesEnum.Type8)
                    {
                        areaRule.FolderId = await _backendConfigurationPnDbContext.ProperyAreaFolders
                            .Include(x => x.AreaProperty)
                            .Where(x => x.AreaProperty.Id == createModel.PropertyAreaId)
                            .Select(x => x.FolderId)
                            .FirstOrDefaultAsync().ConfigureAwait(false);

                    }

                    areaRule.FolderName = await sdkDbContext.FolderTranslations
                        .Where(x => x.FolderId == areaRule.FolderId)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == _language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync().ConfigureAwait(false);

                    await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

                    var translations = new List<AreaRuleTranslation>();

                    if (areaProperty.Area.Type != AreaTypesEnum.Type7 && areaProperty.Area.Type != AreaTypesEnum.Type8)
                    {
                        translations = areaRuleCreateModel.TranslatedNames
                            .Select(x => new AreaRuleTranslation
                            {
                                AreaRuleId = areaRule.Id,
                                LanguageId = (int)x.Id,
                                Name = x.Name,
                                CreatedByUserId = userId,
                                UpdatedByUserId = userId,
                            }).ToList();
                    }

                    if (areaProperty.Area.Type is AreaTypesEnum.Type7)
                    {
                        translations = areaRuleType7.AreaRuleTranslations
                            .Select(x => new AreaRuleTranslation
                            {
                                AreaRuleId = areaRule.Id,
                                LanguageId = x.LanguageId,
                                Name = x.Name,
                                CreatedByUserId = userId,
                                UpdatedByUserId = userId,
                            }).ToList();
                    }

                    if (areaProperty.Area.Type is AreaTypesEnum.Type8)
                    {
                        translations = areaRuleType8.AreaRuleTranslations
                            .Select(x => new AreaRuleTranslation
                            {
                                AreaRuleId = areaRule.Id,
                                LanguageId = x.LanguageId,
                                Name = x.Name,
                                CreatedByUserId = userId,
                                UpdatedByUserId = userId,
                            }).ToList();
                    }

                    if (areaProperty.Area.Type is AreaTypesEnum.Type10)
                    {
                        foreach (var poolHourModel in areaRuleCreateModel.TypeSpecificFields.PoolHoursModel.Parrings)
                        {
                            var poolHour = new PoolHour
                            {
                                AreaRuleId = areaRule.Id,
                                DayOfWeek = (DayOfWeekEnum)(poolHourModel.DayOfWeek + 1),
                                Index = poolHourModel.Index,
                                IsActive = poolHourModel.IsActive,
                                CreatedByUserId = userId,
                                UpdatedByUserId = userId,
                                Name = poolHourModel.Name
                            };
                            await poolHour.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        var folderId = await core.FolderCreate(
                            areaRuleCreateModel.TranslatedNames.Select(x =>
                            {
                                var model = new Microting.eForm.Infrastructure.Models.CommonTranslationsModel
                                {
                                    Name = x.Name,
                                    LanguageId = (int)x.Id,
                                    Description = ""
                                };
                                return model;
                            }).ToList(),
                            areaRule.FolderId).ConfigureAwait(false);

                        var folderIds = new List<int>
                        {
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "7. Søn",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "7. Sun",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "7. Son",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "1. Man",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "1. Mon",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "1. Mon",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "2. Tir",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "2. Tue",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "2. Die",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "3. Ons",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "3. Wed",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "3. Mit",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "4. Tor",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "4. Thu",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "4. Don",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "5. Fre",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "5. Fri",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "5. Fre",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<Microting.eForm.Infrastructure.Models.CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "6. Lør",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "6. Sat",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "6. Sam",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                        };


                        foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                        {
                            FolderId = folderIdLocal,
                            ProperyAreaAsignmentId = areaProperty.Id,
                        }))
                        {
                            await assignmentWithFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }
                    }

                    foreach (var translation in translations)
                    {
                        await translation.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }
                }

                return new OperationDataResult<AreaRuleModel>(true,"SuccessfullyCreateAreaRule");
            }
            catch (Exception e)
            {
                // Log.LogException(e.Message);
                // Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaRuleModel>(false,"ErrorWhileCreateAreaRule");
            }
        }

}