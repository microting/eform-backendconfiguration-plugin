using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Data.Seed.Data;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationPropertyAreasServiceHelper
{
    public static async Task<OperationResult> Update(PropertyAreasUpdateModel updateModel, Core core,
        BackendConfigurationPnDbContext _backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext _itemsPlanningPnDbContext, int userId)
    {
        try
        {
            updateModel.Areas = updateModel.Areas.Where(x => x.Activated).ToList();
            var assignments = await _backendConfigurationPnDbContext.AreaProperties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == updateModel.PropertyId)
                .ToListAsync().ConfigureAwait(false);

            var assignmentsForCreate = updateModel.Areas
                .Where(x => x.Id == null)
                .Where(x => x.Activated)
                .ToList();

            var assignmentsForDelete = assignments
                .Where(x => !updateModel.Areas.Where(y => y.Id.HasValue).Select(y => y.Id).Contains(x.Id))
                .ToList();

            //var core = await _coreHelper.GetCore().ConfigureAwait(false);

            var sdkDbContext = core.DbContextHelper.GetDbContext();
            await using var _ = sdkDbContext.ConfigureAwait(false);

            foreach (var assignmentForCreate in assignmentsForCreate)
            {
                var area = await _backendConfigurationPnDbContext.Areas
                    .Include(x => x.AreaTranslations)
                    .FirstAsync(x => x.Id == assignmentForCreate.AreaId).ConfigureAwait(false);

                var newAssignment = new AreaProperty
                {
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId,
                    AreaId = assignmentForCreate.AreaId,
                    PropertyId = updateModel.PropertyId,
                    Checked = assignmentForCreate.Activated,
                };
                await newAssignment.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

                var property = await _backendConfigurationPnDbContext.Properties
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.Id == updateModel.PropertyId)
                    .FirstAsync().ConfigureAwait(false);

                var danishLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "da");
                var englishLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "en-US");
                var germanLanguage = await sdkDbContext.Languages.FirstAsync(x => x.LanguageCode == "de-DE");

                switch (area.Type)
                {
                    case AreaTypesEnum.Type9:
                    {
                        var folderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                // Name = "05. Halebid og klargøring af stalde",
                                Name = "25. KemiKontrol",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                // Name = "05. Tailbite and preparation of stables",
                                Name = "25. Chemistry Control",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                // Name = "05. Stallungen",
                                Name = "25. Chemiekontrolle",
                                Description = "",
                            },
                        }, property.FolderId).ConfigureAwait(false);
                        var assignmentWithOneFolder = new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id,
                        };
                        await assignmentWithOneFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

                        var folderIds = new List<int>
                        {
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "25.01 Opret kemiprodukt", // todo
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "25.01 Create chemical product",
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "25.01 Chemisches Produkt herstellen", // todo
                                    Description = property.Name,
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "25.02 Udløber i dag eller er udløbet", // todo
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "25.02 Expires today or has expired",
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "25.02 Läuft heute ab oder ist abgelaufen", // todo
                                    Description = property.Name,
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "25.03 Udløber om senest 1 mdr.", // todo
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "25.03 Expires in 1 month at the latest",
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "25.03 Läuft spätestens in 1 Monat ab", // todo
                                    Description = property.Name,
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "25.04 Udløber om senest 3 mdr.", // todo
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "25.04 Expires in 3 months at the latest",
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "25.04 Läuft spätestens in 3 Monaten ab", // todo
                                    Description = property.Name,
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "25.05 Udløber om senest 6 mdr.", // todo
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "25.05 Expires in 6 months at the latest",
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "25.05 Läuft spätestens in 6 Monaten ab", // todo
                                    Description = property.Name,
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "25.06 Udløber om senest 12 mdr.", // todo
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "25.06 Expires in 12 months at the latest",
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "25.06 Läuft spätestens in 12 Monaten ab", // todo
                                    Description = property.Name,
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "25.07 Udløber om mere end 12 mdr.", // todo
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "25.07 Expires in more than 12 months.",
                                    Description = property.Name,
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "25.07 Läuft in mehr als 12 Monaten ab.", // todo
                                    Description = property.Name,
                                },
                            }, folderId).ConfigureAwait(false)
                        };

                        foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                                 {
                                     FolderId = folderIdLocal,
                                     ProperyAreaAsignmentId = newAssignment.Id,
                                 }))
                        {
                            await assignmentWithFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }


                        // var groupCreate = await core.EntityGroupCreate(Constants.FieldTypes.EntitySearch, $"Chemicals - Barcode - {property.Name}", "", true, false).ConfigureAwait(false);
                        // property.EntitySearchListChemicals = Convert.ToInt32(groupCreate.MicrotingUid);
                        // groupCreate = await core.EntityGroupCreate(Constants.FieldTypes.EntitySearch, $"Chemicals - Regno - {property.Name}", "", true, false).ConfigureAwait(false);
                        // property.EntitySearchListChemicalRegNos = Convert.ToInt32(groupCreate.MicrotingUid);
                        var groupCreate = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                            $"Chemicals - Areas - {property.Name}", "", true, false).ConfigureAwait(false);
                        property.EntitySelectListChemicalAreas = Convert.ToInt32(groupCreate.MicrotingUid);
                        await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        string text = "25.01 Registrer produkter";

                        var eformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == text)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Select(x => x.CheckListId)
                            .FirstAsync().ConfigureAwait(false);

                        var areaRule = new AreaRule
                        {
                            PropertyId = property.Id,
                            FolderId = folderId,
                            CreatedByUserId = userId,
                            UpdatedByUserId = userId,
                            EformId = eformId,
                            EformName = text,
                            AreaId = area.Id
                        };
                        AreaInitialField areaInitialField = await _backendConfigurationPnDbContext.AreaInitialFields
                            .Where(x => x.AreaId == area.Id).FirstOrDefaultAsync().ConfigureAwait(false);
                        areaInitialField.EformName = text;
                        await areaInitialField.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        AreaRuleInitialField areaRuleInitialField = new AreaRuleInitialField
                        {
                            AreaRuleId = areaRule.Id,
                            Notifications = false,
                            ComplianceEnabled = false,
                            RepeatEvery = 0,
                            RepeatType = 0,
                            EformName = text,
                        };
                        await areaRuleInitialField.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        break;
                    }
                    case AreaTypesEnum.Type10:
                    {
                        var groupCreate = await core.EntityGroupCreate(Constants.FieldTypes.EntitySearch,
                                $"00. Aflæsninger, målinger, forbrug og fækale uheld - {property.Name}", "", true,
                                false)
                            .ConfigureAwait(false);
                        await SeedPoolEform(property.Name, core, sdkDbContext, groupCreate.MicrotingUid)
                            .ConfigureAwait(false);
                        await SeedFaeceseForm(property.Name, core, sdkDbContext, groupCreate.MicrotingUid)
                            .ConfigureAwait(false);
                        property.EntitySearchListPoolWorkers = int.Parse(groupCreate.MicrotingUid);
                        await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        var folderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                // Name = "05. Halebid og klargøring af stalde",
                                Name = "00. Aflæsninger, målinger, forbrug og fækale uheld",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                // Name = "05. Tailbite and preparation of stables",
                                Name = "00. Readings, measurements, consumption and fecal accidents",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                // Name = "05. Stallungen",
                                Name = "00. Messwerte, Messungen, Verbrauch und Fäkalunfälle",
                                Description = "",
                            },
                        }, property.FolderId).ConfigureAwait(false);
                        var assignmentWithOneFolder = new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id,
                        };

                        await assignmentWithOneFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        break;
                    }
                    case AreaTypesEnum.Type3:
                    {
                        var folderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                // Name = "05. Halebid og klargøring af stalde",
                                Name = "05. Stalde: Halebid og klargøring",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                // Name = "05. Tailbite and preparation of stables",
                                Name = "05. Stables: Tail biting and preparation",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                // Name = "05. Stallungen",
                                Name = "05. Ställe: Schwanzbeißen und Vorbereitung",
                                Description = "",
                            },
                        }, property.FolderId).ConfigureAwait(false);
                        var assignmentWithOneFolder = new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id,
                        };

                        await assignmentWithOneFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        // await assignmentWithTwoFolder.Create(_backendConfigurationPnDbContext);

                        var groupCreate = await core
                            .EntityGroupCreate(Constants.FieldTypes.EntitySelect, property.Name, "", true, false)
                            .ConfigureAwait(false);
                        // TODO load tailbite eForm and seed it.
                        await SeedTailBite(property.Name, core, sdkDbContext, groupCreate.MicrotingUid)
                            .ConfigureAwait(false);
                        newAssignment.GroupMicrotingUuid = Convert.ToInt32(groupCreate.MicrotingUid);
                        await newAssignment.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        string text = $"05. Halebid og risikovurdering - {property.Name}";
                        foreach (var areaRule in
                                 BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                        {
                            areaRule.PropertyId = property.Id;
                            areaRule.FolderId = folderId;
                            areaRule.CreatedByUserId = userId;
                            areaRule.UpdatedByUserId = userId;
                            if (!string.IsNullOrEmpty(text))
                            {
                                var eformId = await sdkDbContext.CheckListTranslations
                                    .Where(x => x.Text == text)
                                    .Select(x => x.CheckListId)
                                    .FirstAsync().ConfigureAwait(false);
                                areaRule.EformName = text;
                                areaRule.EformId = eformId;
                            }

                            await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                            property.FolderId).ConfigureAwait(false);
                        //create 7 folders
                        var folderIds = new List<int>
                        {
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "20.07 Søndag",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "20.07 Sunday",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "20.07 Sonntag",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "20.01 Mandag",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "20.01 Monday",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "20.01 Montag",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "20.02 Tirsdag",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "20.02 Tuesday",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "20.02 Dienstag",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "20.03 Onsdag",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "20.03 Wednesday",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "20.03 Mittwoch",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "20.04 Torsdag",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "20.04 Thursday",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "20.04 Donnerstag",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "20.05 Fredag",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "20.05 Friday",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "20.05 Freitag",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "20.06 Lørdag",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "20.06 Saturday",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "20.06 Samstag",
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                        };

                        await new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id,
                        }.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

                        foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                                 {
                                     FolderId = folderIdLocal,
                                     ProperyAreaAsignmentId = newAssignment.Id,
                                 }))
                        {
                            await assignmentWithFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        foreach (var areaRule in
                                 BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                        {
                            areaRule.PropertyId = property.Id;
                            areaRule.FolderId = folderId;
                            areaRule.CreatedByUserId = userId;
                            areaRule.UpdatedByUserId = userId;
                            areaRule.ComplianceModifiable = true;
                            areaRule.NotificationsModifiable = true;
                            if (!string.IsNullOrEmpty(areaRule.EformName))
                            {
                                var eformId = await sdkDbContext.CheckListTranslations
                                    .Where(x => x.Text == areaRule.EformName)
                                    .Select(x => x.CheckListId)
                                    .FirstAsync().ConfigureAwait(false);
                                areaRule.EformId = eformId;
                            }

                            await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                            property.FolderId).ConfigureAwait(false);
                        //create 4 folders
                        var folderIds = new List<int>
                        {
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "23.00 Aflæsninger", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "23.00 Readings environmental management",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "23.00 Messungen Umweltmanagement", // todo
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "23.01 Logbøger miljøteknologier", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "23.01 Logbooks for any environmental technologies",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "23.01 Fahrtenbücher für alle Umwelttechnologien", // todo
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "23.02 Dokumentation afsluttede inspektioner", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "23.02 Documentation of completed inspections",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "23.02 Dokumentation abgeschlossener Inspektionen", // todo
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "23.03 Dokumentation miljøledelse", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "23.03 Documentation for environmental management",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "23.03 Dokumentation für das Umweltmanagement", // todo
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                            await core.FolderCreate(new List<CommonTranslationsModel>
                            {
                                new()
                                {
                                    LanguageId = danishLanguage.Id, // da
                                    Name = "23.04 Overholdelse fodringskrav", // todo
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = englishLanguage.Id, // en
                                    Name = "23.04 Compliance with feeding requirements",
                                    Description = "",
                                },
                                new()
                                {
                                    LanguageId = germanLanguage.Id, // ge
                                    Name = "23.04 Einhaltung der Fütterungsanforderungen", // todo
                                    Description = "",
                                },
                            }, folderId).ConfigureAwait(false),
                        };

                        await new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id,
                        }.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

                        foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                                 {
                                     FolderId = folderIdLocal,
                                     ProperyAreaAsignmentId = newAssignment.Id,
                                 }))
                        {
                            await assignmentWithFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        foreach (var areaRule in
                                 BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                        {
                            areaRule.PropertyId = property.Id;
                            areaRule.FolderId = folderId;
                            areaRule.CreatedByUserId = userId;
                            areaRule.UpdatedByUserId = userId;
                            if (!string.IsNullOrEmpty(areaRule.EformName))
                            {
                                var eformId = await sdkDbContext.CheckListTranslations
                                    .Where(x => x.Text == areaRule.EformName)
                                    .Select(x => x.CheckListId)
                                    .FirstAsync().ConfigureAwait(false);
                                areaRule.EformId = eformId;
                            }

                            await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                            property.FolderId).ConfigureAwait(false);
                        //create 4 folders
                        var folderIds = new List<int>();

                        var subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.00 Aflæsninger", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.00 Readings environmental management",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.00 Messungen Umweltmanagement", // todo
                                Description = "",
                            },
                        }, folderId).ConfigureAwait(false);
                        folderIds.Add(subFolderId);
                        subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01 Logbøger og bilag", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01 Logbooks and appendices",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01 Logbücher und Anhänge", // todo
                                Description = "",
                            },
                        }, folderId).ConfigureAwait(false);
                        folderIds.Add(subFolderId);
                        var subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.01 Gyllebeholdere", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.01 Manure containers",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.01 Güllebehälter", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.02 Gyllekøling", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.02 Slurry cooling",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.02 Schlammkühlung", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.03 Forsuring", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.03 Acidification",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.03 Versauerung", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.04 Ugentlig udslusning af gylle", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.04 Weekly slurry disposal",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.04 Wöchentliche Gülleentsorgung", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.05 Punktudsugning i slagtesvinestalde", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.05 Point extraction in fattening pig stables",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.05 Punktabsaugung in Mastschweineställen", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.06 Varmevekslere til traditionelle slagtekyllingestalde", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.06 Heat exchangers for traditional broiler houses",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.06 Wärmetauscher für traditionelle Masthähnchenställe", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.07 Gødningsbånd til æglæggende høns", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.07 Manure belt for laying hens",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.07 Kotband für Legehennen", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.08 Biologisk luftrensning", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.08 Biological air purification",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.08 Biologische Luftreinigung", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.01.09 Kemisk luftrensning", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.01.09 Chemical air purification",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.01.09 Chemische Luftreinigung", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.02 Kontroller og bilag", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.02 Checks and attachments",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.02 Schecks und Anhänge", // todo
                                Description = "",
                            },
                        }, folderId).ConfigureAwait(false);
                        folderIds.Add(subFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.02.01 Visuel kontrol af tom gyllebeholdere", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.02.01 Visual inspection of empty slurry tankers",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.02.01 Sichtprüfung von leeren Güllefässern", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.02.02 Gyllepumper", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.02.02 Slurry pumps",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.02.02 Schlammpumpen", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.02.03 Forsyningssystemer til vand og foder", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.02.03 Water and feed supply systems",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.02.03 Wasser- und Futterversorgungssysteme", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.02.04 Varme-, køle- og ventilationssystemer", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.02.04 Heating, cooling and ventilation systems",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.02.04 Heiz-, Kühl- und Lüftungssysteme", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name =
                                    "24.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv.", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name =
                                    "24.02.05 Silos and equipment in transport equipment in connection with feed systems - pipes, augers, etc.",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name =
                                    "24.02.05 Silos und Einrichtungen in Transporteinrichtungen in Verbindung mit Beschickungssystemen - Rohre, Schnecken usw.", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.02.06 Luftrensningssystemer", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.02.06 Air purification systems",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.02.06 Luftreinigungssysteme", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.02.07 Udstyr til drikkevand", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.02.07 Equipment for drinking water",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.02.07 Ausrüstung für Trinkwasser", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name =
                                    "24.02.08 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.02.08 Machines for application of livestock manure and dosing mechanism",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.02.08 Maschinen zum Ausbringen von Viehmist und Dosiermechanismus", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.03 Miljøledelse", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.03 Environmental management",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.03 Umweltmanagement", // todo
                                Description = "",
                            },
                        }, folderId).ConfigureAwait(false);
                        folderIds.Add(subFolderId);
                        subFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.04 Fodringskrav", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.04 Feeding requirements",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.04 Fütterungsanforderungen", // todo
                                Description = "",
                            },
                        }, folderId).ConfigureAwait(false);
                        folderIds.Add(subFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.04.01 Fasefodring", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.04.01 Phase feeding",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.04.01 Phasenfütterung", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.04.02 Reduceret indhold af råprotein", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.04.02 Reduced content of crude protein",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.04.02 Reduzierter Gehalt an Rohprotein", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);
                        subSubFolderId = await core.FolderCreate(new List<CommonTranslationsModel>
                        {
                            new()
                            {
                                LanguageId = danishLanguage.Id, // da
                                Name = "24.04.03 Tilsætningsstoffer i foder - fytase eller andet", // todo
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = englishLanguage.Id, // en
                                Name = "24.04.03 Additives in feed - phytase or other",
                                Description = "",
                            },
                            new()
                            {
                                LanguageId = germanLanguage.Id, // ge
                                Name = "24.04.03 Zusatzstoffe in Futtermitteln – Phytase oder andere", // todo
                                Description = "",
                            },
                        }, subFolderId).ConfigureAwait(false);
                        folderIds.Add(subSubFolderId);

                        await new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id,
                        }.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);

                        foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                                 {
                                     FolderId = folderIdLocal,
                                     ProperyAreaAsignmentId = newAssignment.Id,
                                 }))
                        {
                            await assignmentWithFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        foreach (var areaRule in
                                 BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                        {
                            areaRule.PropertyId = property.Id;
                            areaRule.FolderId = folderId;
                            areaRule.CreatedByUserId = userId;
                            areaRule.UpdatedByUserId = userId;
                            if (!string.IsNullOrEmpty(areaRule.EformName))
                            {
                                var eformId = await sdkDbContext.CheckListTranslations
                                    .Where(x => x.Text == areaRule.EformName)
                                    .Select(x => x.CheckListId)
                                    .FirstAsync().ConfigureAwait(false);
                                areaRule.EformId = eformId;
                            }

                            await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                            property.FolderId).ConfigureAwait(false);
                        var assignmentWithFolder = new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id,
                        };
                        await assignmentWithFolder.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        foreach (var areaRule in
                                 BackendConfigurationSeedAreas.AreaRules.Where(x => x.AreaId == area.Id))
                        {
                            areaRule.PropertyId = property.Id;
                            areaRule.FolderId = folderId;
                            areaRule.CreatedByUserId = userId;
                            areaRule.UpdatedByUserId = userId;
                            areaRule.ComplianceModifiable = true;
                            areaRule.NotificationsModifiable = true;
                            if (!string.IsNullOrEmpty(areaRule.EformName))
                            {
                                var eformId = await sdkDbContext.CheckListTranslations
                                    .Where(x => x.Text == areaRule.EformName)
                                    .Select(x => x.CheckListId)
                                    .FirstAsync().ConfigureAwait(false);
                                areaRule.EformId = eformId;
                            }

                            await areaRule.Create(_backendConfigurationPnDbContext).ConfigureAwait(false);
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
                    .ToListAsync().ConfigureAwait(false);

                foreach (var areaRule in areaRules)
                {
                    if (areaRule.Area.Type is AreaTypesEnum.Type3 && areaRule.GroupItemId != 0)
                    {
                        // delete item from selectable list
                        var entityGroupItem = await sdkDbContext.EntityItems
                            .Where(x => x.Id == areaRule.GroupItemId).FirstOrDefaultAsync().ConfigureAwait(false);
                        if (entityGroupItem != null)
                        {
                            await entityGroupItem.Delete(sdkDbContext).ConfigureAwait(false);
                        }

                        Property property =
                            await _backendConfigurationPnDbContext.Properties
                                .SingleOrDefaultAsync(x => x.Id == areaRule.PropertyId).ConfigureAwait(false);
                        string eformName = $"05. Halebid og risikovurdering - {property.Name}";
                        var eformId = await sdkDbContext.CheckListTranslations
                            .Where(x => x.Text == eformName)
                            .Select(x => x.CheckListId)
                            .FirstAsync().ConfigureAwait(false);
                        foreach (CheckListSite checkListSite in sdkDbContext.CheckListSites.Where(x =>
                                     x.CheckListId == eformId))
                        {
                            await core.CaseDelete(checkListSite.MicrotingUid).ConfigureAwait(false);
                        }
                    }

                    // delete translations for are rules
                    foreach (var areaRuleAreaRuleTranslation in areaRule.AreaRuleTranslations.Where(x =>
                                 x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        areaRuleAreaRuleTranslation.UpdatedByUserId = userId;
                        await areaRuleAreaRuleTranslation.Delete(_backendConfigurationPnDbContext)
                            .ConfigureAwait(false);
                    }

                    // delete plannings area rules and items planning
                    foreach (var areaRulePlanning in areaRule.AreaRulesPlannings
                                 .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                    {
                        foreach (var planningSite in areaRulePlanning.PlanningSites
                                     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                        {
                            planningSite.UpdatedByUserId = userId;
                            await planningSite.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        if (areaRulePlanning.ItemPlanningId != 0)
                        {
                            var planning = await _itemsPlanningPnDbContext.Plannings
                                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                .Where(x => x.Id == areaRulePlanning.ItemPlanningId)
                                .Include(x => x.NameTranslations)
                                .FirstOrDefaultAsync().ConfigureAwait(false);
                            if (planning != null)
                            {
                                foreach (var translation in planning.NameTranslations
                                             .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
                                {
                                    translation.UpdatedByUserId = userId;
                                    await translation.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                }

                                planning.UpdatedByUserId = userId;
                                await planning.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);

                                var planningCaseSites = await _itemsPlanningPnDbContext.PlanningCaseSites
                                    .Where(x => x.PlanningId == planning.Id)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToListAsync().ConfigureAwait(false);
                                foreach (PlanningCaseSite planningCaseSite in planningCaseSites)
                                {
                                    planningCaseSite.UpdatedByUserId = userId;
                                    await planningCaseSite.Delete(_itemsPlanningPnDbContext).ConfigureAwait(false);
                                    var result =
                                        await sdkDbContext.Cases.SingleAsync(x =>
                                            x.Id == planningCaseSite.MicrotingSdkCaseId).ConfigureAwait(false);
                                    if (result.MicrotingUid != null)
                                    {
                                        await core.CaseDelete((int)result.MicrotingUid).ConfigureAwait(false);
                                    }
                                }
                            }
                        }

                        areaRulePlanning.UpdatedByUserId = userId;
                        await areaRulePlanning.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                    }

                    // delete area rule
                    areaRule.UpdatedByUserId = userId;
                    await areaRule.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                // delete entity select group. only for type 3(tail bite and stables)
                if (areaPropertyForDelete.GroupMicrotingUuid != 0)
                {
                    await core.EntityGroupDelete(areaPropertyForDelete.GroupMicrotingUuid.ToString())
                        .ConfigureAwait(false);
                }

                areaPropertyForDelete.UpdatedByUserId = userId;
                await areaPropertyForDelete.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

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
                        .FirstAsync().ConfigureAwait(false);
                    await folder.Delete(sdkDbContext).ConfigureAwait(false);
                }
            }

            return new OperationResult(true, "SuccessfullyUpdatePropertyAreas");
        }
        catch (Exception e)
        {
            //Log.LogException(e.Message);
            //Log.LogException(e.StackTrace);
            return new OperationResult(false, "ErrorWhileUpdatePropertyAreas");
        }
    }

    private static async Task SeedTailBite(string propertyName, Core core, MicrotingDbContext sdkDbContext,
        string entityGroupId)
    {
        string text =
            $"05. Halebid og risikovurdering - {propertyName}|05. Tail bite and risc assessment - {propertyName}";
        if (!await sdkDbContext.CheckListTranslations.AnyAsync(x => x.Text == text).ConfigureAwait(false))
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream =
                assembly.GetManifestResourceStream(
                    $"BackendConfiguration.Pn.Resources.eForms.05. Halebid og risikovurdering.xml");

            string contents;
            using (var sr = new StreamReader(resourceStream))
            {
                contents = await sr.ReadToEndAsync().ConfigureAwait(false);
            }

            contents = contents.Replace("SOURCE_REPLACE_ME", entityGroupId);
            var mainElement = await core.TemplateFromXml(contents).ConfigureAwait(false);
            mainElement.Label = text;
            mainElement.ElementList[0].Label = text;

            int clId = await core.TemplateCreate(mainElement).ConfigureAwait(false);
            var cl = await sdkDbContext.CheckLists.SingleAsync(x => x.Id == clId).ConfigureAwait(false);
            cl.IsLocked = true;
            cl.IsEditable = false;
            cl.IsDoneAtEditable = true;
            cl.ReportH1 = "05.Stalde: Halebid og klargøring";
            cl.ReportH2 = "05.01Halebid";
            cl.QuickSyncEnabled = 1;
            await cl.Update(sdkDbContext).ConfigureAwait(false);
            var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id).ConfigureAwait(false);
            subCl.QuickSyncEnabled = 1;
            await subCl.Update(sdkDbContext).ConfigureAwait(false);

        }
    }

    private static async Task SeedPoolEform(string propertyName, Core core, MicrotingDbContext sdkDbContext,
        string entityGroupId)
    {
        string text = $"01. Aflæsninger - {propertyName}";
        if (!await sdkDbContext.CheckListTranslations.AnyAsync(x => x.Text == text).ConfigureAwait(false))
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream =
                assembly.GetManifestResourceStream($"BackendConfiguration.Pn.Resources.eForms.01. Aflæsninger.xml");

            string contents;
            using (var sr = new StreamReader(resourceStream))
            {
                contents = await sr.ReadToEndAsync().ConfigureAwait(false);
            }

            contents = contents.Replace("REPLACE_ME", entityGroupId);
            var mainElement = await core.TemplateFromXml(contents).ConfigureAwait(false);
            mainElement.Label = text;
            mainElement.ElementList[0].Label = text;

            int clId = await core.TemplateCreate(mainElement).ConfigureAwait(false);
            var cl = await sdkDbContext.CheckLists.SingleAsync(x => x.Id == clId).ConfigureAwait(false);
            cl.IsLocked = true;
            cl.IsEditable = false;
            cl.IsDoneAtEditable = true;
            //cl.ReportH1 = "05.Stalde: Halebid og klargøring";
            //cl.ReportH2 = "05.01Halebid";
            cl.QuickSyncEnabled = 1;
            await cl.Update(sdkDbContext).ConfigureAwait(false);
            var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id).ConfigureAwait(false);
            subCl.QuickSyncEnabled = 1;
            await subCl.Update(sdkDbContext).ConfigureAwait(false);

        }
    }

    private static async Task SeedFaeceseForm(string propertyName, Core core, MicrotingDbContext sdkDbContext,
        string entityGroupId)
    {
        string text = $"02. Fækale uheld - {propertyName}";
        if (!await sdkDbContext.CheckListTranslations.AnyAsync(x => x.Text == text).ConfigureAwait(false))
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceStream =
                assembly.GetManifestResourceStream($"BackendConfiguration.Pn.Resources.eForms.02. Fækale uheld.xml");

            string contents;
            using (var sr = new StreamReader(resourceStream))
            {
                contents = await sr.ReadToEndAsync().ConfigureAwait(false);
            }

            contents = contents.Replace("REPLACE_ME", entityGroupId);
            var mainElement = await core.TemplateFromXml(contents).ConfigureAwait(false);
            mainElement.Label = text;
            mainElement.ElementList[0].Label = text;

            int clId = await core.TemplateCreate(mainElement).ConfigureAwait(false);
            var cl = await sdkDbContext.CheckLists.SingleAsync(x => x.Id == clId).ConfigureAwait(false);
            cl.IsLocked = true;
            cl.IsEditable = false;
            cl.IsDoneAtEditable = true;
            //cl.ReportH1 = "05.Stalde: Halebid og klargøring";
            //cl.ReportH2 = "05.01Halebid";
            cl.QuickSyncEnabled = 1;
            await cl.Update(sdkDbContext).ConfigureAwait(false);
            var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id).ConfigureAwait(false);
            subCl.QuickSyncEnabled = 1;
            await subCl.Update(sdkDbContext).ConfigureAwait(false);

        }
    }
}