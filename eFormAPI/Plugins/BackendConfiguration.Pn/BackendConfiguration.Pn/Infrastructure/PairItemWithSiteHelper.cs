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

#nullable enable
using System.Globalization;
using System.Threading;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using JetBrains.Annotations;

namespace BackendConfiguration.Pn.Infrastructure;

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
    public static async Task Pair(List<int> assignmentSiteIds, int relatedEFormId, int planningId,
        int planningFolderId, eFormCore.Core sdkCore, ItemsPlanningPnDbContext _itemsPlanningPnDbContext,
        bool useStartDateAsStartOfPeriod,
        IBackendConfigurationLocalizationService? localizationService)
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
                    if (caseToDelete.MicrotingSdkCaseId != 0)
                    {
                        var caseDto = await sdkCore.CaseLookupCaseId(caseToDelete.MicrotingSdkCaseId)
                            .ConfigureAwait(false);
                        if (caseDto.MicrotingUId != null)
                            await sdkCore.CaseDelete((int)caseDto.MicrotingUId).ConfigureAwait(false);
                        caseToDelete.WorkflowState = Constants.WorkflowStates.Retracted;
                        await caseToDelete.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    }
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
                    await _itemsPlanningPnDbContext.PlanningCaseSites.FirstOrDefaultAsync(x =>
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

                // planningCaseSite.Status = planningCaseSite.Status == 100 ? planningCaseSite.Status : 2;
                // planningCaseSite.WorkflowState = Constants.WorkflowStates.Retracted;
                // await planningCaseSite.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
            }
            else
            {
                var dbPlanning = await _itemsPlanningPnDbContext.Plannings.SingleAsync(x => x.Id == planning.Id)
                    .ConfigureAwait(false);
                if (dbPlanning.StartDate > DateTime.Now)
                {
                    dbPlanning.LastExecutedTime = null;
                    await dbPlanning.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                    continue;
                }

                var now = DateTime.UtcNow;
                var startDate = dbPlanning.StartDate;
                dbPlanning.DayOfMonth = startDate.Day;

                switch (dbPlanning.RepeatType)
                {
                    case RepeatType.Day:
                        if (dbPlanning.RepeatEvery > 0)
                        {
                            var diff = (now - startDate).TotalDays;
                            var multiplier = (int)(diff / planning.RepeatEvery);
                            var nextExecutionTime =
                                startDate.AddDays(multiplier * planning.RepeatEvery);
                            if (nextExecutionTime < now)
                            {
                                nextExecutionTime = nextExecutionTime.AddDays(planning.RepeatEvery);
                            }

                            dbPlanning.NextExecutionTime = nextExecutionTime;
                        }
                        else
                        {
                            mainElement.Repeated = dbPlanning.RepeatEvery;
                        }

                        break;
                    case RepeatType.Week:
                    {
                        var nextExecutionTime = dbPlanning.StartDate.AddDays(planning.RepeatEvery * 7);
                        while (nextExecutionTime < now)
                        {
                            nextExecutionTime = nextExecutionTime.AddDays(planning.RepeatEvery * 7);
                        }

                        dbPlanning.NextExecutionTime = nextExecutionTime;

                    }
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
                                new DateTime(startDate.Year, startDate.Month, (int)dbPlanning.DayOfMonth!, 0, 0, 0)
                                    .AddMonths(1);

                            while (dbPlanning.NextExecutionTime < now)
                            {
                                dbPlanning.NextExecutionTime =
                                    dbPlanning.NextExecutionTime?.AddMonths(dbPlanning.RepeatEvery);
                            }
                        }
                        else
                        {
                            dbPlanning.NextExecutionTime =
                                new DateTime(startDate.Year, startDate.Month,
                                    (int)dbPlanning.DayOfMonth!,
                                    0, 0, 0).AddMonths(dbPlanning.RepeatEvery);

                            while (dbPlanning.NextExecutionTime < now)
                            {
                                dbPlanning.NextExecutionTime =
                                    dbPlanning.NextExecutionTime?.AddMonths(dbPlanning.RepeatEvery);
                            }
                        }
                    }
                        break;
                }

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

                if (dbPlanning.NextExecutionTime != null && localizationService != null)
                {
                    DateTime beginningOfTime = new DateTime(2020, 1, 1);
                    mainElement.DisplayOrder = ((DateTime)dbPlanning.NextExecutionTime - beginningOfTime).Days;
                    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(language.LanguageCode);

                    if (dbPlanning.RepeatEvery == 0 && dbPlanning.RepeatType == RepeatType.Day)
                    {
                        mainElement.EndDate = DateTime.UtcNow.AddYears(10);
                    } else
                    {
                        mainElement.BadgeCountEnabled = true;

                        if (string.IsNullOrEmpty(mainElement.ElementList[0].Description.InderValue))
                        {
                            mainElement.ElementList[0].Description.InderValue =
                                $"<strong>{localizationService.GetString("Deadline")}: {((DateTime)dbPlanning.NextExecutionTime).AddDays(-1).ToString("dd.MM.yyyy")}</strong>";
                        }
                        else
                        {
                            mainElement.ElementList[0].Description.InderValue +=
                                $"<br><strong>{localizationService.GetString("Deadline")}: {((DateTime)dbPlanning.NextExecutionTime).AddDays(-1).ToString("dd.MM.yyyy")}</strong>";
                        }

                        mainElement.EndDate = (DateTime)dbPlanning.NextExecutionTime;
                    }
                }
                else
                {
                    mainElement.EndDate = DateTime.Now.AddYears(10);
                }

                mainElement.CheckListFolderName = folderMicrotingId;
                mainElement.StartDate = DateTime.Now.ToUniversalTime();

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
                    var unixTimestamp = (long)planningCaseSite.MicrotingSdkCaseDoneAt.Value
                        .Subtract(new DateTime(1970, 1, 1))
                        .TotalSeconds;

                    mainElement.ElementList[0].Description.InderValue = unixTimestamp.ToString();
                }

                if (planningCaseSite.MicrotingSdkCaseId < 1 && planning.StartDate <= DateTime.Now)
                {
                    var body = "";
                    folder = await GetTopFolder((int)planning.SdkFolderId, sdkDbContext).ConfigureAwait(false);
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

                    mainElement.PushMessageBody = body;
                    mainElement.PushMessageTitle = planningNameTranslation?.Name;

                    if (planning.RepeatEvery == 0 && planning.RepeatType == RepeatType.Day)
                    {
                        mainElement.Repeated = 0;
                    }

                    var caseId = await sdkCore.CaseCreate(mainElement, "", (int)sdkSite.MicrotingUid, null)
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

                    dbPlanning.ShowExpireDate = true;
                    dbPlanning.LastExecutedTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                    await dbPlanning.Update(_itemsPlanningPnDbContext).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<Folder> GetTopFolder(int folderId, MicrotingDbContext dbContext)
    {
        var result = await dbContext.Folders.FirstAsync(y => y.Id == folderId).ConfigureAwait(false);
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