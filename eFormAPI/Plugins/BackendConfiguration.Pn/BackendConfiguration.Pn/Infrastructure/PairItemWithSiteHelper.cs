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
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
    using Microting.ItemsPlanningBase.Infrastructure.Enums;

    public static class PairItemWithSiteHelper
    {
        public static async Task Pair(List<int> assignmentSiteIds, int relatedEFormId, int planningId, int planningFolderId, eFormCore.Core sdkCore, ItemsPlanningPnDbContext _itemsPlanningPnDbContext, bool useStartDateAsStartOfPeriod)
        {
            var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();
            foreach (var assignmentSiteId in assignmentSiteIds)
            {

                var sdkSite = await sdkDbContext.Sites.SingleAsync(x => x.Id == assignmentSiteId).ConfigureAwait(false);
                var language = await sdkDbContext.Languages.SingleAsync(x => x.Id == sdkSite.LanguageId)
                    .ConfigureAwait(false);
                var mainElement = await sdkCore.ReadeForm(relatedEFormId, language).ConfigureAwait(false);

                var planning = await _itemsPlanningPnDbContext.Plannings
                    .Where(x => x.Id == planningId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Include(x => x.NameTranslations)
                    .Select(x => new
                    {
                        x.Id, x.Type, x.PlanningNumber, x.BuildYear, x.StartDate, x.PushMessageOnDeployment,
                        x.SdkFolderId, x.NameTranslations, x.RepeatEvery, x.RepeatType
                    })
                    .FirstAsync().ConfigureAwait(false);

                var folder = await sdkDbContext.Folders.SingleAsync(x => x.Id == planningFolderId)
                    .ConfigureAwait(false);
                var folderMicrotingId = folder.MicrotingUid.ToString();

                // get planning cases
                var planningCase = await _itemsPlanningPnDbContext.PlanningCases
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                    .Where(x => x.PlanningId == planningId)
                    .Where(x => x.MicrotingSdkeFormId == relatedEFormId)
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                if (planningCase == null)
                {
                    planningCase = new PlanningCase
                    {
                        PlanningId = planning.Id,
                        Status = 66,
                        MicrotingSdkeFormId = relatedEFormId
                    };
                    await planningCase.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }

                var casesToDelete = await _itemsPlanningPnDbContext.PlanningCaseSites
                    .Where(x => x.PlanningId == planning.Id)
                    .Where(x => x.MicrotingSdkSiteId == assignmentSiteId)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
                    .Where(x => x.WorkflowState != Constants.WorkflowStates.Retracted)
                    .ToListAsync().ConfigureAwait(false);

                foreach (var caseToDelete in casesToDelete)
                {
                    try
                    {
                        var caseDto = await sdkCore.CaseLookupCaseId(caseToDelete.MicrotingSdkCaseId)
                            .ConfigureAwait(false);
                        if (caseDto.MicrotingUId != null)
                            await sdkCore.CaseDelete((int) caseDto.MicrotingUId).ConfigureAwait(false);
                        caseToDelete.WorkflowState = Constants.WorkflowStates.Retracted;
                        await caseToDelete.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        caseToDelete.WorkflowState = Constants.WorkflowStates.Retracted;
                        await caseToDelete.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                        Console.WriteLine(e);
                    }
                }

                if (planningCase.Status == 100 && planning.RepeatType != RepeatType.Day && planning.RepeatEvery != 0)
                {
                    var planningCaseSite =
                        await _itemsPlanningPnDbContext.PlanningCaseSites.SingleOrDefaultAsync(x =>
                            x.PlanningCaseId == planningCase.Id
                            && x.MicrotingSdkSiteId == assignmentSiteId
                            && x.WorkflowState != Constants.WorkflowStates.Removed).ConfigureAwait(false);

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

                        await planningCaseSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }

                    planningCaseSite.Status = planningCaseSite.Status == 100 ? planningCaseSite.Status : 2;
                    planningCaseSite.WorkflowState = Constants.WorkflowStates.Retracted;
                    await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
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
                            .FirstOrDefaultAsync().ConfigureAwait(false);

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

                        await planningCaseSite.Create(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }

                    if (planningCaseSite.MicrotingSdkCaseDoneAt.HasValue)
                    {
                        var unixTimestamp = (long) planningCaseSite.MicrotingSdkCaseDoneAt.Value
                            .Subtract(new DateTime(1970, 1, 1))
                            .TotalSeconds;

                        mainElement.ElementList[0].Description.InderValue = unixTimestamp.ToString();
                    }

                    if (planningCaseSite.MicrotingSdkCaseId < 1 && planning.StartDate <= DateTime.Now)
                    {
                        // ReSharper disable once PossibleInvalidOperationException
                        if (planning.PushMessageOnDeployment)
                        {
                            var body = "";
                            folder = await GetTopFolder((int) planning.SdkFolderId, sdkDbContext).ConfigureAwait(false);
                            if (folder != null)
                            {
                                //planningPnModel.SdkFolderId = sdkDbContext.Folders
                                //    .FirstOrDefault(y => y.Id == planningPnModel.SdkFolderId)
                                //    ?.Id;
                                var folderTranslation =
                                    await sdkDbContext.FolderTranslations.SingleOrDefaultAsync(x =>
                                            x.FolderId == folder.Id && x.LanguageId == sdkSite.LanguageId)
                                        .ConfigureAwait(false);
                                body = $"{folderTranslation.Name} ({sdkSite.Name};{DateTime.Now:dd.MM.yyyy})";
                            }

                            var planningNameTranslation =
                                planning.NameTranslations.FirstOrDefault(x => x.LanguageId == sdkSite.LanguageId);

                            if (planning.RepeatType != RepeatType.Day && planning.RepeatEvery != 0)
                            {
                                mainElement.PushMessageBody = body;
                                mainElement.PushMessageTitle = planningNameTranslation?.Name;
                            }
                        }

                        if (planning.RepeatEvery == 0 && planning.RepeatType == RepeatType.Day)
                        {
                            mainElement.Repeated = 0;
                        }

                        if (mainElement.Label == "Morgenrundtur")
                        {
                            mainElement.Repeated = 1;
                        }

                        var caseId = await sdkCore.CaseCreate(mainElement, "", (int) sdkSite.MicrotingUid, null)
                            .ConfigureAwait(false);
                        if (caseId != null)
                        {
                            if (sdkDbContext.Cases.Any(x => x.MicrotingUid == caseId))
                            {
                                planningCaseSite.MicrotingSdkCaseId =
                                    sdkDbContext.Cases.Single(x => x.MicrotingUid == caseId).Id;
                            }
                            else
                            {
                                planningCaseSite.MicrotingCheckListSitId =
                                    sdkDbContext.CheckListSites.Single(x => x.MicrotingUid == caseId).Id;
                            }

                            await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                        }

                        //var now = DateTime.UtcNow;
                        var dbPlanning = await _itemsPlanningPnDbContext.Plannings.SingleAsync(x => x.Id == planning.Id)
                            .ConfigureAwait(false);

                        var now = DateTime.UtcNow;
                        var startDate = dbPlanning.StartDate;
                        if (!useStartDateAsStartOfPeriod)
                        {
                            startDate = new DateTime(now.Year, 1, 1, 0, 0, 0);
                        }
                        else
                        {
                            dbPlanning.DayOfMonth = startDate.Day;
                        }
                        //now = DateTime.UtcNow;
                        switch (dbPlanning.RepeatType)
                        {
                            case RepeatType.Day:
                                // dbPlanning.NextExecutionTime = now.AddDays(dbPlanning.RepeatEvery);
                                if (dbPlanning.RepeatEvery > 1)
                                {
                                    var diff = (now - startDate).TotalDays;
                                    var multiplier = (int) (diff / planning.RepeatEvery);
                                    var nextExecutionTime =
                                        startDate.AddDays(multiplier * planning.RepeatEvery);
                                    if (nextExecutionTime < now)
                                    {
                                        nextExecutionTime = nextExecutionTime.AddDays(planning.RepeatEvery);
                                    }
                                    dbPlanning.NextExecutionTime = nextExecutionTime;
                                }

                                break;
                            case RepeatType.Week:
                            {
                                var diff = (now - startDate).TotalDays;
                                var multiplier = (int) (diff / (planning.RepeatEvery * 7));
                                var dayOfWeek = (int) dbPlanning.DayOfWeek!;
                                if (dayOfWeek == 0)
                                {
                                    dayOfWeek = 7;
                                }

                                var startOfWeek =
                                    startDate.StartOfWeek(
                                        (DayOfWeek) dayOfWeek);
                                if (startOfWeek.Year != startDate.Year)
                                {
                                    startOfWeek = startOfWeek.AddDays(7);
                                }

                                var nextExecutionTime =
                                    startOfWeek.AddDays(multiplier * planning.RepeatEvery * 7);
                                if (nextExecutionTime < now)
                                {
                                    nextExecutionTime = nextExecutionTime.AddDays(planning.RepeatEvery * 7);
                                }
                                dbPlanning.NextExecutionTime = nextExecutionTime;

                            }
                                // dbPlanning.NextExecutionTime = startOfWeek.AddDays(dbPlanning.RepeatEvery * 7);
                                break;
                            case RepeatType.Month:
                            {
                                if (dbPlanning.DayOfMonth == 0)
                                {
                                    dbPlanning.DayOfMonth = 1;
                                }
                                if (planning.RepeatEvery == 1)
                                {
                                    dbPlanning.NextExecutionTime =
                                        new DateTime(startDate.Year, startDate.Month + 1, (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                    if (dbPlanning.NextExecutionTime < now)
                                    {
                                        dbPlanning.NextExecutionTime =
                                            new DateTime(startDate.Year, now.Month + 1, (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                    }
                                }
                                else
                                {
                                    if (useStartDateAsStartOfPeriod)
                                    {
                                        dbPlanning.NextExecutionTime =
                                            new DateTime(startDate.Year, startDate.Month,
                                                (int)dbPlanning.DayOfMonth!,
                                                0, 0, 0).AddMonths(dbPlanning.RepeatEvery);
                                        if (dbPlanning.NextExecutionTime < now)
                                        {
                                            dbPlanning.NextExecutionTime =
                                                new DateTime(startDate.Year, now.Month,
                                                    (int)dbPlanning.DayOfMonth!,
                                                    0, 0, 0).AddMonths(dbPlanning.RepeatEvery);
                                        }
                                    } else
                                    {
                                        switch (dbPlanning.RepeatEvery)
                                        {
                                            case 2:
                                            {
                                                var months = new[] { 1, 3, 5, 7, 9, 11 };

                                                if (startDate < now)
                                                {
                                                    startDate = now;
                                                }

                                                if (months.Contains(startDate.Month))
                                                {
                                                    dbPlanning.NextExecutionTime =
                                                        new DateTime(startDate.Year, startDate.Month + 2,
                                                            (int)dbPlanning.DayOfMonth!,
                                                            0, 0, 0);
                                                }
                                                else
                                                {
                                                    dbPlanning.NextExecutionTime =
                                                        new DateTime(startDate.Year, startDate.Month + 1,
                                                            (int)dbPlanning.DayOfMonth!,
                                                            0, 0, 0);
                                                }
                                            }
                                                break;
                                            case 3:
                                            {
                                                if (startDate < now)
                                                {
                                                    startDate = now;
                                                }

                                                var months = new[] { 1, 4, 7, 10 };
                                                if (months.Contains(startDate.Month))
                                                {
                                                    dbPlanning.NextExecutionTime =
                                                        new DateTime(startDate.Year, startDate.Month + 3,
                                                            (int)dbPlanning.DayOfMonth!,
                                                            0, 0, 0);
                                                }
                                                else
                                                {
                                                    months = new[] { 2, 5, 8, 11 };
                                                    if (months.Contains(startDate.Month))
                                                    {
                                                        dbPlanning.NextExecutionTime =
                                                            new DateTime(startDate.Year, startDate.Month + 2,
                                                                (int)dbPlanning.DayOfMonth!,
                                                                0, 0, 0);
                                                    }
                                                    else
                                                    {
                                                        dbPlanning.NextExecutionTime =
                                                            new DateTime(startDate.Year, startDate.Month + 1,
                                                                (int)dbPlanning.DayOfMonth!,
                                                                0, 0, 0);
                                                    }
                                                }
                                            }
                                                break;
                                            case 6:
                                                if (startDate.Month < 6)
                                                {
                                                    dbPlanning.NextExecutionTime =
                                                        new DateTime(startDate.Year, startDate.Month,
                                                            (int)dbPlanning.DayOfMonth!,
                                                            0, 0, 0);
                                                }
                                                else
                                                {
                                                    dbPlanning.NextExecutionTime =
                                                        new DateTime(startDate.Year + 1, startDate.Month,
                                                            (int)dbPlanning.DayOfMonth!,
                                                            0, 0, 0);
                                                }

                                                if (dbPlanning.NextExecutionTime < now)
                                                {
                                                    dbPlanning.NextExecutionTime =
                                                        ((DateTime)dbPlanning.NextExecutionTime).AddMonths(6);
                                                }

                                                break;
                                            case 12:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 1, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 24:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 2, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 36:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 3, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 48:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 4, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 60:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 5, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 72:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 6, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 84:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 7, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 96:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 8, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 108:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 9, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                            case 120:
                                                dbPlanning.NextExecutionTime =
                                                    new DateTime(startDate.Year + 10, startDate.Month,
                                                        (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                                break;
                                        }
                                    }

                                    // var diff = now.Month;
                                    // var multiplier = diff / planning.RepeatEvery;
                                    // var startOfMonth =
                                    //     new DateTime(now.Year, 1, 1, 0, 0, 0).AddMonths(multiplier * planning.RepeatEvery);
                                    // // if (startOfMonth.Year != now.Year)
                                    // // {
                                    // //     startOfMonth = startOfMonth.AddMonths(1);
                                    // // }
                                    //
                                    // var nextExecutionTime =
                                    //     new DateTime(startOfMonth.Year, startOfMonth.Month, (int)dbPlanning.DayOfMonth!, 0, 0, 0);
                                    // dbPlanning.NextExecutionTime = nextExecutionTime;
                                }
                            }
                                // dbPlanning.DayOfMonth ??= 1;
                                // if (dbPlanning.DayOfMonth == 0)
                                // {
                                //     dbPlanning.DayOfMonth = 1;
                                // }
                                // var startOfMonth = new DateTime(now.Year, now.Month, (int) dbPlanning.DayOfMonth, 0, 0, 0);
                                // dbPlanning.NextExecutionTime = startOfMonth.AddMonths(dbPlanning.RepeatEvery);
                                break;
                        }

                        dbPlanning.LastExecutedTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                        await dbPlanning.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task<Folder> GetTopFolder(int folderId, MicrotingDbContext dbContext)
        {
            var result = await dbContext.Folders.FirstOrDefaultAsync(y => y.Id == folderId).ConfigureAwait(false);
            if (result.ParentId != null)
            {
                result = await GetTopFolder((int)result.ParentId, dbContext).ConfigureAwait(false);
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