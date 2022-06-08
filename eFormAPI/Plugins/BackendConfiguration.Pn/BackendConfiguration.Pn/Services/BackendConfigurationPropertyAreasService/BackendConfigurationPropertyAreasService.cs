/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

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

using System.IO;
using System.Reflection;
using eFormCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.BackendConfigurationPropertyAreasService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using BackendConfigurationLocalizationService;
    using Infrastructure.Data.Seed.Data;
    using Infrastructure.Models.PropertyAreas;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Dto;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eForm.Infrastructure.Models;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.eFormApi.BasePn.Infrastructure.Helpers;
    using Microting.eFormApi.BasePn.Infrastructure.Models.API;
    using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using CommonTranslationsModel = Microting.eForm.Infrastructure.Models.CommonTranslationsModel;

    public class BackendConfigurationPropertyAreasService : IBackendConfigurationPropertyAreasService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;
        private readonly IUserService _userService;
        private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;

        public BackendConfigurationPropertyAreasService(
            IEFormCoreService coreHelper,
            IUserService userService,
            BackendConfigurationPnDbContext backendConfigurationPnDbContext,
            IBackendConfigurationLocalizationService backendConfigurationLocalizationService,
            ItemsPlanningPnDbContext itemsPlanningPnDbContext)
        {
            _coreHelper = coreHelper;
            _userService = userService;
            _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
            _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        }

        public async Task<OperationDataResult<List<PropertyAreaModel>>> Read(int propertyId)
        {
            try
            {
                var propertyAreas = new List<PropertyAreaModel>();

                var propertyAreasQuery = _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyId == propertyId)
                    .Include(x => x.Area)
                    .ThenInclude(x => x.AreaRules)
                    .ThenInclude(x => x.AreaRulesPlannings);

                var areas = _backendConfigurationPnDbContext.Areas
                    .Include(x => x.AreaTranslations)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .ToList();

                List<PropertyAreaModel> areasForAdd;
                var language = await _userService.GetCurrentUserLanguage();
                if (propertyAreasQuery.Any())
                {
                    propertyAreas = await propertyAreasQuery
                        .Select(x => new PropertyAreaModel
                        {
                            Id = x.Id,
                            Activated = x.Checked,
                            Description = x.Area.AreaTranslations.Where(y => y.LanguageId == language.Id)
                                .Select(y => y.Description).FirstOrDefault(),
                            Name = x.Area.AreaTranslations.Where(y => y.LanguageId == language.Id).Select(y => y.Name)
                                .FirstOrDefault(),
                            Status = x.Area.AreaRules
                                .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                                .Where(y => y.PropertyId == propertyId)
                                .SelectMany(y => y.AreaRulesPlannings)
                                .Where(y => y.WorkflowState != Constants.WorkflowStates.Removed)
                                .Any(y => y.Status),
                            AreaId = x.AreaId,
                            Type = x.Area.Type,
                        })
                        .ToListAsync();
                    areasForAdd = areas.Where(x => !propertyAreasQuery.Any(y => y.AreaId == x.Id))
                        .Select(x => new PropertyAreaModel
                        {
                            Id = null,
                            Activated = false,
                            Description = x.AreaTranslations.Where(y => y.LanguageId == language.Id)
                                .Select(y => y.Description).FirstOrDefault(),
                            Name = x.AreaTranslations.Where(y => y.LanguageId == language.Id).Select(y => y.Name)
                                .FirstOrDefault(),
                            Status = false,
                            AreaId = x.Id,
                            Type = x.Type,
                        })
                        .ToList();
                }
                else
                {
                    areasForAdd = areas
                        .Select(x => new PropertyAreaModel
                        {
                            Id = null,
                            Activated = false,
                            Description = x.AreaTranslations.Where(y => y.LanguageId == language.Id)
                                .Select(y => y.Description).FirstOrDefault(),
                            Name = x.AreaTranslations.Where(y => y.LanguageId == language.Id).Select(y => y.Name)
                                .FirstOrDefault(),
                            Status = false,
                            AreaId = x.Id,
                            Type = x.Type,
                        })
                        .ToList();
                }

                propertyAreas.AddRange(areasForAdd);

                propertyAreas = propertyAreas.OrderBy(x => x.AreaId).ToList();

                return new OperationDataResult<List<PropertyAreaModel>>(true, propertyAreas);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<List<PropertyAreaModel>>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadPropertyAreas")}: {e.Message}");
            }
        }

        public async Task<OperationResult> Update(PropertyAreasUpdateModel updateModel)
        {
            try
            {
                updateModel.Areas = updateModel.Areas.Where(x => x.Activated).ToList();
                var assignments = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.PropertyId == updateModel.PropertyId)
                    .ToListAsync();

                var assignmentsForCreate = updateModel.Areas
                    .Where(x => x.Id == null)
                    .Where(x => x.Activated)
                    .ToList();

                var assignmentsForDelete = assignments
                    .Where(x => !updateModel.Areas.Where(y => y.Id.HasValue).Select(y => y.Id).Contains(x.Id))
                    .ToList();

                var core = await _coreHelper.GetCore();

                await using var sdkDbContext = core.DbContextHelper.GetDbContext();

                foreach (var assignmentForCreate in assignmentsForCreate)
                {
                    var area = await _backendConfigurationPnDbContext.Areas
                        .Include(x => x.AreaTranslations)
                        .FirstAsync(x => x.Id == assignmentForCreate.AreaId);

                    var newAssignment = new AreaProperty
                    {
                        CreatedByUserId = _userService.UserId,
                        UpdatedByUserId = _userService.UserId,
                        AreaId = assignmentForCreate.AreaId,
                        PropertyId = updateModel.PropertyId,
                        Checked = assignmentForCreate.Activated,
                    };
                    await newAssignment.Create(_backendConfigurationPnDbContext);

                    var property = await _backendConfigurationPnDbContext.Properties
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.Id == updateModel.PropertyId)
                        .FirstAsync();
                    switch (area.Type)
                    {
                        case AreaTypesEnum.Type3:
                        {
                            var folderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    // Name = "05. Halebid og klargøring af stalde",
                                    Name = "05. Stalde: Halebid og klargøring",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    // Name = "05. Tailbite and preparation of stables",
                                    Name = "05. Stables: Tail biting and preparation",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    // Name = "05. Stallungen",
                                    Name = "05. Ställe: Schwanzbeißen und Vorbereitung",
                                    Description = "",
                                },
                            }, property.FolderId);
                            var assignmentWithOneFolder = new ProperyAreaFolder
                            {
                                FolderId = folderId,
                                ProperyAreaAsignmentId = newAssignment.Id,
                            };

                            await assignmentWithOneFolder.Create(_backendConfigurationPnDbContext);
                            // await assignmentWithTwoFolder.Create(_backendConfigurationPnDbContext);

                            var groupCreate = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect, property.Name, "", true, false);
                            // TODO load tailbite eForm and seed it.
                            await SeedTailBite(property.Name, core, sdkDbContext, groupCreate.MicrotingUid);
                            newAssignment.GroupMicrotingUuid = Convert.ToInt32(groupCreate.MicrotingUid);
                            await newAssignment.Update(_backendConfigurationPnDbContext);
                            string text = $"05. Halebid og risikovurdering - {property.Name}";
                            foreach (var areaRule in BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                            {
                                areaRule.PropertyId = property.Id;
                                areaRule.FolderId = folderId;
                                areaRule.CreatedByUserId = _userService.UserId;
                                areaRule.UpdatedByUserId = _userService.UserId;
                                if (!string.IsNullOrEmpty(text))
                                {
                                    var eformId = await sdkDbContext.CheckListTranslations
                                        .Where(x => x.Text == text)
                                        .Select(x => x.CheckListId)
                                        .FirstAsync();
                                    areaRule.EformName = text;
                                    areaRule.EformId = eformId;
                                }
                                await areaRule.Create(_backendConfigurationPnDbContext);
                            }
                            break;
                        }
                        case AreaTypesEnum.Type5:
                        {
                            // create folder with stable name
                            var folderId = await core.FolderCreate(
                                area.AreaTranslations.Select(x => new CommonTranslationsModel
                                {
                                    Name = x.Name,
                                    LanguageId = x.LanguageId,
                                    Description = "",
                                }).ToList(),
                                property.FolderId);
                            //create 7 folders
                            var folderIds = new List<int>
                            {
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "20.07 Søndag",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "20.07 Sunday",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "20.07 Sonntag",
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "20.01 Mandag",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "20.01 Monday",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "20.01 Montag",
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "20.02 Tirsdag",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "20.02 Tuesday",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "20.02 Dienstag",
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "20.03 Onsdag",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "20.03 Wednesday",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "20.03 Mittwoch",
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "20.04 Torsdag",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "20.04 Thursday",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "20.04 Donnerstag",
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "20.05 Fredag",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "20.05 Friday",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "20.05 Freitag",
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "20.06 Lørdag",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "20.06 Saturday",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "20.06 Samstag",
                                        Description = "",
                                    },
                                }, folderId),
                            };

                            await new ProperyAreaFolder
                            {
                                FolderId = folderId,
                                ProperyAreaAsignmentId = newAssignment.Id,
                            }.Create(_backendConfigurationPnDbContext);

                            foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                            {
                                FolderId = folderIdLocal,
                                ProperyAreaAsignmentId = newAssignment.Id,
                            }))
                            {
                                await assignmentWithFolder.Create(_backendConfigurationPnDbContext);
                            }
                            foreach (var areaRule in BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                            {
                                areaRule.PropertyId = property.Id;
                                areaRule.FolderId = folderId;
                                areaRule.CreatedByUserId = _userService.UserId;
                                areaRule.UpdatedByUserId = _userService.UserId;
                                areaRule.ComplianceModifiable = true;
                                areaRule.NotificationsModifiable = true;
                                if (!string.IsNullOrEmpty(areaRule.EformName))
                                {
                                    var eformId = await sdkDbContext.CheckListTranslations
                                        .Where(x => x.Text == areaRule.EformName)
                                        .Select(x => x.CheckListId)
                                        .FirstAsync();
                                    areaRule.EformId = eformId;
                                }
                                await areaRule.Create(_backendConfigurationPnDbContext);
                            }
                            break;
                        }
                        case AreaTypesEnum.Type7:
                        {
                            // create folder with ie reporting name
                            var folderId = await core.FolderCreate(
                                area.AreaTranslations.Select(x => new CommonTranslationsModel
                                {
                                    Name = x.Name,
                                    LanguageId = x.LanguageId,
                                    Description = "",
                                }).ToList(),
                                property.FolderId);
                            //create 4 folders
                            var folderIds = new List<int>
                            {
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "23.00 Aflæsninger", // todo
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "23.00 Readings environmental management",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "23.00 Messungen Umweltmanagement", // todo
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "23.01 Logbøger miljøteknologier", // todo
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "23.01 Logbooks for any environmental technologies",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "23.01 Fahrtenbücher für alle Umwelttechnologien", // todo
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "23.02 Dokumentation afsluttede inspektioner", // todo
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "23.02 Documentation of completed inspections",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "23.02 Dokumentation abgeschlossener Inspektionen", // todo
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "23.03 Dokumentation miljøledelse", // todo
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "23.03 Documentation for environmental management",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "23.03 Dokumentation für das Umweltmanagement", // todo
                                        Description = "",
                                    },
                                }, folderId),
                                await core.FolderCreate(new List<CommonTranslationsModel>
                                {
                                    new()
                                    {
                                        LanguageId = 1, // da
                                        Name = "23.04 Overholdelse fodringskrav", // todo
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 2, // en
                                        Name = "23.04 Compliance with feeding requirements",
                                        Description = "",
                                    },
                                    new()
                                    {
                                        LanguageId = 3, // ge
                                        Name = "23.04 Einhaltung der Fütterungsanforderungen", // todo
                                        Description = "",
                                    },
                                }, folderId),
                            };

                            await new ProperyAreaFolder
                            {
                                FolderId = folderId,
                                ProperyAreaAsignmentId = newAssignment.Id,
                            }.Create(_backendConfigurationPnDbContext);

                            foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                            {
                                FolderId = folderIdLocal,
                                ProperyAreaAsignmentId = newAssignment.Id,
                            }))
                            {
                                await assignmentWithFolder.Create(_backendConfigurationPnDbContext);
                            }
                            foreach (var areaRule in BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                            {
                                areaRule.PropertyId = property.Id;
                                areaRule.FolderId = folderId;
                                areaRule.CreatedByUserId = _userService.UserId;
                                areaRule.UpdatedByUserId = _userService.UserId;
                                if (!string.IsNullOrEmpty(areaRule.EformName))
                                {
                                    var eformId = await sdkDbContext.CheckListTranslations
                                        .Where(x => x.Text == areaRule.EformName)
                                        .Select(x => x.CheckListId)
                                        .FirstAsync();
                                    areaRule.EformId = eformId;
                                }
                                await areaRule.Create(_backendConfigurationPnDbContext);
                            }
                            break;
                        }

                        case AreaTypesEnum.Type8:
                        {
                            // create folder with ie reporting name
                            var folderId = await core.FolderCreate(
                                area.AreaTranslations.Select(x => new CommonTranslationsModel
                                {
                                    Name = x.Name,
                                    LanguageId = x.LanguageId,
                                    Description = "",
                                }).ToList(),
                                property.FolderId);
                            //create 4 folders
                            var folderIds = new List<int>();

                            var subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.00 Aflæsninger", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.00 Readings environmental management",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.00 Messungen Umweltmanagement", // todo
                                    Description = "",
                                },
                            }, folderId);
                            folderIds.Add(subFolderId);
                            subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01 Logbøger miljøteknologier", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01 Logbooks for any environmental technologies",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01 Fahrtenbücher für alle Umwelttechnologien", // todo
                                    Description = "",
                                },
                            }, folderId);
                            folderIds.Add(subFolderId);
                            var subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.01 Gyllebeholdere", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.01 Manure containers",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.01 Güllebehälter", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.02 Gyllekøling", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.02 Slurry cooling",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.02 Schlammkühlung", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.03 Forsuring", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.03 Acidification",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.03 Versauerung", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.04 Ugentlig udslusning af gylle", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.04 Weekly slurry disposal",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.04 Wöchentliche Gülleentsorgung", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.05 Punktudsugning i slagtesvinestalde", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.05 Point extraction in fattening pig stables",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.05 Punktabsaugung in Mastschweineställen", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.06 Varmevekslere til traditionelle slagtekyllingestalde", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.06 Heat exchangers for traditional broiler houses",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.06 Wärmetauscher für traditionelle Masthähnchenställe", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.07 Gødningsbånd til æglæggende høns", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.07 Manure belt for laying hens",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.07 Kotband für Legehennen", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.08 Biologisk luftrensning", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.08 Biological air purification",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.08 Biologische Luftreinigung", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.01.09 Kemisk luftrensning", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.01.09 Chemical air purification",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.01.09 Chemische Luftreinigung", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02 Dokumentation afsluttede inspektioner", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02 Documentation of completed inspections",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02 Dokumentation abgeschlossener Inspektionen", // todo
                                    Description = "",
                                },
                            }, folderId);
                            folderIds.Add(subFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02.01 Visuel kontrol af tom gyllebeholdere", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02.01 Visual inspection of empty slurry tankers",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02.01 Sichtprüfung von leeren Güllefässern", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02.02 Gyllepumper", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02.02 Slurry pumps",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02.02 Schlammpumpen", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02.03 Forsyningssystemer til vand og foder", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02.03 Water and feed supply systems",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02.03 Wasser- und Futterversorgungssysteme", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02.04 Varme-, køle- og ventilationssystemer", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02.04 Heating, cooling and ventilation systems",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02.04 Heiz-, Kühl- und Lüftungssysteme", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv.", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02.05 Silos and equipment in transport equipment in connection with feed systems - pipes, augers, etc.",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02.05 Silos und Einrichtungen in Transporteinrichtungen in Verbindung mit Beschickungssystemen - Rohre, Schnecken usw.", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02.06 Luftrensningssystemer", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02.06 Air purification systems",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02.06 Luftreinigungssysteme", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02.07 Udstyr til drikkevand", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02.07 Equipment for drinking water",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02.07 Ausrüstung für Trinkwasser", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.02.08 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.02.08 Machines for application of livestock manure and dosing mechanism",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.02.08 Maschinen zum Ausbringen von Viehmist und Dosiermechanismus", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.03 Dokumentation miljøledelse", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.03 Documentation for environmental management",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.03 Dokumentation für das Umweltmanagement", // todo
                                    Description = "",
                                },
                            }, folderId);
                            folderIds.Add(subFolderId);
                            subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.04 Overholdelse fodringskrav", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.04 Compliance with feeding requirements",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.04 Einhaltung der Fütterungsanforderungen", // todo
                                    Description = "",
                                },
                            }, folderId);
                            folderIds.Add(subFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.04.01 Fasefodring", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.04.01 Phase feeding",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.04.01 Phasenfütterung", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.04.02 Reduceret indhold af råprotein", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.04.02 Reduced content of crude protein",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.04.02 Reduzierter Gehalt an Rohprotein", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);
                            subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = 1, // da
                                    Name = "24.04.03 Tilsætningsstoffer i foder - fytase eller andet", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 2, // en
                                    Name = "24.04.03 Additives in feed - phytase or other",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = 3, // ge
                                    Name = "24.04.03 Zusatzstoffe in Futtermitteln – Phytase oder andere", // todo
                                    Description = "",
                                },
                            }, subFolderId);
                            folderIds.Add(subSubFolderId);

                                await new ProperyAreaFolder
                            {
                                FolderId = folderId,
                                ProperyAreaAsignmentId = newAssignment.Id,
                            }.Create(_backendConfigurationPnDbContext);

                            foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                            {
                                FolderId = folderIdLocal,
                                ProperyAreaAsignmentId = newAssignment.Id,
                            }))
                            {
                                await assignmentWithFolder.Create(_backendConfigurationPnDbContext);
                            }
                            foreach (var areaRule in BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                            {
                                areaRule.PropertyId = property.Id;
                                areaRule.FolderId = folderId;
                                areaRule.CreatedByUserId = _userService.UserId;
                                areaRule.UpdatedByUserId = _userService.UserId;
                                if (!string.IsNullOrEmpty(areaRule.EformName))
                                {
                                    var eformId = await sdkDbContext.CheckListTranslations
                                        .Where(x => x.Text == areaRule.EformName)
                                        .Select(x => x.CheckListId)
                                        .FirstAsync();
                                    areaRule.EformId = eformId;
                                }
                                await areaRule.Create(_backendConfigurationPnDbContext);
                            }
                            break;
                        }
                        default:
                        {
                            var folderId = await core.FolderCreate(
                                area.AreaTranslations.Select(x => new CommonTranslationsModel
                                {
                                    Name = x.Name,
                                    LanguageId = x.LanguageId,
                                    Description = "",
                                }).ToList(),
                                property.FolderId);
                            var assignmentWithFolder = new ProperyAreaFolder
                            {
                                FolderId = folderId,
                                ProperyAreaAsignmentId = newAssignment.Id,
                            };
                            await assignmentWithFolder.Create(_backendConfigurationPnDbContext);
                            foreach (var areaRule in BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                            {
                                areaRule.PropertyId = property.Id;
                                areaRule.FolderId = folderId;
                                areaRule.CreatedByUserId = _userService.UserId;
                                areaRule.UpdatedByUserId = _userService.UserId;
                                areaRule.ComplianceModifiable = true;
                                areaRule.NotificationsModifiable = true;
                                if (!string.IsNullOrEmpty(areaRule.EformName))
                                {
                                    var eformId = await sdkDbContext.CheckListTranslations
                                        .Where(x => x.Text == areaRule.EformName)
                                        .Select(x => x.CheckListId)
                                        .FirstAsync();
                                    areaRule.EformId = eformId;
                                }
                                await areaRule.Create(_backendConfigurationPnDbContext);
                            }
                            break;
                        }
                    }
                }

                foreach (var areaPropertyForDelete in assignmentsForDelete)
                {
                    // get areaRules and select all linked entity for delete
                    var areaRules = await _backendConfigurationPnDbContext.AreaRules
                        .Where(x => x.PropertyId == areaPropertyForDelete.PropertyId)
                        .Where(x => x.AreaId == areaPropertyForDelete.AreaId)
                        .Include(x => x.Area)
                        .Include(x => x.AreaRuleTranslations)
                        .Include(x => x.AreaRulesPlannings)
                        .ThenInclude(x => x.PlanningSites)
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .ToListAsync();

                    foreach (var areaRule in areaRules)
                    {
                        if (areaRule.Area.Type is AreaTypesEnum.Type3 && areaRule.GroupItemId != 0)
                        {
                            // delete item from selectable list
                            var entityGroupItem = await sdkDbContext.EntityItems
                                .Where(x => x.Id == areaRule.GroupItemId).FirstOrDefaultAsync();
                            if (entityGroupItem != null)
                            {
                                await entityGroupItem.Delete(sdkDbContext);
                            }
                            Property property =
                                await _backendConfigurationPnDbContext.Properties
                                    .SingleOrDefaultAsync(x => x.Id == areaRule.PropertyId);
                            string eformName = $"05. Halebid og risikovurdering - {property.Name}";
                            var eformId = await sdkDbContext.CheckListTranslations
                                .Where(x => x.Text == eformName)
                                .Select(x => x.CheckListId)
                                .FirstAsync();
                            foreach (CheckListSite checkListSite in sdkDbContext.CheckListSites.Where(x =>
                                x.CheckListId == eformId))
                            {
                                await core.CaseDelete(checkListSite.MicrotingUid);
                            }
                        }

                        // delete translations for are rules
                        foreach (var areaRuleAreaRuleTranslation in areaRule.AreaRuleTranslations.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            areaRuleAreaRuleTranslation.UpdatedByUserId = _userService.UserId;
                            await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext);
                        }

                        // delete plannings area rules and items planning
                        foreach (var areaRulePlanning in areaRule.AreaRulesPlannings
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            foreach (var planningSite in areaRulePlanning.PlanningSites
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                            {
                                planningSite.UpdatedByUserId = _userService.UserId;
                                await planningSite.Delete(_backendConfigurationPnDbContext);
                            }

                            if (areaRulePlanning.ItemPlanningId != 0)
                            {
                                var planning = await _itemsPlanningPnDbContext.Plannings
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                                    .Include(x => x.NameTranslations)
                                    .FirstOrDefaultAsync();
                                if (planning != null)
                                {
                                    foreach (var translation in planning.NameTranslations
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                    {
                                        translation.UpdatedByUserId = _userService.UserId;
                                        await translation.Delete(_itemsPlanningPnDbContext);
                                    }

                                    planning.UpdatedByUserId = _userService.UserId;
                                    await planning.Delete(_itemsPlanningPnDbContext);

                                    var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                        .Where(x => x.PlanningId == planning.Id)
                                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                        .ToListAsync();
                                    foreach (PlanningCaseSite planningCaseSite in planningCaseSites)
                                    {
                                        planningCaseSite.UpdatedByUserId = _userService.UserId;
                                        await planningCaseSite.Delete(_itemsPlanningPnDbContext);
                                        var result =
                                            await sdkDbContext.Cases.SingleAsync(x =>
                                                x.Id == planningCaseSite.MicrotingSdkCaseId);
                                        if (result.MicrotingUid != null)
                                        {
                                            await core.CaseDelete((int)result.MicrotingUid);
                                        }
                                    }
                                }
                            }

                            areaRulePlanning.UpdatedByUserId = _userService.UserId;
                            await areaRulePlanning.Delete(_backendConfigurationPnDbContext);
                        }

                        // delete area rule
                        areaRule.UpdatedByUserId = _userService.UserId;
                        await areaRule.Delete(_backendConfigurationPnDbContext);
                    }

                    // delete entity select group. only for type 3(tail bite and stables)
                    if (areaPropertyForDelete.GroupMicrotingUuid != 0)
                    {
                        await core.EntityGroupDelete(areaPropertyForDelete.GroupMicrotingUuid.ToString());
                    }
                    areaPropertyForDelete.UpdatedByUserId = _userService.UserId;
                    await areaPropertyForDelete.Delete(_backendConfigurationPnDbContext);

                    var foldersIdForDelete = _backendConfigurationPnDbContext.ProperyAreaFolders
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.ProperyAreaAsignmentId == areaPropertyForDelete.Id)
                        .Select(x => x.FolderId)
                        .ToList();

                    foreach (var folderIdForDelete in foldersIdForDelete)
                    {
                        var folder = await sdkDbContext.Folders
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Id == folderIdForDelete)
                            .FirstAsync();
                        await folder.Delete(sdkDbContext);
                    }
                }

                return new OperationResult(true,
                    _backendConfigurationLocalizationService.GetString("SuccessfullyUpdatePropertyAreas"));
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileUpdatePropertyAreas")}: {e.Message}");
            }
        }

        public async Task<OperationDataResult<AreaModel>> ReadAreaByPropertyAreaId(int propertyAreaId)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContex = core.DbContextHelper.GetDbContext();
                var areaProperties = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == propertyAreaId)
                    .Include(x => x.Area)
                    //.ThenInclude(x => x.AreaInitialField)
                    .Include(x => x.Area.AreaTranslations)
                    .Include(x => x.Property)
                    .ThenInclude(x => x.SelectedLanguages)
                    .Include(x => x.Property.PropertyWorkers)
                    .Where(x => x.Area.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Property.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (areaProperties.Property.PropertyWorkers.All(
                    x => x.WorkflowState == Constants.WorkflowStates.Removed))
                {
                    return new OperationDataResult<AreaModel>(false,
                        _backendConfigurationLocalizationService.GetString("NotFoundPropertyWorkerAssignments"));
                }

                if (areaProperties.Area == null)
                {
                    return new OperationDataResult<AreaModel>(false,
                        _backendConfigurationLocalizationService.GetString("NotFoundArea"));
                }

                var sites = new List<SiteDto>();

                foreach (var worker in areaProperties.Property.PropertyWorkers
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.WorkerId))
                {
                    var site = await sdkDbContex.Sites
                        .Where(x => x.Id == worker)
                        .FirstAsync();
                    sites.Add(new SiteDto()
                    {
                        SiteId = worker,
                        SiteName = site.Name,
                    });
                }

                var languages = await sdkDbContex.Languages.AsNoTracking().ToListAsync();
                var language = await _userService.GetCurrentUserLanguage();

                var areaModel = new AreaModel
                {
                    Name = areaProperties.Area.AreaTranslations.Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name).FirstOrDefault(),
                    Id = areaProperties.AreaId,
                    Languages = areaProperties.Property.SelectedLanguages
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => new CommonDictionaryModel
                        {
                            Id = x.LanguageId,
                            Name = languages.First(y => y.Id == x.LanguageId).Name,
                        }).ToList(),
                    AvailableWorkers = sites,
                    Type = areaProperties.Area.Type,
                    InitialFields = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField != null
                        ? new AreaInitialFields
                        {
                            Alarm = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.Alarm,
                            DayOfWeek = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.DayOfWeek,
                            EformName = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.EformName,
                            SendNotifications = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.Notifications,
                            RepeatType = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.RepeatType,
                            Type = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.Type,
                            RepeatEvery = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.RepeatEvery,
                            EndDate = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.EndDate,
                            ComplianceEnabled = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaProperties.AreaId).AreaInitialField.ComplianceEnabled,
                        }
                        : null,
                    InfoBox = areaProperties.Area.AreaTranslations
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.InfoBox)
                        .FirstOrDefault(),
                    Placeholder = areaProperties.Area.AreaTranslations
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Placeholder)
                        .FirstOrDefault(),
                    NewItemName = areaProperties.Area.AreaTranslations
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.NewItemName)
                        .FirstOrDefault(),
                    GroupId = areaProperties.GroupMicrotingUuid,
                };
                if (areaModel.InitialFields != null && !string.IsNullOrEmpty(areaModel.InitialFields.EformName))
                {
                    areaModel.InitialFields.EformId = await sdkDbContex.CheckListTranslations
                        .Where(x => x.Text == areaModel.InitialFields.EformName)
                        .Select(x => x.CheckListId)
                        .FirstOrDefaultAsync();
                }

                return new OperationDataResult<AreaModel>(true, areaModel);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadArea")}: {e.Message}");
            }
        }

        public async Task<OperationDataResult<AreaModel>> ReadAreaByAreaRuleId(int areaRuleId)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var sdkDbContex = core.DbContextHelper.GetDbContext();

                var languages = await sdkDbContex.Languages.Select(x => new { x.Id, x.Name }).ToListAsync();
                var language = await _userService.GetCurrentUserLanguage();

                var areaRule = await _backendConfigurationPnDbContext.AreaRules
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == areaRuleId)
                    .Include(x => x.Area)
                    //.ThenInclude(x => x.AreaInitialField)
                    .Include(x => x.Area.AreaTranslations)
                    .Where(x => x.Area.WorkflowState != Constants.WorkflowStates.Removed)
                    .Include(x => x.Property)
                    .ThenInclude(x => x.SelectedLanguages)
                    .Include(x => x.Property.PropertyWorkers)
                    .Where(x => x.Property.WorkflowState != Constants.WorkflowStates.Removed)
                    .FirstOrDefaultAsync();

                if (areaRule == null)
                {
                    return new OperationDataResult<AreaModel>(false,
                        _backendConfigurationLocalizationService.GetString("AreaRuleNotFound"));
                }
                var sites = new List<SiteDto>();

                foreach (var worker in areaRule.Property.PropertyWorkers
                             .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).Select(x => x.WorkerId))
                {
                    var site = await sdkDbContex.Sites
                        .Where(x => x.Id == worker)
                        .FirstAsync();
                    sites.Add(new SiteDto()
                    {
                        SiteId = worker,
                        SiteName = site.Name,
                    });
                }

                var areaModel = new AreaModel
                {
                    Name = areaRule.Area.AreaTranslations.Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name).FirstOrDefault(),
                    Id = areaRule.AreaId,
                    Type = areaRule.Area.Type,
                    Languages = areaRule.Property.SelectedLanguages
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Select(x => new CommonDictionaryModel
                        {
                            Id = x.LanguageId,
                            Name = languages.First(y => y.Id == x.LanguageId).Name,
                        }).ToList(),
                    AvailableWorkers = sites,
                    InitialFields = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField != null
                        ? new AreaInitialFields
                        {
                            Alarm = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.Alarm,
                            DayOfWeek = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.DayOfWeek,
                            EformName = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.EformName,
                            SendNotifications = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.Notifications,
                            RepeatType = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.RepeatType,
                            Type = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.Type,
                            RepeatEvery = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.RepeatEvery,
                            EndDate = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.EndDate,
                            ComplianceEnabled = BackendConfigurationSeedAreas.AreasSeed.First(x => x.Id == areaRule.AreaId).AreaInitialField.ComplianceEnabled,
                        }
                        : null,
                };
                if (areaModel.InitialFields != null && !string.IsNullOrEmpty(areaModel.InitialFields.EformName))
                {
                    areaModel.InitialFields.EformId = await sdkDbContex.CheckListTranslations
                        .Where(x => x.Text == areaModel.InitialFields.EformName)
                        .Select(x => x.CheckListId)
                        .FirstOrDefaultAsync();
                }

                return new OperationDataResult<AreaModel>(true, areaModel);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationDataResult<AreaModel>(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileReadArea")}: {e.Message}");
            }
        }

        private async Task SeedTailBite(string propertyName, Core core, MicrotingDbContext sdkDbContext, string entityGroupId)
        {
            string text = $"05. Halebid og risikovurdering - {propertyName}";
            if (!await sdkDbContext.CheckListTranslations.AnyAsync(x => x.Text == text))
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceStream = assembly.GetManifestResourceStream($"BackendConfiguration.Pn.Resources.eForms.05. Halebid og risikovurdering.xml");

                string contents;
                using(var sr = new StreamReader(resourceStream))
                {
                    contents = await sr.ReadToEndAsync();
                }

                contents = contents.Replace("SOURCE_REPLACE_ME", entityGroupId);
                var mainElement = await core.TemplateFromXml(contents);
                mainElement.Label = text;
                mainElement.ElementList[0].Label = text;

                int clId = await core.TemplateCreate(mainElement);
                var cl = await sdkDbContext.CheckLists.SingleAsync(x => x.Id == clId);
                cl.IsLocked = true;
                cl.IsEditable = false;
                cl.IsDoneAtEditable = true;
                cl.ReportH1 = "05.Stalde: Halebid og klargøring";
                cl.ReportH2 = "05.01Halebid";
                cl.QuickSyncEnabled = 1;
                await cl.Update(sdkDbContext);
                var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id);
                subCl.QuickSyncEnabled = 1;
                await subCl.Update(sdkDbContext);

            }
        }


        public async Task<OperationResult> CreateEntityList(List<EntityItem> entityItemsListForCreate, int propertyAreaId)
        {
            try
            {
                var core = await _coreHelper.GetCore();
                var propertyArea = await _backendConfigurationPnDbContext.AreaProperties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == propertyAreaId)
                    .Include(x => x.Area)
                    .Include(x => x.Property)
                    .FirstOrDefaultAsync();
                if (propertyArea == null)
                {
                    return new OperationResult(false,
                        _backendConfigurationLocalizationService.GetString("AreaPropertyNotFound"));
                }
                var currentUserLanguage = await _userService.GetCurrentUserLanguage();
                var groupCreate = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                    $"{propertyArea.Property.Name} - {propertyArea.Area.AreaTranslations.Where(x => x.LanguageId == currentUserLanguage.Id).Select(x => x.Name).FirstOrDefault()}", "",
                    true, true);
                var entityGroup = await core.EntityGroupRead(groupCreate.MicrotingUid);
                var nextItemUid = entityGroup.EntityGroupItemLst.Count;
                foreach (var entityItem in entityItemsListForCreate)
                {
                    await core.EntitySelectItemCreate(entityGroup.Id, entityItem.Name, entityItem.DisplayIndex,
                        nextItemUid.ToString());
                    nextItemUid++;
                }

                propertyArea.GroupMicrotingUuid = Convert.ToInt32(entityGroup.MicrotingUUID);
                await propertyArea.Update(_backendConfigurationPnDbContext);

                return new OperationResult(true);
            }
            catch (Exception e)
            {
                Log.LogException(e.Message);
                Log.LogException(e.StackTrace);
                return new OperationResult(false,
                    $"{_backendConfigurationLocalizationService.GetString("ErrorWhileCreateEntityList")}: {e.Message}");
            }
        }
    }
}