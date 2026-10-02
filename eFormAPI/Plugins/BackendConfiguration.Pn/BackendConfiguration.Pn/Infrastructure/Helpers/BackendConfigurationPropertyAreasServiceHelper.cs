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
    /// <summary>
    /// Localization key returned when a caller tries to assign, give rules to or
    /// plan the Type9 area "25. KemiKontrol", whose legacy flow was removed (#1362).
    /// </summary>
    public const string LegacyChemicalAreaRemoved = "LegacyChemicalAreaRemoved";

    /// <summary>The area type of "25. KemiKontrol", the only area that ever used Type9.</summary>
    public const AreaTypesEnum LegacyChemicalAreaType = AreaTypesEnum.Type9;

    public static async Task<OperationResult> Update(PropertyAreasUpdateModel updateModel, Core core,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext, int userId)
    {
        try
        {
            updateModel.Areas = updateModel.Areas.Where(x => x.Activated).ToList();

            // Refused before any write. Existing Type9 assignments are not listed by
            // Read, so a save never mentions them: they are excluded from the
            // delete set below and left for LegacyChemicalCleanupService.
            var assignmentsForCreate = updateModel.Areas.Where(x => x.Id == null).ToList();
            var requestedAreaIds = assignmentsForCreate.Select(x => x.AreaId).ToList();
            if (await backendConfigurationPnDbContext.Areas
                    .AnyAsync(x => requestedAreaIds.Contains(x.Id) && x.Type == LegacyChemicalAreaType)
                    .ConfigureAwait(false))
            {
                return new OperationResult(false, LegacyChemicalAreaRemoved);
            }

            var assignments = await backendConfigurationPnDbContext.AreaProperties
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.PropertyId == updateModel.PropertyId)
                .Where(x => x.Area.Type != LegacyChemicalAreaType)
                .ToListAsync().ConfigureAwait(false);

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
                    case AreaTypesEnum.Type3:
                    {
                        var folderId = await core.FolderCreate([
                            new()
                            {
                                LanguageId = danishLanguage.Id,
                                // Name = "05. Halebid og klargøring af stalde",
                                Name = "05. Halebid",
                                Description = ""
                            },

                            new()
                            {
                                LanguageId = englishLanguage.Id,
                                // Name = "05. Tailbite and preparation of stables",
                                Name = "05. Tail biting",
                                Description = ""
                            },

                            new()
                            {
                                LanguageId = germanLanguage.Id,
                                // Name = "05. Stallungen",
                                Name = "05. Schwanzbeißen",
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

                            await AreaRuleLanguageHelper
                                .RemapSeedLanguageIdsAsync(areaRule, sdkDbContext).ConfigureAwait(false);
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

                            await AreaRuleLanguageHelper
                                .RemapSeedLanguageIdsAsync(areaRule, sdkDbContext).ConfigureAwait(false);
                            await areaRule.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
                        }

                        break;
                    }
                }
            }

            foreach (var areaPropertyForDelete in assignmentsForDelete)
            {
                await DeleteAreaPropertyAsync(areaPropertyForDelete, core, backendConfigurationPnDbContext,
                    itemsPlanningPnDbContext, userId).ConfigureAwait(false);
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

    /// <summary>
    /// Deactivates one area on a property: soft-deletes its area rules,
    /// plannings, planning sites, items-planning plannings (deleting their SDK
    /// cases), its entity group, the assignment itself and its SDK folders.
    /// Shared by the property-areas update and LegacyChemicalCleanupService.
    /// </summary>
    /// <param name="deleteCase">
    /// Deletes one SDK case by MicrotingUid. LegacyChemicalCleanupService passes
    /// its SDK seam, which throws on failure so the assignment is reported as
    /// failed. When omitted, Core.CaseDelete is called and a refused delete is
    /// logged, as the property-areas update has always carried on past it.
    /// </param>
    public static async Task DeleteAreaPropertyAsync(AreaProperty areaProperty, Core core,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext, int userId,
        Func<int, Task>? deleteCase = null)
    {
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        deleteCase ??= DefaultDeleteCase(core);

        await DeleteAreaRulesAsync(areaProperty.PropertyId, areaProperty.AreaId, core,
            backendConfigurationPnDbContext, itemsPlanningPnDbContext, userId, deleteCase).ConfigureAwait(false);

        // delete entity select group. only for type 3(tail bite and stables)
        if (areaProperty.GroupMicrotingUuid != 0)
        {
            await core.EntityGroupDelete(areaProperty.GroupMicrotingUuid.ToString())
                .ConfigureAwait(false);
        }

        areaProperty.UpdatedByUserId = userId;
        await areaProperty.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);

        var foldersIdForDelete = backendConfigurationPnDbContext.ProperyAreaFolders
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.ProperyAreaAsignmentId == areaProperty.Id)
            .Select(x => x.FolderId)
            .ToList();

        foreach (var folderIdForDelete in foldersIdForDelete)
        {
            // Tolerate folders already removed by hand, so the one-off
            // LegacyChemicalCleanupService cannot wedge on them.
            var folder = await sdkDbContext.Folders
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Where(x => x.Id == folderIdForDelete)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (folder != null)
            {
                await folder.Delete(sdkDbContext).ConfigureAwait(false);
            }
        }
    }

    private static Func<int, Task> DefaultDeleteCase(Core core) => async microtingUid =>
    {
        if (!await core.CaseDelete(microtingUid).ConfigureAwait(false))
        {
            Console.WriteLine($"DeleteAreaPropertyAsync: Core.CaseDelete({microtingUid}) returned false");
        }
    };

    /// <summary>
    /// Soft-deletes the live area rules of one area on one property, with their
    /// translations and plannings (see <see cref="DeleteAreaRulePlanningAsync"/>).
    /// </summary>
    internal static async Task DeleteAreaRulesAsync(int propertyId, int areaId, Core core,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext, int userId, Func<int, Task> deleteCase)
    {
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        // get areaRules and select all linked entity for delete
        var areaRules = await backendConfigurationPnDbContext.AreaRules
            .Where(x => x.PropertyId == propertyId)
            .Where(x => x.AreaId == areaId)
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
                    await deleteCase(checkListSite.MicrotingUid).ConfigureAwait(false);
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
                await DeleteAreaRulePlanningAsync(areaRulePlanning, sdkDbContext, backendConfigurationPnDbContext,
                    itemsPlanningPnDbContext, userId, deleteCase).ConfigureAwait(false);
            }

            // delete area rule
            areaRule.UpdatedByUserId = userId;
            await areaRule.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Soft-deletes one area rule planning (its PlanningSites must be loaded),
    /// its planning sites and its items-planning planning, deleting the SDK case
    /// behind every live PlanningCaseSite before soft-deleting that link, and the
    /// planning only after its case sites, so a failed delete leaves both for a
    /// later run.
    /// </summary>
    internal static async Task DeleteAreaRulePlanningAsync(AreaRulePlanning areaRulePlanning,
        MicrotingDbContext sdkDbContext, BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        ItemsPlanningPnDbContext itemsPlanningPnDbContext, int userId, Func<int, Task> deleteCase)
    {
        foreach (var planningSite in areaRulePlanning.PlanningSites
                     .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed))
        {
            planningSite.UpdatedByUserId = userId;
            await planningSite.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
        }

        if (areaRulePlanning.ItemPlanningId != 0)
        {
            // Case sites are walked even when the planning is already removed: an
            // interrupted earlier pass removed the planning before its case sites.
            var planningCaseSites = await itemsPlanningPnDbContext.PlanningCaseSites
                .Where(x => x.PlanningId == areaRulePlanning.ItemPlanningId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .ToListAsync().ConfigureAwait(false);
            foreach (var planningCaseSite in planningCaseSites)
            {
                var microtingUid = await ResolvePlannedCaseUidAsync(planningCaseSite, sdkDbContext)
                    .ConfigureAwait(false);
                if (microtingUid != null)
                {
                    await deleteCase(microtingUid.Value).ConfigureAwait(false);
                }

                planningCaseSite.UpdatedByUserId = userId;
                await planningCaseSite.Delete(itemsPlanningPnDbContext).ConfigureAwait(false);
            }

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
            }
        }

        areaRulePlanning.UpdatedByUserId = userId;
        await areaRulePlanning.Delete(backendConfigurationPnDbContext).ConfigureAwait(false);
    }

    /// <summary>
    /// The MicrotingUid to delete for one PlanningCaseSite, or null when there is
    /// nothing live to delete. Two shapes exist:
    /// <list type="bullet">
    /// <item>Items-planning: MicrotingSdkCaseId is a Cases.Id.</item>
    /// <item>The removed Type9 flow (Repeated = 0): there is no Cases row;
    /// MicrotingSdkCaseId holds the CheckListSite's MicrotingUid and
    /// MicrotingCheckListSitId its Id.</item>
    /// </list>
    /// The legacy shape is recognised by the CheckListSite carrying the same uid,
    /// and checked first, so its uid is never read as a Cases.Id (which could
    /// name an unrelated case). A link whose CheckListSite row is gone is
    /// ambiguous and skipped. Removed or retracted rows were deleted already.
    /// </summary>
    internal static async Task<int?> ResolvePlannedCaseUidAsync(PlanningCaseSite planningCaseSite,
        MicrotingDbContext sdkDbContext)
    {
        var checkListSite = planningCaseSite.MicrotingCheckListSitId == 0
            ? null
            : await sdkDbContext.CheckListSites.AsNoTracking()
                .Where(x => x.Id == planningCaseSite.MicrotingCheckListSitId)
                .Select(x => new { x.MicrotingUid, x.WorkflowState })
                .FirstOrDefaultAsync().ConfigureAwait(false);

        if (planningCaseSite.MicrotingCheckListSitId != 0 && checkListSite == null)
        {
            return null;
        }

        if (checkListSite != null && checkListSite.MicrotingUid == planningCaseSite.MicrotingSdkCaseId)
        {
            return IsLive(checkListSite.WorkflowState) ? checkListSite.MicrotingUid : null;
        }

        var sdkCase = await sdkDbContext.Cases.AsNoTracking()
            .Where(x => x.Id == planningCaseSite.MicrotingSdkCaseId)
            .Select(x => new { x.MicrotingUid, x.WorkflowState })
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (sdkCase != null)
        {
            return sdkCase.MicrotingUid != null && IsLive(sdkCase.WorkflowState) ? sdkCase.MicrotingUid : null;
        }

        return checkListSite != null && IsLive(checkListSite.WorkflowState) ? checkListSite.MicrotingUid : null;
    }

    private static bool IsLive(string? workflowState) =>
        workflowState is not (Constants.WorkflowStates.Removed or Constants.WorkflowStates.Retracted);

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