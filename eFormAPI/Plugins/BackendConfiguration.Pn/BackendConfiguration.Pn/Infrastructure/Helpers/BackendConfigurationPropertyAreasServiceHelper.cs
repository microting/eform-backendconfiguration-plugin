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
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext, int userId)
    {
        try
        {
            updateModel.Areas = updateModel.Areas.Where(x => x.Activated).ToList();
            var assignments = await backendConfigurationPnDbContext.AreaProperties
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

            var sdkDbContext = core.DbContextHelper.GetDbContext();

            foreach (var assignmentForCreate in assignmentsForCreate)
            {
                var area = await backendConfigurationPnDbContext.Areas
                    .Include(x => x.AreaTranslations)
                    .FirstAsync(x => x.Id == assignmentForCreate.AreaId).ConfigureAwait(false);

                var newAssignment = new AreaProperty
                {
                    CreatedByUserId = userId,
                    UpdatedByUserId = userId,
                    AreaId = assignmentForCreate.AreaId,
                    PropertyId = updateModel.PropertyId,
                    Checked = assignmentForCreate.Activated
                };
                await newAssignment.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

                var property = await backendConfigurationPnDbContext.Properties
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
                        var folderId = await core.FolderCreate([
                            new()
                            {
                                LanguageId = danishLanguage.Id,
                                // Name = "05. Halebid og klargøring af stalde",
                                Name = "25. KemiKontrol",
                                Description = ""
                            },

                            new()
                            {
                                LanguageId = englishLanguage.Id,
                                // Name = "05. Tailbite and preparation of stables",
                                Name = "25. Chemistry Control",
                                Description = ""
                            },

                            new()
                            {
                                LanguageId = germanLanguage.Id,
                                // Name = "05. Stallungen",
                                Name = "25. Chemiekontrolle",
                                Description = ""
                            }
                        ], property.FolderId).ConfigureAwait(false);
                        var assignmentWithOneFolder = new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id
                        };
                        await assignmentWithOneFolder.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

                        var folderIds = new List<int>
                        {
                            await core.FolderCreate([
                                new()
                                {
                                    LanguageId = danishLanguage.Id,
                                    Name = "25.01 Opret kemiprodukt",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = englishLanguage.Id,
                                    Name = "25.01 Create chemical product",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = germanLanguage.Id,
                                    Name = "25.01 Chemisches Produkt herstellen",
                                    Description = property.Name
                                }
                            ], folderId).ConfigureAwait(false),
                            await core.FolderCreate([
                                new()
                                {
                                    LanguageId = danishLanguage.Id,
                                    Name = "25.02 Udløber i dag eller er udløbet",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = englishLanguage.Id,
                                    Name = "25.02 Expires today or has expired",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = germanLanguage.Id,
                                    Name = "25.02 Läuft heute ab oder ist abgelaufen",
                                    Description = property.Name
                                }
                            ], folderId).ConfigureAwait(false),
                            await core.FolderCreate([
                                new()
                                {
                                    LanguageId = danishLanguage.Id,
                                    Name = "25.03 Udløber om senest 1 mdr.",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = englishLanguage.Id,
                                    Name = "25.03 Expires in 1 month at the latest",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = germanLanguage.Id,
                                    Name = "25.03 Läuft spätestens in 1 Monat ab",
                                    Description = property.Name
                                }
                            ], folderId).ConfigureAwait(false),
                            await core.FolderCreate([
                                new()
                                {
                                    LanguageId = danishLanguage.Id,
                                    Name = "25.04 Udløber om senest 3 mdr.",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = englishLanguage.Id,
                                    Name = "25.04 Expires in 3 months at the latest",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = germanLanguage.Id,
                                    Name = "25.04 Läuft spätestens in 3 Monaten ab",
                                    Description = property.Name
                                }
                            ], folderId).ConfigureAwait(false),
                            await core.FolderCreate([
                                new()
                                {
                                    LanguageId = danishLanguage.Id,
                                    Name = "25.05 Udløber om senest 6 mdr.",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = englishLanguage.Id,
                                    Name = "25.05 Expires in 6 months at the latest",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = germanLanguage.Id,
                                    Name = "25.05 Läuft spätestens in 6 Monaten ab",
                                    Description = property.Name
                                }
                            ], folderId).ConfigureAwait(false),
                            await core.FolderCreate([
                                new()
                                {
                                    LanguageId = danishLanguage.Id,
                                    Name = "25.06 Udløber om senest 12 mdr.",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = englishLanguage.Id,
                                    Name = "25.06 Expires in 12 months at the latest",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = germanLanguage.Id,
                                    Name = "25.06 Läuft spätestens in 12 Monaten ab",
                                    Description = property.Name
                                }
                            ], folderId).ConfigureAwait(false),
                            await core.FolderCreate([
                                new()
                                {
                                    LanguageId = danishLanguage.Id,
                                    Name = "25.07 Udløber om mere end 12 mdr.",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = englishLanguage.Id,
                                    Name = "25.07 Expires in more than 12 months.",
                                    Description = property.Name
                                },

                                new()
                                {
                                    LanguageId = germanLanguage.Id,
                                    Name = "25.07 Läuft in mehr als 12 Monaten ab.",
                                    Description = property.Name
                                }
                            ], folderId).ConfigureAwait(false)
                        };

                        foreach (var assignmentWithFolder in folderIds.Select(folderIdLocal => new ProperyAreaFolder
                                 {
                                     FolderId = folderIdLocal,
                                     ProperyAreaAsignmentId = newAssignment.Id
                                 }))
                        {
                            await assignmentWithFolder.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        var groupCreate = await core.EntityGroupCreate(Constants.FieldTypes.EntitySelect,
                            $"Chemicals - Areas - {property.Name}", "", true, false).ConfigureAwait(false);
                        property.EntitySelectListChemicalAreas = Convert.ToInt32(groupCreate.MicrotingUid);
                        await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
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
                        AreaInitialField areaInitialField = await backendConfigurationPnDbContext.AreaInitialFields
                            .Where(x => x.AreaId == area.Id).FirstOrDefaultAsync().ConfigureAwait(false);
                        areaInitialField.EformName = text;
                        await areaInitialField.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                        await areaRule.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                        AreaRuleInitialField areaRuleInitialField = new AreaRuleInitialField
                        {
                            AreaRuleId = areaRule.Id,
                            Notifications = false,
                            ComplianceEnabled = false,
                            RepeatEvery = 0,
                            RepeatType = 0,
                            EformName = text
                        };
                        await areaRuleInitialField.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                        break;
                    }
                    case AreaTypesEnum.Type3:
                    {
                        var folderId = await core.FolderCreate([
                            new()
                            {
                                LanguageId = danishLanguage.Id,
                                // Name = "05. Halebid og klargøring af stalde",
                                Name = "Halebid",
                                Description = ""
                            },

                            new()
                            {
                                LanguageId = englishLanguage.Id,
                                // Name = "05. Tailbite and preparation of stables",
                                Name = "Tail biting",
                                Description = ""
                            },

                            new()
                            {
                                LanguageId = germanLanguage.Id,
                                // Name = "05. Stallungen",
                                Name = "Schwanzbeißen",
                                Description = ""
                            }
                        ], property.FolderId).ConfigureAwait(false);
                        var assignmentWithOneFolder = new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id
                        };

                        await assignmentWithOneFolder.Create(backendConfigurationPnDbContext).ConfigureAwait(false);

                        var tag = await itemsPlanningPnDbContext.PlanningTags
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .Where(x => x.Name == "Halebid")
                            .FirstOrDefaultAsync().ConfigureAwait(false);

                        if (tag == null)
                        {
                            tag = new PlanningTag
                            {
                                Name = "Halebid",
                                CreatedByUserId = userId,
                                UpdatedByUserId = userId
                            };
                            await tag.Create(itemsPlanningPnDbContext).ConfigureAwait(false);
                        }
                        area.ItemPlanningTagId = tag.Id;
                        await area.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
                        // await assignmentWithTwoFolder.Create(_backendConfigurationPnDbContext);

                        var groupCreate = await core
                            .EntityGroupCreate(Constants.FieldTypes.EntitySelect, property.Name, "", true, false)
                            .ConfigureAwait(false);
                        // TODO load tailbite eForm and seed it.
                        await SeedTailBite(property.Name, core, sdkDbContext, groupCreate.MicrotingUid)
                            .ConfigureAwait(false);
                        newAssignment.GroupMicrotingUuid = Convert.ToInt32(groupCreate.MicrotingUid);
                        await newAssignment.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
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

                            await areaRule.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
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
                                Description = ""
                            }).ToList(),
                            property.FolderId).ConfigureAwait(false);
                        var assignmentWithFolder = new ProperyAreaFolder
                        {
                            FolderId = folderId,
                            ProperyAreaAsignmentId = newAssignment.Id
                        };
                        await assignmentWithFolder.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
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

                            await areaRule.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        break;
                    }
                }
            }

            foreach (var areaPropertyForDelete in assignmentsForDelete)
            {
                // get areaRules and select all linked entity for delete
                var areaRules = await backendConfigurationPnDbContext.AreaRules
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
                            await backendConfigurationPnDbContext.Properties
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
                        await areaRuleAreaRuleTranslation.Delete(backendConfigurationPnDbContext)
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
                            await planningSite.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        if (areaRulePlanning.ItemPlanningId != 0)
                        {
                            var planning = await itemsPlanningPnDbContext.Plannings
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
                                    await translation.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
                                }

                                planning.UpdatedByUserId = userId;
                                await planning.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);

                                var planningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
                                    .Where(x => x.PlanningId == planning.Id)
                                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                                    .ToListAsync().ConfigureAwait(false);
                                foreach (PlanningCaseSite planningCaseSite in planningCaseSites)
                                {
                                    planningCaseSite.UpdatedByUserId = userId;
                                    await planningCaseSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
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
                        await areaRulePlanning.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                    }

                    // delete area rule
                    areaRule.UpdatedByUserId = userId;
                    await areaRule.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                // delete entity select group. only for type 3(tail bite and stables)
                if (areaPropertyForDelete.GroupMicrotingUuid != 0)
                {
                    await core.EntityGroupDelete(areaPropertyForDelete.GroupMicrotingUuid.ToString())
                        .ConfigureAwait(false);
                }

                areaPropertyForDelete.UpdatedByUserId = userId;
                await areaPropertyForDelete.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);

                var foldersIdForDelete = backendConfigurationPnDbContext.ProperyAreaFolders
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
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
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
                    "BackendConfiguration.Pn.Resources.eForms.05. Halebid og risikovurdering.xml");

            string contents;
            using (var sr = new StreamReader(resourceStream!))
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
                assembly.GetManifestResourceStream("BackendConfiguration.Pn.Resources.eForms.01. Aflæsninger.xml");

            string contents;
            using (var sr = new StreamReader(resourceStream!))
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
                assembly.GetManifestResourceStream("BackendConfiguration.Pn.Resources.eForms.02. Fækale uheld.xml");

            string contents;
            using (var sr = new StreamReader(resourceStream!))
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
            cl.QuickSyncEnabled = 1;
            await cl.Update(sdkDbContext).ConfigureAwait(false);
            var subCl = await sdkDbContext.CheckLists.SingleAsync(x => x.ParentId == cl.Id).ConfigureAwait(false);
            subCl.QuickSyncEnabled = 1;
            await subCl.Update(sdkDbContext).ConfigureAwait(false);

        }
    }
}