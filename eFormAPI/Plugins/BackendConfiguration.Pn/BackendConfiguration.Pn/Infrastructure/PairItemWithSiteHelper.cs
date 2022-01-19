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

using Microting.ItemsPlanningBase.Infrastructure.Enums;

namespace BackendConfiguration.Pn.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Infrastructure;
    using Microting.eForm.Infrastructure.Constants;
    using Microting.eForm.Infrastructure.Data.Entities;
    using Microting.eFormApi.BasePn.Abstractions;
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;

    public class PairItemWichSiteHelper
    {
        private readonly ItemsPlanningPnDbContext _itemsPlanningPnDbContext;
        private readonly IEFormCoreService _coreService;

        public PairItemWichSiteHelper(
            ItemsPlanningPnDbContext itemsPlanningPnDbContext,
            IEFormCoreService coreService)
        {
            _itemsPlanningPnDbContext = itemsPlanningPnDbContext;
            _coreService = coreService;
        }

        public async Task Pair(List<int> assignmentSiteIds, int relatedEFormId, int planningId, int planningFolderId)
        {
            var sdkCore =
                await _coreService.GetCore();
            await using var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
            foreach (var assignmentSiteId in assignmentSiteIds)
            {

                var sdkSite = await sdkDbContext.Sites.SingleAsync(x => x.Id == assignmentSiteId);
                var language = await sdkDbContext.Languages.SingleAsync(x => x.Id == sdkSite.LanguageId);
                var mainElement = await sdkCore.ReadeForm(relatedEFormId, language);

                var planning = await _itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == planningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Include(x => x.NameTranslations)
                    .Select(x => new
                    {
                        x.Id, x.Type, x.PlanningNumber, x.BuildYear, x.StartDate, x.PushMessageOnDeployment,
                        x.SdkFolderId, x.NameTranslations, x.RepeatEvery, x.RepeatType
                    })
                    .FirstAsync();

                var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == planningFolderId);
                var folderMicrotingId = folder.MicrotingUid.ToString();

                // get planning cases
                var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                    .Where(x => x.PlanningId == planningId)
                    .Where(x => x.MicrotingSdkeFormId == relatedEFormId)
                    .FirstOrDefaultAsync();

                if (planningCase == null)
                {
                    planningCase = new PlanningCase
                    {
                        PlanningId = planning.Id,
                        Status = 66,
                        MicrotingSdkeFormId = relatedEFormId
                    };
                    await planningCase.Create(_itemsPlanningPnDbContext);
                }

                var casesToDelete = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningId == planning.Id)
                    .Where(x => x.MicrotingSdkSiteId == assignmentSiteId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                    .ToListAsync();

                foreach (var caseToDelete in casesToDelete)
                {
                    var caseDto = await sdkCore.CaseLookupCaseId(caseToDelete.MicrotingSdkCaseId);
                    if (caseDto.MicrotingUId != null)
                        await sdkCore.CaseDelete((int)caseDto.MicrotingUId);
                    caseToDelete.WorkflowState = Constants.WorkflowStates.Retracted;
                    await caseToDelete.Update(_itemsPlanningPnDbContext);
                }

                if (planningCase.Status == 100)
                {
                    var planningCaseSite =
                        await _itemsPlanningPnDbContext.PlanningCaseSites.SingleOrDefaultAsync(x =>
                            x.PlanningCaseId == planningCase.Id
                            && x.MicrotingSdkSiteId == assignmentSiteId
                            && x.WorkflowState != Constants.WorkflowStates.Removed);

                    if (planningCaseSite == null)
                    {
                        planningCaseSite = new PlanningCaseSite
                        {
                            MicrotingSdkSiteId = assignmentSiteId,
                            MicrotingSdkeFormId = relatedEFormId,
                            Status = 2,
                            PlanningId = planning.Id,
                            PlanningCaseId = planningCase.Id
                        };

                        await planningCaseSite.Create(_itemsPlanningPnDbContext);
                    }

                    planningCaseSite.Status = planningCaseSite.Status == 100 ? planningCaseSite.Status : 2;
                    planningCaseSite.WorkflowState = Constants.WorkflowStates.Retracted;
                    await planningCaseSite.Update(_itemsPlanningPnDbContext);
                }
                else
                {
                    var translation = planning.NameTranslations
                        .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                        .Where(x => x.LanguageId == language.Id)
                        .Select(x => x.Name)
                        .FirstOrDefault();

                    mainElement.Label = string.IsNullOrEmpty(planning.PlanningNumber)
                        ? ""
                        : planning.PlanningNumber;
                    if (!string.IsNullOrEmpty(translation))
                    {
                        mainElement.Label +=
                            string.IsNullOrEmpty(mainElement.Label) ? $"{translation}" : $" - {translation}";
                    }

                    if (!string.IsNullOrEmpty(planning.BuildYear))
                    {
                        mainElement.Label += string.IsNullOrEmpty(mainElement.Label)
                            ? $"{planning.BuildYear}"
                            : $" - {planning.BuildYear}";
                    }

                    if (!string.IsNullOrEmpty(planning.Type))
                    {
                        mainElement.Label += string.IsNullOrEmpty(mainElement.Label)
                            ? $"{planning.Type}"
                            : $" - {planning.Type}";
                    }

                    if (mainElement.ElementList.Count == 1)
                    {
                        mainElement.ElementList[0].Label = mainElement.Label;
                    }

                    mainElement.CheckListFolderName = folderMicrotingId;
                    mainElement.StartDate = DateTime.Now.ToUniversalTime();
                    mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                    // mainElement.PushMessageBody = mainElement.Label;
                    // mainElement.PushMessageTitle = folder.Name;
                    // if (folder.ParentId != null)
                    // {
                    //     var parentFolder = await sdkDbContext.Folders.SingleAsync(x => x.Id == folder.ParentId);
                    //     mainElement.PushMessageTitle = parentFolder.Name;
                    //     mainElement.PushMessageBody = $"{folder.Name}\n{mainElement.Label}";
                    // }

                    var planningCaseSite =
                        await _itemsPlanningPnDbContext.PlanningCaseSites
                            .Where(x => x.PlanningCaseId == planningCase.Id)
                            .Where(x => x.MicrotingSdkSiteId == assignmentSiteId)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                            .FirstOrDefaultAsync();

                    if (planningCaseSite == null)
                    {
                        planningCaseSite = new PlanningCaseSite
                        {
                            MicrotingSdkSiteId = assignmentSiteId,
                            MicrotingSdkeFormId = relatedEFormId,
                            Status = 66,
                            PlanningId = planning.Id,
                            PlanningCaseId = planningCase.Id
                        };

                        await planningCaseSite.Create(_itemsPlanningPnDbContext);
                    }

                    if (planningCaseSite.MicrotingSdkCaseDoneAt.HasValue)
                    {
                        var unixTimestamp = (long)(planningCaseSite.MicrotingSdkCaseDoneAt.Value
                                .Subtract(new DateTime(1970, 1, 1)))
                            .TotalSeconds;

                        mainElement.ElementList[0].Description.InderValue = unixTimestamp.ToString();
                    }

                    if (planningCaseSite.MicrotingSdkCaseId < 1 && planning.StartDate <= DateTime.Now)
                    {
                        // ReSharper disable once PossibleInvalidOperationException
                        if (planning.PushMessageOnDeployment)
                        {
                            var body = "";
                            folder = await GetTopFolder((int)planning.SdkFolderId, sdkDbContext);
                            if (folder != null)
                            {
                                //planningPnModel.SdkFolderId = sdkDbContext.Folders
                                //    .FirstOrDefault(y => y.Id == planningPnModel.SdkFolderId)
                                //    ?.Id;
                                var folderTranslation =
                                    await sdkDbContext.FolderTranslations.SingleOrDefaultAsync(x =>
                                        x.FolderId == folder.Id && x.LanguageId == sdkSite.LanguageId);
                                body = $"{folderTranslation.Name} ({sdkSite.Name};{DateTime.Now:d, M yyyy})";
                            }

                            var planningNameTranslation =
                                planning.NameTranslations.FirstOrDefault(x => x.LanguageId == sdkSite.LanguageId);

                            mainElement.PushMessageBody = body;
                            mainElement.PushMessageTitle = planningNameTranslation?.Name;
                            if (planning.RepeatEvery == 0 && planning.RepeatType == RepeatType.Day)
                            {
                                mainElement.Repeated = 0;
                            }
                        }

                        var caseId = await sdkCore.CaseCreate(mainElement, "", (int)sdkSite.MicrotingUid, null);
                        if (caseId != null)
                        {
                            planningCaseSite.MicrotingSdkCaseId =
                                sdkDbContext.Cases.Single(x => x.MicrotingUid == caseId).Id;
                            await planningCaseSite.Update(_itemsPlanningPnDbContext);
                        }

                        var now = DateTime.UtcNow;
                        var dbPlanning = await _itemsPlanningPnDbContext.Plannings.SingleAsync(x => x.Id == planning.Id);
                        switch (dbPlanning.RepeatType)
                        {
                            case RepeatType.Day:
                                dbPlanning.NextExecutionTime = now.AddDays(dbPlanning.RepeatEvery);
                                break;
                            case RepeatType.Week:
                                var startOfWeek =
                                    new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).StartOfWeek(
                                        (DayOfWeek) dbPlanning.DayOfWeek);
                                dbPlanning.NextExecutionTime = startOfWeek.AddDays(dbPlanning.RepeatEvery * 7);
                                break;
                            case RepeatType.Month:
                                dbPlanning.DayOfMonth ??= 1;
                                if (dbPlanning.DayOfMonth == 0)
                                {
                                    dbPlanning.DayOfMonth = 1;
                                }
                                var startOfMonth = new DateTime(now.Year, now.Month, (int) dbPlanning.DayOfMonth, 0, 0, 0);
                                dbPlanning.NextExecutionTime = startOfMonth.AddMonths(dbPlanning.RepeatEvery);
                                break;
                        }
                        dbPlanning.LastExecutedTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                        await dbPlanning.Update(_itemsPlanningPnDbContext);
                    }
                }
            }
        }

        private static async Task<Folder> GetTopFolder(int folderId, MicrotingDbContext dbContext)
        {
            var result = await dbContext.Folders.FirstOrDefaultAsync(y => y.Id == folderId);
            if (result.ParentId != null)
            {
                result = await GetTopFolder((int)result.ParentId, dbContext);
            }
            return result;
        }
    }


    public static class DateTimeExtensions
    {
        public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
        {
            int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            return dt.AddDays(-1 * diff).Date;
        }
    }
}