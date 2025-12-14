/*
The MIT License (MIT)

Copyright (c) 2007 - 2023 Microting A/S

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

namespace BackendConfiguration.Pn.Integration.Test;

using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using Services.BackendConfigurationCompliancesService;
using Services.BackendConfigurationLocalizationService;
using Microting.eFormApi.BasePn.Abstractions;
using NSubstitute;
using eFormCore;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationCompliancesServiceStatsTest : TestBaseSetup
{
    [Test]
    public async Task Stats_WithNoData_ReturnsZeroValues()
    {
        // Arrange
        var core = await GetCore();
        var localizationService = new BackendConfigurationLocalizationService();
        var userService = Substitute.For<IUserService>();
        var connectionString = MicrotingDbContext!.Database.GetConnectionString();
        
        // Create required planning tag "Miljøtilsyn"
        var envTag = new PlanningTag
        {
            Name = "Miljøtilsyn",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(envTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
        
        var compliancesService = new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext,
            BackendConfigurationPnDbContext!,
            userService,
            localizationService,
            new EFormCoreService(connectionString!),
            TimePlanningPnDbContext!
        );

        // Act
        var result = await compliancesService.Stats();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.Not.Null);
        Assert.That(result.Model.TotalCount, Is.EqualTo(0));
        Assert.That(result.Model.TodayCount, Is.EqualTo(0));
        Assert.That(result.Model.OneWeekInTheFutureCount, Is.EqualTo(0));
        Assert.That(result.Model.OneWeekCount, Is.EqualTo(0));
        Assert.That(result.Model.TwoWeeksCount, Is.EqualTo(0));
        Assert.That(result.Model.OneMonthCount, Is.EqualTo(0));
        Assert.That(result.Model.TwoMonthsCount, Is.EqualTo(0));
        Assert.That(result.Model.ThreeMonthsCount, Is.EqualTo(0));
        Assert.That(result.Model.SixMonthsCount, Is.EqualTo(0));
        Assert.That(result.Model.MoreThanSixMonthsCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Stats_WithMultipleCompliances_CountsCorrectly()
    {
        // Arrange
        var core = await GetCore();
        var localizationService = new BackendConfigurationLocalizationService();
        var userService = Substitute.For<IUserService>();

        // Create planning tag "Miljøtilsyn" required by Stats method
        var envTag = new PlanningTag
        {
            Name = "Miljøtilsyn",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(envTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Create compliances with different deadlines
        var random = new Random();
        var now = DateTime.UtcNow;

        // 1 compliance one week in the future
        await CreateCompliance(now.AddDays(5));
        
        // 2 compliances today or past
        await CreateCompliance(now);
        await CreateCompliance(now.AddDays(-1));
        
        // 1 compliance one week ago
        await CreateCompliance(now.AddDays(-5));
        
        // 1 compliance two weeks ago
        await CreateCompliance(now.AddDays(-10));
        
        // 1 compliance one month ago
        await CreateCompliance(now.AddDays(-20));
        
        // 1 compliance two months ago
        await CreateCompliance(now.AddDays(-45));
        
        // 1 compliance three months ago
        await CreateCompliance(now.AddDays(-75));
        
        // 1 compliance six months ago
        await CreateCompliance(now.AddDays(-120));
        
        // 1 compliance more than six months ago
        await CreateCompliance(now.AddDays(-200));

        var compliancesService = new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext,
            BackendConfigurationPnDbContext!,
            userService,
            localizationService,
            new EFormCoreService(MicrotingDbContext!.Database.GetConnectionString()!),
            TimePlanningPnDbContext!
        );

        // Act
        var result = await compliancesService.Stats();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.Not.Null);
        Assert.That(result.Model.TotalCount, Is.EqualTo(10));
        Assert.That(result.Model.OneWeekInTheFutureCount, Is.EqualTo(1));
        Assert.That(result.Model.TodayCount, Is.GreaterThanOrEqualTo(2)); // Today and past
        Assert.That(result.Model.OneWeekCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Model.TwoWeeksCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Model.OneMonthCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Model.TwoMonthsCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Model.ThreeMonthsCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Model.SixMonthsCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Model.MoreThanSixMonthsCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Stats_WithEnvironmentInspectionTaggedCompliances_CountsCorrectly()
    {
        // Arrange
        var core = await GetCore();
        var localizationService = new BackendConfigurationLocalizationService();
        var userService = Substitute.For<IUserService>();

        // Create planning tag "Miljøtilsyn"
        var envTag = new PlanningTag
        {
            Name = "Miljøtilsyn",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(envTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Create planning with all required fields
        var planning = new Planning
        {
            WorkflowState = Constants.WorkflowStates.Created,
            Enabled = true,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
            RepeatEvery = 1,
            RepeatType = RepeatType.Day,
            RelatedEFormId = 0
        };
        await ItemsPlanningPnDbContext.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Associate planning with environment inspection tag
        var planningTag = new PlanningsTags
        {
            PlanningId = planning.Id,
            PlanningTagId = envTag.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningsTags.AddAsync(planningTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        
        // Create 3 compliances with environment tag - 2 today/past, 1 in future
        await CreateCompliance(now.AddDays(-2), planning.Id);
        await CreateCompliance(now.AddDays(-10), planning.Id); // Oldest
        await CreateCompliance(now.AddDays(5), planning.Id);

        // Create 2 compliances without environment tag
        await CreateCompliance(now.AddDays(-1));
        await CreateCompliance(now.AddDays(3));

        // Clear change tracker to ensure fresh data from database
        ItemsPlanningPnDbContext.ChangeTracker.Clear();
        BackendConfigurationPnDbContext!.ChangeTracker.Clear();

        var compliancesService = new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext,
            BackendConfigurationPnDbContext,
            userService,
            localizationService,
            new EFormCoreService(MicrotingDbContext!.Database.GetConnectionString()!),
            TimePlanningPnDbContext!
        );

        // Act
        var result = await compliancesService.Stats();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.Not.Null);
        Assert.That(result.Model.TotalCount, Is.EqualTo(5));
        Assert.That(result.Model.NumberOfPlannedEnvironmentInspectionTagTasks, Is.EqualTo(3));
        Assert.That(result.Model.TodayCountEnvironmentInspectionTag, Is.EqualTo(2));
        Assert.That(result.Model.DateOfOldestEnvironmentInspectionTagPlannedTask, Is.Not.Null);
        // The oldest should be 10 days ago
        var expectedOldest = now.AddDays(-10).Date;
        Assert.That(result.Model.DateOfOldestEnvironmentInspectionTagPlannedTask!.Value.Date, 
            Is.EqualTo(expectedOldest));
    }

    [Test]
    public async Task Stats_WithWorkorderCases_CountsCorrectly()
    {
        // Arrange
        var core = await GetCore();
        var localizationService = new BackendConfigurationLocalizationService();
        var userService = Substitute.For<IUserService>();

        // Create planning tag "Miljøtilsyn"
        var envTag = new PlanningTag
        {
            Name = "Miljøtilsyn",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(envTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        
        // Create workorder cases
        await CreateWorkorderCase(CaseStatusesEnum.Ongoing, true, now.AddDays(-5));
        await CreateWorkorderCase(CaseStatusesEnum.Awaiting, true, now.AddDays(-10)); // Oldest non-completed
        await CreateWorkorderCase(CaseStatusesEnum.Completed, true, now.AddDays(-3)); // Counted in total, excluded from oldest

        // Create workorder cases that shouldn't be counted at all:
        await CreateWorkorderCase(CaseStatusesEnum.NewTask, true, now.AddDays(-7)); // NewTask - excluded from both
        await CreateWorkorderCase(CaseStatusesEnum.Ongoing, false, now.AddDays(-8)); // Not leading case - excluded from both

        var compliancesService = new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext,
            BackendConfigurationPnDbContext!,
            userService,
            localizationService,
            new EFormCoreService(MicrotingDbContext!.Database.GetConnectionString()!),
            TimePlanningPnDbContext!
        );

        // Act
        var result = await compliancesService.Stats();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.Not.Null);
        // NumberOfAdHocTasks counts all except NewTask: Ongoing, Awaiting, Completed = 3
        Assert.That(result.Model.NumberOfAdHocTasks, Is.EqualTo(3));
        Assert.That(result.Model.DateOfOldestAdHocTask, Is.Not.Null);
        // DateOfOldestAdHocTask excludes Completed and NewTask, so oldest is the Awaiting from 10 days ago
        var expectedOldest = now.AddDays(-10).Date;
        Assert.That(result.Model.DateOfOldestAdHocTask!.Value.Date, Is.EqualTo(expectedOldest));
    }

    [Test]
    public async Task Stats_WithAreaRulePlannings_CountsCorrectly()
    {
        // Arrange
        var core = await GetCore();
        var localizationService = new BackendConfigurationLocalizationService();
        var userService = Substitute.For<IUserService>();

        // Create planning tag "Miljøtilsyn"
        var envTag = new PlanningTag
        {
            Name = "Miljøtilsyn",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(envTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Create area rule plannings with different boolean status values
        // The Stats method counts distinct Status values (true/false)
        await CreateAreaRulePlanning(true);  // Status = true
        await CreateAreaRulePlanning(true);  // Status = true (duplicate)
        await CreateAreaRulePlanning(false); // Status = false
        await CreateAreaRulePlanning(true);  // Status = true (duplicate)

        var compliancesService = new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext,
            BackendConfigurationPnDbContext!,
            userService,
            localizationService,
            new EFormCoreService(MicrotingDbContext!.Database.GetConnectionString()!),
            TimePlanningPnDbContext!
        );

        // Act
        var result = await compliancesService.Stats();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.Not.Null);
        // Should count 2 distinct status values: true and false
        Assert.That(result.Model.NumberOfPlannedTasks, Is.EqualTo(2));
    }

    [Test]
    public async Task Stats_WithCompletedEnvironmentInspectionPlannings_CountsLast30Days()
    {
        // Arrange
        var core = await GetCore();
        var localizationService = new BackendConfigurationLocalizationService();
        var userService = Substitute.For<IUserService>();

        // Create planning tag "Miljøtilsyn"
        var envTag = new PlanningTag
        {
            Name = "Miljøtilsyn",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(envTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Create planning with all required fields
        var planning = new Planning
        {
            WorkflowState = Constants.WorkflowStates.Created,
            Enabled = true,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
            RepeatEvery = 1,
            RepeatType = RepeatType.Day,
            RelatedEFormId = 0
        };
        await ItemsPlanningPnDbContext.Plannings.AddAsync(planning);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        // Associate planning with environment inspection tag
        var planningTag = new PlanningsTags
        {
            PlanningId = planning.Id,
            PlanningTagId = envTag.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext.PlanningsTags.AddAsync(planningTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        
        // Create completed planning cases - 2 within last 30 days, 1 older
        await CreatePlanningCase(planning.Id, 100, now.AddDays(-10));
        await CreatePlanningCase(planning.Id, 100, now.AddDays(-25));
        await CreatePlanningCase(planning.Id, 100, now.AddDays(-40)); // Too old

        // Create incomplete planning case (shouldn't count)
        await CreatePlanningCase(planning.Id, 50, now.AddDays(-5));

        var compliancesService = new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext,
            BackendConfigurationPnDbContext!,
            userService,
            localizationService,
            new EFormCoreService(MicrotingDbContext!.Database.GetConnectionString()!),
            TimePlanningPnDbContext!
        );

        // Act
        var result = await compliancesService.Stats();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.Not.Null);
        Assert.That(result.Model.NumberOfCompletedEnvironmentInspectionTagPlanningsLast30Days, Is.EqualTo(2));
    }

    [Test]
    public async Task Stats_WithRandomData_ReturnsConsistentResults()
    {
        // Arrange
        var core = await GetCore();
        var localizationService = new BackendConfigurationLocalizationService();
        var userService = Substitute.For<IUserService>();

        // Create planning tag "Miljøtilsyn"
        var envTag = new PlanningTag
        {
            Name = "Miljøtilsyn",
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await ItemsPlanningPnDbContext!.PlanningTags.AddAsync(envTag);
        await ItemsPlanningPnDbContext.SaveChangesAsync();

        var random = new Random(12345); // Fixed seed for reproducibility
        var now = DateTime.UtcNow;

        // Generate random compliances
        var complianceCount = random.Next(5, 15);
        for (int i = 0; i < complianceCount; i++)
        {
            var daysOffset = random.Next(-365, 30);
            await CreateCompliance(now.AddDays(daysOffset));
        }

        // Generate random workorder cases
        var workorderCount = random.Next(3, 8);
        for (int i = 0; i < workorderCount; i++)
        {
            var status = random.Next(0, 2) == 0 ? CaseStatusesEnum.Ongoing : CaseStatusesEnum.Awaiting;
            await CreateWorkorderCase(status, true, now.AddDays(-random.Next(1, 60)));
        }

        var compliancesService = new BackendConfigurationCompliancesService(
            ItemsPlanningPnDbContext,
            BackendConfigurationPnDbContext!,
            userService,
            localizationService,
            new EFormCoreService(MicrotingDbContext!.Database.GetConnectionString()!),
            TimePlanningPnDbContext!
        );

        // Act
        var result = await compliancesService.Stats();

        // Assert - Verify basic consistency
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Model, Is.Not.Null);
        
        // Total count should match what we created
        var actualComplianceCount = await BackendConfigurationPnDbContext!.Compliances.CountAsync();
        Assert.That(result.Model.TotalCount, Is.EqualTo(actualComplianceCount));
        
        // Ad hoc tasks should match active workorder cases
        var actualWorkorderCount = await BackendConfigurationPnDbContext.WorkorderCases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.CaseStatusesEnum != CaseStatusesEnum.NewTask)
            .Where(x => x.LeadingCase == true)
            .CountAsync();
        Assert.That(result.Model.NumberOfAdHocTasks, Is.EqualTo(actualWorkorderCount));
    }

    // Helper methods
    private async Task CreateCompliance(DateTime deadline, int? planningId = null)
    {
        var compliance = new Compliance
        {
            Deadline = deadline,
            PlanningId = planningId ?? 0,
            PropertyId = 1,
            StartDate = deadline.AddDays(-7),
            WorkflowState = Constants.WorkflowStates.Created,
            AreaId = 1
        };

        await BackendConfigurationPnDbContext!.Compliances.AddAsync(compliance);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task CreateWorkorderCase(CaseStatusesEnum status, bool leadingCase, DateTime createdAt)
    {
        // Create Property first (required by PropertyWorker)
        var property = new Property
        {
            Name = Guid.NewGuid().ToString(),
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
            ItemPlanningTagId = 0
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Create PropertyWorker (required by WorkorderCase)
        var propertyWorker = new PropertyWorker
        {
            PropertyId = property.Id,
            WorkerId = 1,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };
        await BackendConfigurationPnDbContext.PropertyWorkers.AddAsync(propertyWorker);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Create WorkorderCase
        var workorderCase = new WorkorderCase
        {
            PropertyWorkerId = propertyWorker.Id,
            CaseStatusesEnum = status,
            LeadingCase = leadingCase,
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedAt = createdAt,
            CaseId = 0,
            CreatedByUserId = 1,
            UpdatedByUserId = 1
        };

        await BackendConfigurationPnDbContext.WorkorderCases.AddAsync(workorderCase);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task CreateAreaRulePlanning(bool status)
    {
        // Create Property first (required by AreaRule)
        var property = new Property
        {
            Name = Guid.NewGuid().ToString(),
            WorkflowState = Constants.WorkflowStates.Created,
            CreatedByUserId = 1,
            UpdatedByUserId = 1,
            ItemPlanningTagId = 0
        };
        await BackendConfigurationPnDbContext!.Properties.AddAsync(property);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Create Area
        var area = new Area
        {
            WorkflowState = Constants.WorkflowStates.Created
        };
        await BackendConfigurationPnDbContext.Areas.AddAsync(area);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Create AreaRule that references both Property and Area
        var areaRule = new AreaRule
        {
            AreaId = area.Id,
            WorkflowState = Constants.WorkflowStates.Created,
            PropertyId = property.Id
        };
        await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
        await BackendConfigurationPnDbContext.SaveChangesAsync();

        // Create AreaRulePlanning that references the AreaRule
        var areaRulePlanning = new AreaRulePlanning
        {
            AreaId = area.Id,
            AreaRuleId = areaRule.Id,
            ItemPlanningId = 1,
            Status = status,
            WorkflowState = Constants.WorkflowStates.Created
        };

        await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(areaRulePlanning);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private async Task CreatePlanningCase(int planningId, int status, DateTime updatedAt)
    {
        var planningCase = new PlanningCase
        {
            PlanningId = planningId,
            Status = status,
            WorkflowState = Constants.WorkflowStates.Created,
            UpdatedAt = updatedAt
        };

        await ItemsPlanningPnDbContext!.PlanningCases.AddAsync(planningCase);
        await ItemsPlanningPnDbContext.SaveChangesAsync();
    }
}
