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


using BackendConfiguration.Pn.Controllers;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.TimePlanningBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.BackendConfigurationCompliancesService;

using BackendConfigurationLocalizationService;
using Infrastructure.Models.Compliances.Index;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Delegates.CaseUpdate;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class BackendConfigurationCompliancesService : IBackendConfigurationCompliancesService
{

    private readonly IEFormCoreService _coreHelper;
    private readonly IBackendConfigurationLocalizationService _localizationService;
    private readonly IUserService _userService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDbContext;
    private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
    private readonly TimePlanningPnDbContext _timePlanningPnDbContext;

    public BackendConfigurationCompliancesService(
        ItemsPlanningPnDbContext itemsPlanningPnDbContext,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IUserService userService,
        IBackendConfigurationLocalizationService localizationService,
        IEFormCoreService coreHelper,
        TimePlanningPnDbContext timePlanningPnDbContext
    )
    {
        _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
        _backendConfigurationPnDbContext = backendConfigurationPnDbContext;
        _userService = userService;
        _localizationService = localizationService;
        _coreHelper = coreHelper;
        _timePlanningPnDbContext = timePlanningPnDbContext;
    }

    public async Task<OperationDataResult<Paged<CompliancesModel>>> Index(CompliancesRequestModel request)
    {
        var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
        var result = new Paged<CompliancesModel>
        {
            Entities = []
        };

        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        var complianceList = _backendConfigurationPnDbContext.Compliances
            .Where(x => x.PropertyId == request.PropertyId)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed);

        if (request.Days > 0)
        {
            complianceList = complianceList.Where(x => x.Deadline <= DateTime.Now.AddDays(request.Days));
        }

        var theList = await complianceList.AsNoTracking()
            .OrderBy(x => x.Deadline)
            .ToListAsync().ConfigureAwait(false);

        foreach (var compliance in theList)
        {
            var planningNameTranslation = await _itemsPlanningPnDbContext.PlanningNameTranslation
                .SingleOrDefaultAsync(x => x.PlanningId == compliance.PlanningId && x.LanguageId == language.Id).ConfigureAwait(false);

            if (planningNameTranslation == null)
            {
                continue;
            }
            var areaTranslation = await _backendConfigurationPnDbContext.AreaTranslations
                .SingleOrDefaultAsync(x => x.AreaId == compliance.AreaId && x.LanguageId == language.Id).ConfigureAwait(false);

            if (areaTranslation == null)
            {
                continue;
            }

            var planningSites = await _itemsPlanningPnDbContext.PlanningSites
                .Where(x => x.PlanningId == compliance.PlanningId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .Select(x => x.SiteId)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);

            var sitesList = await sdkDbContext.Sites.Where(x => planningSites.Contains(x.Id)).ToListAsync().ConfigureAwait(false);

            var responsible = sitesList.Select(site => new KeyValuePair<int, string>(site.Id, site.Name)).ToList();

            var complianceModel = new CompliancesModel
            {
                CaseId = compliance.MicrotingSdkCaseId,
                CreatedAt = compliance.CreatedAt,
                Deadline = compliance.Deadline.AddDays(-1),
                ComplianceTypeId = null,
                ControlArea = areaTranslation.Name,
                EformId = compliance.MicrotingSdkeFormId,
                Id = compliance.Id,
                ItemName = planningNameTranslation.Name,
                PlanningId = compliance.PlanningId,
                Responsible = responsible
            };

            result.Entities.Add(complianceModel);
        }

        return new OperationDataResult<Paged<CompliancesModel>>(true, result);
    }

    public async Task<OperationDataResult<int>> ComplianceStatus(int propertyId)
    {
        var compliance = await Index(new CompliancesRequestModel
        {
            PropertyId = propertyId
        }).ConfigureAwait(false);

        return new OperationDataResult<int>(true, compliance.Model.Entities.Count == 0 ? 0 : 1);
    }

    public async Task<OperationDataResult<ReplyElement>> Read(int id)
    {
        try
        {
            var core = await _coreHelper.GetCore().ConfigureAwait(false);
            // var sdkDbContext = core.DbContextHelper.GetDbContext();
            // var caseDto = await sdkDbContext.Cases.SingleOrDefaultAsync(x => x.Id == id);
            // if (caseDto == null)
            // {
            // return new OperationDataResult<ReplyElement>(false, _localizationService.GetString("CaseNotFound"));
            // }
            var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
            var theCase = await core.CaseRead(id, language).ConfigureAwait(false);
            // theCase.Id = id;

            return !theCase.Equals(null)
                ? new OperationDataResult<ReplyElement>(true, theCase)
                : new OperationDataResult<ReplyElement>(false);
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationDataResult<ReplyElement>(false, ex.Message);
        }
    }

    public async Task<OperationResult> Update(ReplyRequest model)
    {
        var checkListValueList = new List<string>();
        var fieldValueList = new List<string>();
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
        var currentUser = await _userService.GetCurrentUserAsync().ConfigureAwait(false);
        try
        {
            model.ElementList.ForEach(element =>
            {
                checkListValueList.AddRange(CaseUpdateHelper.GetCheckList(element));
                fieldValueList.AddRange(CaseUpdateHelper.GetFieldList(element));
            });
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false, $"{_localizationService.GetString("CaseCouldNotBeUpdated")} Exception: {ex.Message}");
        }

        try
        {
            // #1157 pre-flight validation. This method used to soft-delete the
            // Compliance and complete the SDK case BEFORE it had finished validating,
            // with no transaction and no compensation, so every late rejection below
            // left an unrecoverable partial write: the occurrence vanished from the
            // calendar and the compliance list while nothing was ever completed, and a
            // retry could not repair it. Everything the completion needs is therefore
            // resolved and checked FIRST. The first irreversible write is the
            // compliance.Delete() below the "pre-flight complete" line; nothing above it
            // mutates. No write moved relative to another write: the order
            // (compliance.Delete, core.CaseUpdate, foundCase.Update, the items-planning
            // promotion, the property recompute, the retraction) is unchanged. A READ did
            // cross a write, though: the PlanningCaseSite lookup now runs here in the
            // pre-flight, before core.CaseUpdate and before the CaseUpdateDelegate
            // invocation loop, where it used to run after both - and that same tracked
            // entity is mutated and saved further down. Inert today: no
            // CaseUpdateDelegate subscriber is registered anywhere in the workspace (the
            // only registrations are commented out, e.g.
            // ItemsPlanning.Pn/EformItemsPlanningPlugin.cs:113), and core.CaseUpdate
            // writes only SDK tables. A future subscriber must not write PlanningCaseSites
            // through a different DbContext - this method's tracked snapshot would then be
            // saved back over the delegate's changes, a silent lost update.
            //
            // #1157 option 3, defence in depth: the WorkflowState filter on the
            // Compliance lookup. Without it a retry after an earlier partial failure
            // re-found the already-soft-deleted row, re-ran the whole cascade against it
            // and reported success over an occurrence that no longer exists. With it the
            // retry fails fast and distinguishably instead.
            var compliance = await _backendConfigurationPnDbContext.Compliances
                .SingleOrDefaultAsync(x => x.Id == model.ExtraId
                                           && x.WorkflowState != Constants.WorkflowStates.Removed)
                .ConfigureAwait(false);
            if (compliance == null)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.Update: no live Compliance {model.ExtraId} (never existed, or already soft-deleted by an earlier completion) - nothing was mutated");
                return new OperationResult(false, $"{_localizationService.GetString("CaseCouldNotBeUpdated")}");
            }

            var sdkDbContext = core.DbContextHelper.GetDbContext();

            // Existence check only. The tracked instance that actually gets mutated is
            // re-read further down, AFTER core.CaseUpdate/CaseUpdateFieldValues have
            // written to the row, so the completion still operates on post-update state
            // exactly as it did before this hoist.
            var sdkCaseExists = await sdkDbContext.Cases
                .AsNoTracking()
                .AnyAsync(x => x.Id == model.Id)
                .ConfigureAwait(false);
            if (!sdkCaseExists)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.Update: no SDK Case {model.Id} (complianceId: {compliance.Id}) - nothing was mutated");
                return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
            }

            // Resolve the occurrence by its SDK case id — the only per-occurrence
            // key. The previous CreatedAt.Date == Compliance.StartDate.Date
            // heuristic assumed a planning deploys at most one occurrence per day;
            // back-filled past series break that (every back-filled
            // PlanningCaseSite.CreatedAt and Compliance.StartDate collapse to the
            // day the backfill ran), so it kept returning the same sibling row and
            // only the first completed occurrence ever reached Status 100.
            // Matches the mobile path (EventsGrpcService.cs:1703-1707) and the
            // scheduler path (eFormCompletedHandler.cs:62-63).
            // model.Id is the SDK case id the existence check above just confirmed, and
            // is by construction the id the re-read foundCase carries, so this is the
            // same selector it used to run with foundCase.Id.
            var planningCaseSite = await _itemsPlanningPnDbContext.PlanningCaseSites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
                .FirstOrDefaultAsync(x => x.MicrotingSdkCaseId == model.Id).ConfigureAwait(false);
            if (planningCaseSite == null)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.Update: no PlanningCaseSite found for MicrotingSdkCaseId {model.Id} (complianceId: {compliance.Id}, planningId: {compliance.PlanningId}) - nothing was mutated");
                return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
            }
            // #1218: cross-check that the occurrence reached via the SDK case id really
            // belongs to the planning the quoted Compliance is on. The SDK case comes from
            // model.Id and `compliance` from model.ExtraId — both client-supplied, and
            // nothing else pairs them, so without this a request quoting one property's
            // compliance id together with another property's case id promotes the
            // unrelated property's occurrence.
            // Deliberately AFTER the lookup, not folded back into its predicate:
            // MicrotingSdkCaseId must stay the SOLE selector (#1158 — adding
            // PlanningId back into the Where would re-introduce a second selector and
            // report a genuine mismatch as the indistinguishable "no occurrence found"
            // instead of as a mismatch).
            if (planningCaseSite.PlanningId != compliance.PlanningId)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.Update: PlanningCaseSite {planningCaseSite.Id} (planningId: {planningCaseSite.PlanningId}) resolved from MicrotingSdkCaseId {model.Id} does not belong to compliance {compliance.Id} (planningId: {compliance.PlanningId}) - rejecting mismatched case/compliance pair, nothing was mutated");
                return new OperationResult(false, _localizationService.GetString("CaseDoesNotBelongToCompliance"));
            }

            // #1157: the completing worker's SDK Site is resolved HERE, in the pre-flight,
            // not beside its first use inside the mutation block. model.SiteId is
            // client-supplied and nothing above validates it, so an unknown - or
            // soft-deleted - site was discovered only at the `site.Name` dereference
            // below, i.e. AFTER compliance.Delete, core.CaseUpdate and foundCase.Update
            // had all committed; the NullReferenceException was then swallowed by the
            // outer catch into a generic CaseCouldNotBeUpdated. That was the last routine
            // failure mode still leaving the partial write #1157 is about.
            var site = await sdkDbContext.Sites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync(x => x.Id == model.SiteId).ConfigureAwait(false);
            if (site == null)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.Update: no live SDK Site {model.SiteId} (complianceId: {compliance.Id}, caseId: {model.Id}) - nothing was mutated");
                return new OperationResult(false, _localizationService.GetString("SiteNotFound"));
            }

            // ---- pre-flight complete; everything below this line mutates ----

            await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

            await core.CaseUpdate(model.Id, fieldValueList, checkListValueList).ConfigureAwait(false);
            await core.CaseUpdateFieldValues(model.Id, language).ConfigureAwait(false);

            var foundCase = await sdkDbContext.Cases
                .Where(x => x.Id == model.Id)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            // Only reachable if the case was hard-deleted between the pre-flight check
            // and this re-read. Kept as a guard so the promotion below cannot dereference
            // null; it is the one exit that can still leave the partial write #1157 is
            // about, and it is a race, not a routine outcome.
            if (foundCase != null)
            {
                // Preserve the caller-supplied time-of-day so calendar
                // event.start (Deadline day + CalendarConfiguration.StartHour)
                // survives the round-trip. Existing consumers (task-tracker,
                // compliance-list) keep submitting their previous values —
                // task-tracker uses `task.deadlineTask.toISOString()` and
                // Compliance.Deadline is stored as midnight UTC, so their
                // observable behaviour is unchanged. SpecifyKind re-tags
                // without shifting (model.DoneAt arrives as Unspecified-kind
                // from the JSON binder).
                var newDoneAt = DateTime.SpecifyKind(model.DoneAt, DateTimeKind.Utc);
                foundCase.DoneAtUserModifiable = newDoneAt;
                foundCase.DoneAt = newDoneAt;

                // if (site != null)
                // {
                //     foundCase.SiteId = site.Id;
                // }
                // else
                // {
                //     await core.SiteCreate($"{currentUser.FirstName} {currentUser.LastName}", currentUser.FirstName, currentUser.LastName,
                //         null, "da");
                //     site = await sdkDbContext.Sites
                //         .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                //         .FirstOrDefaultAsync(x => x.Name == $"{currentUser.FirstName} {currentUser.LastName}");
                //     foundCase.SiteId = site.Id;
                // }

                foundCase.SiteId = model.SiteId;
                foundCase.Status = 100;
                foundCase.WorkflowState = Constants.WorkflowStates.Created;
                await foundCase.Update(sdkDbContext).ConfigureAwait(false);

                if (CaseUpdateDelegates.CaseUpdateDelegate != null)
                {
                    var invocationList = CaseUpdateDelegates.CaseUpdateDelegate
                        .GetInvocationList();
                    foreach (var func in invocationList)
                    {
                        func.DynamicInvoke(model.Id);
                    }
                }
                // if (compliance.PlanningCaseSiteId != 0)
                // {
                //     var planningCaseSite = await _itemsPlanningPnDbContext.PlanningCaseSites
                //         .SingleOrDefaultAsync(x => x.Id == compliance.PlanningCaseSiteId).ConfigureAwait(false);
                //     if (planningCaseSite != null)
                //     {
                //         planningCaseSite.Status = 100;
                //         planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language).ConfigureAwait(false);
                //
                //         planningCaseSite.MicrotingSdkCaseDoneAt = newDoneAt;
                //         planningCaseSite.MicrotingSdkCaseId = foundCase.Id;
                //         planningCaseSite.DoneByUserId = (int)foundCase.SiteId;
                //         planningCaseSite.DoneByUserName = $"{currentUser.FirstName} {currentUser.LastName}";
                //         await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                //
                //         var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                //             .SingleAsync(x => x.Id == planningCaseSite.PlanningCaseId).ConfigureAwait(false);
                //         if (planningCase.Status != 100)
                //         {
                //             planningCase.Status = 100;
                //             planningCase.MicrotingSdkCaseDoneAt = newDoneAt;
                //             planningCase.MicrotingSdkCaseId = foundCase.Id;
                //             planningCase.DoneByUserId = (int)foundCase.SiteId;
                //             planningCase.DoneByUserName = planningCaseSite.DoneByUserName;
                //             planningCase.WorkflowState = Constants.WorkflowStates.Processed;
                //
                //             planningCase = await SetFieldValue(planningCase, foundCase.Id, language).ConfigureAwait(false);
                //             await planningCase.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                //         }
                //
                //         planningCaseSite.PlanningCaseId = planningCase.Id;
                //         await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                //     }
                // }
                // else
                // {
                planningCaseSite.Status = 100;
                planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language).ConfigureAwait(false);

                planningCaseSite.MicrotingSdkCaseId = foundCase.Id;
                planningCaseSite.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                planningCaseSite.DoneByUserId = (int)foundCase.SiteId;
                planningCaseSite.DoneByUserName = site.Name;
                await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);

                var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                    .SingleAsync(x => x.Id == planningCaseSite.PlanningCaseId).ConfigureAwait(false);
                if (planningCase.Status != 100)
                {
                    planningCase.Status = 100;
                    planningCase.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                    planningCase.MicrotingSdkCaseId = foundCase.Id;
                    planningCase.DoneByUserId = (int)foundCase.SiteId;
                    planningCase.DoneByUserName = planningCaseSite.DoneByUserName;
                    planningCase.WorkflowState = Constants.WorkflowStates.Processed;

                    planningCase = await SetFieldValue(planningCase, foundCase.Id, language).ConfigureAwait(false);
                    await planningCase.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }
                planningCaseSite.PlanningCaseId = planningCase.Id;
                await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                // }
            }
            else
            {
                return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
            }

            var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId).ConfigureAwait(false);

            if (_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                    x.Deadline < DateTime.UtcNow && x.PropertyId == property.Id &&
                    x.WorkflowState != Constants.WorkflowStates.Removed))
            {
                property.ComplianceStatus = 2;
                property.ComplianceStatusThirty = 2;
                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }
            else
            {
                if (!_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                        x.Deadline < DateTime.UtcNow.AddDays(30) && x.PropertyId == property.Id &&
                        x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    property.ComplianceStatusThirty = 0;
                    await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                if (!_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                        x.Deadline < DateTime.UtcNow && x.PropertyId == property.Id &&
                        x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    property.ComplianceStatus = 0;
                    await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }
            
            try
            {
                if (foundCase.MicrotingUid != null)
                {
                    await core.CaseDelete((int)foundCase.MicrotingUid).ConfigureAwait(false);
                }
                else
                {
                    var checkListSite = await sdkDbContext.CheckListSites
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == model.Id)
                        .ConfigureAwait(false);
                    if (checkListSite != null)
                    {
                        await core.CaseDelete(checkListSite.MicrotingUid).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogException(ex.Message);
                Log.LogException(ex.StackTrace);
            }

            return new OperationResult(true, _localizationService.GetString("CaseHasBeenUpdated"));
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false, _localizationService.GetString("CaseCouldNotBeUpdated") + $" Exception: {ex.Message}");
        }
    }

    // Calendar-specific variant of Update: identical behaviour except the
    // device retraction (core.CaseDelete) is resolved synchronously but
    // executed fire-and-forget, since it is an external platform call that
    // can block for minutes in dev. Retraction was already best-effort in
    // Update (failures are swallowed), so this changes latency, not
    // guarantees.
    public async Task<OperationResult> UpdateFromCalendar(ReplyRequest model)
    {
        var checkListValueList = new List<string>();
        var fieldValueList = new List<string>();
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var language = await _userService.GetCurrentUserLanguage().ConfigureAwait(false);
        var currentUser = await _userService.GetCurrentUserAsync().ConfigureAwait(false);
        try
        {
            model.ElementList.ForEach(element =>
            {
                checkListValueList.AddRange(CaseUpdateHelper.GetCheckList(element));
                fieldValueList.AddRange(CaseUpdateHelper.GetFieldList(element));
            });
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false, $"{_localizationService.GetString("CaseCouldNotBeUpdated")} Exception: {ex.Message}");
        }

        try
        {
            // #1157 pre-flight validation. This method used to soft-delete the
            // Compliance and complete the SDK case BEFORE it had finished validating,
            // with no transaction and no compensation, so every late rejection below
            // left an unrecoverable partial write: the occurrence vanished from the
            // calendar and the compliance list while nothing was ever completed, and a
            // retry could not repair it. Everything the completion needs is therefore
            // resolved and checked FIRST. The first irreversible write is the
            // compliance.Delete() below the "pre-flight complete" line; nothing above it
            // mutates. No write moved relative to another write: the order
            // (compliance.Delete, core.CaseUpdate, foundCase.Update, the items-planning
            // promotion, the property recompute, the retraction) is unchanged. A READ did
            // cross a write, though: the PlanningCaseSite lookup now runs here in the
            // pre-flight, before core.CaseUpdate and before the CaseUpdateDelegate
            // invocation loop, where it used to run after both - and that same tracked
            // entity is mutated and saved further down. Inert today: no
            // CaseUpdateDelegate subscriber is registered anywhere in the workspace (the
            // only registrations are commented out, e.g.
            // ItemsPlanning.Pn/EformItemsPlanningPlugin.cs:113), and core.CaseUpdate
            // writes only SDK tables. A future subscriber must not write PlanningCaseSites
            // through a different DbContext - this method's tracked snapshot would then be
            // saved back over the delegate's changes, a silent lost update.
            //
            // #1157 option 3, defence in depth: the WorkflowState filter on the
            // Compliance lookup. Without it a retry after an earlier partial failure
            // re-found the already-soft-deleted row, re-ran the whole cascade against it
            // and reported success over an occurrence that no longer exists. With it the
            // retry fails fast and distinguishably instead.
            var compliance = await _backendConfigurationPnDbContext.Compliances
                .SingleOrDefaultAsync(x => x.Id == model.ExtraId
                                           && x.WorkflowState != Constants.WorkflowStates.Removed)
                .ConfigureAwait(false);
            if (compliance == null)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.UpdateFromCalendar: no live Compliance {model.ExtraId} (never existed, or already soft-deleted by an earlier completion) - nothing was mutated");
                return new OperationResult(false, $"{_localizationService.GetString("CaseCouldNotBeUpdated")}");
            }

            var sdkDbContext = core.DbContextHelper.GetDbContext();

            // Existence check only. The tracked instance that actually gets mutated is
            // re-read further down, AFTER core.CaseUpdate/CaseUpdateFieldValues have
            // written to the row, so the completion still operates on post-update state
            // exactly as it did before this hoist.
            var sdkCaseExists = await sdkDbContext.Cases
                .AsNoTracking()
                .AnyAsync(x => x.Id == model.Id)
                .ConfigureAwait(false);
            if (!sdkCaseExists)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.UpdateFromCalendar: no SDK Case {model.Id} (complianceId: {compliance.Id}) - nothing was mutated");
                return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
            }

            // Resolve the occurrence by its SDK case id — the only per-occurrence
            // key. The previous CreatedAt.Date == Compliance.StartDate.Date
            // heuristic assumed a planning deploys at most one occurrence per day;
            // back-filled past series break that (every back-filled
            // PlanningCaseSite.CreatedAt and Compliance.StartDate collapse to the
            // day the backfill ran), so it kept returning the same sibling row and
            // only the first completed occurrence ever reached Status 100.
            // Matches the mobile path (EventsGrpcService.cs:1703-1707) and the
            // scheduler path (eFormCompletedHandler.cs:62-63).
            // model.Id is the SDK case id the existence check above just confirmed, and
            // is by construction the id the re-read foundCase carries, so this is the
            // same selector it used to run with foundCase.Id.
            var planningCaseSite = await _itemsPlanningPnDbContext.PlanningCaseSites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed || x.WorkflowState == null)
                .FirstOrDefaultAsync(x => x.MicrotingSdkCaseId == model.Id).ConfigureAwait(false);
            if (planningCaseSite == null)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.UpdateFromCalendar: no PlanningCaseSite found for MicrotingSdkCaseId {model.Id} (complianceId: {compliance.Id}, planningId: {compliance.PlanningId}) - nothing was mutated");
                return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
            }
            // #1218: cross-check that the occurrence reached via the SDK case id really
            // belongs to the planning the quoted Compliance is on. The SDK case comes from
            // model.Id and `compliance` from model.ExtraId — both client-supplied, and
            // nothing else pairs them, so without this a request quoting one property's
            // compliance id together with another property's case id promotes the
            // unrelated property's occurrence.
            // Deliberately AFTER the lookup, not folded back into its predicate:
            // MicrotingSdkCaseId must stay the SOLE selector (#1158 — adding
            // PlanningId back into the Where would re-introduce a second selector and
            // report a genuine mismatch as the indistinguishable "no occurrence found"
            // instead of as a mismatch).
            if (planningCaseSite.PlanningId != compliance.PlanningId)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.UpdateFromCalendar: PlanningCaseSite {planningCaseSite.Id} (planningId: {planningCaseSite.PlanningId}) resolved from MicrotingSdkCaseId {model.Id} does not belong to compliance {compliance.Id} (planningId: {compliance.PlanningId}) - rejecting mismatched case/compliance pair, nothing was mutated");
                return new OperationResult(false, _localizationService.GetString("CaseDoesNotBelongToCompliance"));
            }

            // #1157: the completing worker's SDK Site is resolved HERE, in the pre-flight,
            // not beside its first use inside the mutation block. model.SiteId is
            // client-supplied and nothing above validates it, so an unknown - or
            // soft-deleted - site was discovered only at the `site.Name` dereference
            // below, i.e. AFTER compliance.Delete, core.CaseUpdate and foundCase.Update
            // had all committed; the NullReferenceException was then swallowed by the
            // outer catch into a generic CaseCouldNotBeUpdated. That was the last routine
            // failure mode still leaving the partial write #1157 is about.
            var site = await sdkDbContext.Sites
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                .FirstOrDefaultAsync(x => x.Id == model.SiteId).ConfigureAwait(false);
            if (site == null)
            {
                Log.LogException(
                    $"[ERROR] BackendConfigurationCompliancesService.UpdateFromCalendar: no live SDK Site {model.SiteId} (complianceId: {compliance.Id}, caseId: {model.Id}) - nothing was mutated");
                return new OperationResult(false, _localizationService.GetString("SiteNotFound"));
            }

            // ---- pre-flight complete; everything below this line mutates ----

            await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

            await core.CaseUpdate(model.Id, fieldValueList, checkListValueList).ConfigureAwait(false);
            await core.CaseUpdateFieldValues(model.Id, language).ConfigureAwait(false);

            var foundCase = await sdkDbContext.Cases
                .Where(x => x.Id == model.Id)
                .FirstOrDefaultAsync().ConfigureAwait(false);

            // Only reachable if the case was hard-deleted between the pre-flight check
            // and this re-read. Kept as a guard so the promotion below cannot dereference
            // null; it is the one exit that can still leave the partial write #1157 is
            // about, and it is a race, not a routine outcome.
            if (foundCase != null)
            {
                // Preserve the caller-supplied time-of-day so calendar
                // event.start (Deadline day + CalendarConfiguration.StartHour)
                // survives the round-trip. Existing consumers (task-tracker,
                // compliance-list) keep submitting their previous values —
                // task-tracker uses `task.deadlineTask.toISOString()` and
                // Compliance.Deadline is stored as midnight UTC, so their
                // observable behaviour is unchanged. SpecifyKind re-tags
                // without shifting (model.DoneAt arrives as Unspecified-kind
                // from the JSON binder).
                var newDoneAt = DateTime.SpecifyKind(model.DoneAt, DateTimeKind.Utc);
                foundCase.DoneAtUserModifiable = newDoneAt;
                foundCase.DoneAt = newDoneAt;

                foundCase.SiteId = model.SiteId;
                foundCase.Status = 100;
                foundCase.WorkflowState = Constants.WorkflowStates.Created;
                await foundCase.Update(sdkDbContext).ConfigureAwait(false);

                if (CaseUpdateDelegates.CaseUpdateDelegate != null)
                {
                    var invocationList = CaseUpdateDelegates.CaseUpdateDelegate
                        .GetInvocationList();
                    foreach (var func in invocationList)
                    {
                        func.DynamicInvoke(model.Id);
                    }
                }
                planningCaseSite.Status = 100;
                planningCaseSite = await SetFieldValue(planningCaseSite, foundCase.Id, language).ConfigureAwait(false);

                planningCaseSite.MicrotingSdkCaseId = foundCase.Id;
                planningCaseSite.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                planningCaseSite.DoneByUserId = (int)foundCase.SiteId;
                planningCaseSite.DoneByUserName = site.Name;
                await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);

                var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                    .SingleAsync(x => x.Id == planningCaseSite.PlanningCaseId).ConfigureAwait(false);
                if (planningCase.Status != 100)
                {
                    planningCase.Status = 100;
                    planningCase.MicrotingSdkCaseDoneAt = foundCase.DoneAt;
                    planningCase.MicrotingSdkCaseId = foundCase.Id;
                    planningCase.DoneByUserId = (int)foundCase.SiteId;
                    planningCase.DoneByUserName = planningCaseSite.DoneByUserName;
                    planningCase.WorkflowState = Constants.WorkflowStates.Processed;

                    planningCase = await SetFieldValue(planningCase, foundCase.Id, language).ConfigureAwait(false);
                    await planningCase.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }
                planningCaseSite.PlanningCaseId = planningCase.Id;
                await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }
            else
            {
                return new OperationResult(false, _localizationService.GetString("CaseNotFound"));
            }

            var property = await _backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == compliance.PropertyId).ConfigureAwait(false);

            if (_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                    x.Deadline < DateTime.UtcNow && x.PropertyId == property.Id &&
                    x.WorkflowState != Constants.WorkflowStates.Removed))
            {
                property.ComplianceStatus = 2;
                property.ComplianceStatusThirty = 2;
                await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
            }
            else
            {
                if (!_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                        x.Deadline < DateTime.UtcNow.AddDays(30) && x.PropertyId == property.Id &&
                        x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    property.ComplianceStatusThirty = 0;
                    await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }

                if (!_backendConfigurationPnDbContext.Compliances.AsNoTracking().Any(x =>
                        x.Deadline < DateTime.UtcNow && x.PropertyId == property.Id &&
                        x.WorkflowState != Constants.WorkflowStates.Removed))
                {
                    property.ComplianceStatus = 0;
                    await property.Update(_backendConfigurationPnDbContext).ConfigureAwait(false);
                }
            }

            // Resolve the uid to retract BEFORE returning; the actual CaseDelete is an
            // external platform call that can block for minutes (dev) — run it
            // fire-and-forget. Retraction is best-effort today as well (the shared
            // Update swallows its failures), so guarantees are unchanged.
            int? microtingUidToRetract = foundCase.MicrotingUid;
            if (microtingUidToRetract == null)
            {
                var checkListSite = await sdkDbContext.CheckListSites
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == model.Id)
                    .ConfigureAwait(false);
                microtingUidToRetract = checkListSite?.MicrotingUid;
            }
            if (microtingUidToRetract != null)
            {
                var uidToRetract = (int)microtingUidToRetract;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var retractionCore = await _coreHelper.GetCore().ConfigureAwait(false);
                        await retractionCore.CaseDelete(uidToRetract).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.LogException(ex.Message);
                        Log.LogException(ex.StackTrace);
                    }
                });
            }

            return new OperationResult(true, _localizationService.GetString("CaseHasBeenUpdated"));
        }
        catch (Exception ex)
        {
            Log.LogException(ex.Message);
            Log.LogException(ex.StackTrace);
            return new OperationResult(false, _localizationService.GetString("CaseCouldNotBeUpdated") + $" Exception: {ex.Message}");
        }
    }

    public async Task<OperationResult> Delete(int id)
    {
        var compliance = await _backendConfigurationPnDbContext.Compliances.FirstOrDefaultAsync(x => x.Id == id).ConfigureAwait(false);
        if (compliance == null)
        {
            return new OperationResult(false, _localizationService.GetString("ComplianceNotFound"));
        }
        await compliance.Delete(_backendConfigurationPnDbContext).ConfigureAwait(false);

        return new OperationResult(true, _localizationService.GetString("TaskDeletedSuccessful"));
    }

    public async Task<OperationDataResult<CompliancesStatsModel>> Stats()
    {
        var envTag = await _itemsPlanningPnDbContext.PlanningTags.Where(x => x.Name == "Miljøtilsyn").FirstAsync();
        var complianceList = _backendConfigurationPnDbContext.Compliances;
        var oneWeekInTheFutureCount = await complianceList.CountAsync(x => x.Deadline >= DateTime.UtcNow && x.Deadline <= DateTime.UtcNow.AddDays(7));
        var todayCount = await complianceList.CountAsync(x => x.Deadline.Date <= DateTime.UtcNow.Date && x.WorkflowState != Constants.WorkflowStates.Removed);

        var numberOfPlannedEnvironmentInspectionTagTasks = await _backendConfigurationPnDbContext.AreaRulePlannings.Join(_backendConfigurationPnDbContext.AreaRulePlanningTags,
            planning => planning.Id,
            planningTag => planningTag.AreaRulePlanningId,
            (planning, planningTag) => new { Planning = planning, PlanningTag = planningTag })
            .Where(x => x.Planning.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.Planning.Status)
            .Where(x => x.PlanningTag.ItemPlanningTagId == envTag.Id)
            .CountAsync();

        var numberOfPlannedEnvironmentInspectionTagPlanningsLast30Days = complianceList
            .Where(x => x.Deadline >= DateTime.UtcNow && x.Deadline >= DateTime.UtcNow.AddDays(-30)).ToList()
            .Where(x =>
            {
                var planningTags = _itemsPlanningPnDbContext.PlanningsTags
                    .Where(y => y.PlanningId == x.PlanningId && y.PlanningTagId == envTag.Id)
                    .ToList();
                return planningTags.Any();
            })
            .Count();

        var todayComplianceCountEnvironmentInspectionTag = await complianceList.Where(x => x.Deadline.Date <= DateTime.UtcNow.Date && x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync();
        var todayCountEnvironmentInspectionTag = todayComplianceCountEnvironmentInspectionTag.Where(x =>
        {
            var planningTags = _itemsPlanningPnDbContext.PlanningsTags
                .Where(y => y.PlanningId == x.PlanningId && y.PlanningTagId == envTag.Id)
                .ToList();
            return planningTags.Any();
        }).Count();
        var oldestEnvironmentInspectionTagPlannedTask = todayComplianceCountEnvironmentInspectionTag
            .Where(x =>
            {
                var planningTags = _itemsPlanningPnDbContext.PlanningsTags
                    .Where(y => y.PlanningId == x.PlanningId && y.PlanningTagId == envTag.Id)
                    .ToList();
                return planningTags.Any();
            })
            .OrderBy(x => x.Deadline)
            .FirstOrDefault()?.Deadline;
        var oneWeekCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow && x.Deadline >= DateTime.UtcNow.AddDays(-7));
        var twoWeeksCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-7) && x.Deadline >= DateTime.UtcNow.AddDays(-14));
        var oneMonthCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-14) && x.Deadline >= DateTime.UtcNow.AddDays(-30));
        var twoMonthsCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-30) && x.Deadline >= DateTime.UtcNow.AddDays(-60));
        var threeMonthsCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-60) && x.Deadline >= DateTime.UtcNow.AddDays(-90));
        var sixMonthsCount = await complianceList.CountAsync(x => x.Deadline <= DateTime.UtcNow.AddDays(-90) && x.Deadline >= DateTime.UtcNow.AddDays(-180));
        var moreThanSixMonthsCount = await complianceList.CountAsync(x => x.Deadline < DateTime.UtcNow.AddDays(-180));

        var numberOfWorkorderTasks = await _backendConfigurationPnDbContext.WorkorderCases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask)
            .Where(x => x.LeadingCase == true)
            .CountAsync();

        var oldestWorkorderTask = await _backendConfigurationPnDbContext.WorkorderCases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.Completed && x.CaseStatusesEnum != CaseStatusesEnum.NewTask)
            .Where(x => x.LeadingCase == true)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        var numberOfActiveAreaRulePlannings = _backendConfigurationPnDbContext.AreaRulePlannings
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(x => x.Status)
            .Distinct()
            .ToList()
            .Count;

        var numberOfCompletedEnvironmentInspectionTagPlanningsLast30Days = _itemsPlanningPnDbContext.PlanningCases
            .Where(x => x.Status == 100)
            .Where(x => x.MicrotingSdkCaseDoneAt >= DateTime.UtcNow.AddDays(-30))
            .ToList()
            .Where(x =>
            {
                var planningTags = _itemsPlanningPnDbContext.PlanningsTags
                    .Where(y => y.PlanningId == x.PlanningId && y.PlanningTagId == envTag.Id)
                    .ToList();
                return planningTags.Any();
            })
            .Count();

        var numberOfWorkersWithTimeRegistrationEnabled = await _timePlanningPnDbContext.AssignedSites
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Distinct().CountAsync();

        var numberOfFullDayPlanRegistrationsLastWeek = await _timePlanningPnDbContext.PlanRegistrations
            .Where(x => x.Start1StartedAt != null && x.Stop1StoppedAt != null)
            .Where(x => x.Date >= DateTime.UtcNow.AddDays(-7))
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .CountAsync();

        var totalCount = complianceList.Count();

        var statsModel = new CompliancesStatsModel
        {
            OneWeekInTheFutureCount = oneWeekInTheFutureCount,
            TodayCount = todayCount,
            TotalCount = totalCount,
            OneWeekCount = oneWeekCount,
            TwoWeeksCount = twoWeeksCount,
            OneMonthCount = oneMonthCount,
            TwoMonthsCount = twoMonthsCount,
            ThreeMonthsCount = threeMonthsCount,
            SixMonthsCount = sixMonthsCount,
            MoreThanSixMonthsCount = moreThanSixMonthsCount,
            TodayCountEnvironmentInspectionTag = todayCountEnvironmentInspectionTag,
            DateOfOldestEnvironmentInspectionTagPlannedTask = oldestEnvironmentInspectionTagPlannedTask,
            NumberOfAdHocTasks = numberOfWorkorderTasks,
            DateOfOldestAdHocTask = oldestWorkorderTask?.CreatedAt,
            NumberOfPlannedEnvironmentInspectionTagTasks = numberOfPlannedEnvironmentInspectionTagTasks,
            NumberOfPlannedTasks = numberOfActiveAreaRulePlannings,
            NumberOfCompletedEnvironmentInspectionTagPlanningsLast30Days = numberOfCompletedEnvironmentInspectionTagPlanningsLast30Days,
            NumberOfPlannedEnvironmentInspectionTagPlanningsLast30Days = numberOfPlannedEnvironmentInspectionTagPlanningsLast30Days,
            NumberOfWorkersWithTimeRegistrationEnabled = numberOfWorkersWithTimeRegistrationEnabled,
            NumberOfFullDayTimeRegistrationsLastWeek = numberOfFullDayPlanRegistrationsLastWeek
        };

        return new OperationDataResult<CompliancesStatsModel>(true, statsModel);
    }

    private async Task<PlanningCaseSite> SetFieldValue(PlanningCaseSite planningCaseSite, int caseId, Language language)
    {
        var planning = _itemsPlanningPnDbContext.Plannings
            .SingleOrDefault(x => x.Id == planningCaseSite.PlanningId);
        var caseIds = new List<int>
        {
            planningCaseSite.MicrotingSdkCaseId
        };

        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language).ConfigureAwait(false);

        if (planning == null)
        {
            return planningCaseSite;
        }
        if (planning.NumberOfImagesEnabled)
        {
            planningCaseSite.NumberOfImages = fieldValues
                .Where(fieldValue => fieldValue.FieldType == Constants.FieldTypes.Picture)
                .Count(fieldValue => fieldValue.UploadedData != null);
        }

        return planningCaseSite;
    }

    private async Task<PlanningCase> SetFieldValue(PlanningCase planningCase, int caseId, Language language)
    {
        var core = await _coreHelper.GetCore().ConfigureAwait(false);
        var planning = await _itemsPlanningPnDbContext.Plannings
            .SingleOrDefaultAsync(x => x.Id == planningCase.PlanningId).ConfigureAwait(false);
        var caseIds = new List<int> { planningCase.MicrotingSdkCaseId };
        var fieldValues = await core.Advanced_FieldValueReadList(caseIds, language).ConfigureAwait(false);

        if (planning == null)
        {
            return planningCase;
        }
        if (planning.NumberOfImagesEnabled)
        {
            planningCase.NumberOfImages = fieldValues
                .Where(fieldValue => fieldValue.FieldType == Constants.FieldTypes.Picture)
                .Count(fieldValue => fieldValue.UploadedData != null);
        }

        return planningCase;
    }
}