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

using Microsoft.AspNetCore.Identity;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

using System.Globalization;
using Infrastructure.Helpers;
using Infrastructure.Models;
using Infrastructure.Models.Properties;
using Infrastructure.Models.TaskTracker;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Enums;
using PlanningSite = Microting.ItemsPlanningBase.Infrastructure.Data.Entities.PlanningSite;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class BackendConfigurationTaskTrackerServiceHelperTest : TestBaseSetup
{
	[Test]
	public async Task BackendConfigurationTaskTrackerServiceHelper_IndexTasks_WithoutFilters()
	{
		var core = await GetCore();
		// Arrange
		// Create property
		var propertyCreateModel = new PropertyCreateModel
		{
			Address = Guid.NewGuid().ToString(),
			Chr = Guid.NewGuid().ToString(),
			IndustryCode = Guid.NewGuid().ToString(),
			Cvr = Guid.NewGuid().ToString(),
			IsFarm = true,
			LanguagesIds = [1],
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
			// Property creation caps on a GLOBAL non-removed Property count
			// (BackendConfigurationPropertiesServiceHelper.cs:31-47). With the schema
			// replayed once per fixture, a (1,1) cap makes every arrange after the
			// first silently no-op and the follow-up FirstAsync throw.
			int.MaxValue, int.MaxValue);
		var property =
			await BackendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name);

		// create device user
		var deviceUserModel = new DeviceUserModel
		{
			CustomerNo = 0,
			HasWorkOrdersAssigned = false,
			IsBackendUser = false,
			IsLocked = false,
			LanguageCode = "da",
			TimeRegistrationEnabled = false,
			UserFirstName = Guid.NewGuid().ToString(),
			UserLastName = Guid.NewGuid().ToString(),
			WorkerEmail = Guid.NewGuid().ToString() + "@test.com"
		};

		// Act
		var userService = Substitute.For<IUserService>();
		userService.UserId.Returns(1);
		var userManager = Substitute.For<UserManager<EformUser>>(
			Substitute.For<IUserStore<EformUser>>(),
			null, null, null, null, null, null, null, null);
		await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
			TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);
		var sites = await MicrotingDbContext!.Sites.AsNoTracking().OrderBy(x => x.Name).ToListAsync();

		// create planning
		var timeNow = DateTime.Now;
		var planning = new Planning
		{
			WorkflowState = Constants.WorkflowStates.Created,
			StartDate = timeNow,
			Enabled = true,
			RepeatEvery = 1,
			RepeatType = RepeatType.Month,
			PlanningSites = sites.Select(x => new PlanningSite{ SiteId = x.Id, WorkflowState = Constants.WorkflowStates.Created }).ToList(),
			NextExecutionTime = timeNow.AddMonths(1),
			DayOfMonth = timeNow.Day,
			RepeatUntil = timeNow.AddMonths(6),
		};

		var securityGroup = new SecurityGroup()
		{
			Name = "Kun tid"
		};
		BaseDbContext.SecurityGroups.Add(securityGroup);
		var securityGroup2 = new SecurityGroup()
		{
			Name = "Kun arkiv"
		};
		BaseDbContext.SecurityGroups.Add(securityGroup2);
		var securityGroup3 = new SecurityGroup()
		{
			Name = "eForm users"
		};
		BaseDbContext.SecurityGroups.Add(securityGroup3);
		await BaseDbContext.SaveChangesAsync();

		await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
		await ItemsPlanningPnDbContext.SaveChangesAsync();

		//create area
		var area = new Area
		{
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.Areas.AddAsync(area);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearule
		var areaRule = new AreaRule
		{
			AreaId = area.Id,
			WorkflowState = Constants.WorkflowStates.Created,
			PropertyId = property.Id
		};

		await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearuleplanning
		var areaRulePlanning = new AreaRulePlanning
		{
			AreaRuleId = areaRule.Id,
			AreaId = area.Id,
			ItemPlanningId = planning.Id,
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(areaRulePlanning);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create compliance
		var compliance = new Compliance()
		{
			Deadline = (DateTime)planning.RepeatUntil,
			PlanningId = planning.Id,
			PropertyId = property.Id,
			StartDate = planning.StartDate,
			WorkflowState = Constants.WorkflowStates.Created,
		};

		await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		var filters = new TaskTrackerFiltrationModel
		{
			PropertyIds = [],
			TagIds = [],
			WorkerIds = []
		};

		// Assert
		var result = await BackendConfigurationTaskTrackerHelper.Index(filters, BackendConfigurationPnDbContext!, core, 1, ItemsPlanningPnDbContext!);

		// Assert result
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Success, Is.EqualTo(true));
		// The filters are deliberately empty, so Index() returns every property's
		// tasks. With the schema replayed once per fixture, sibling tests' rows are
		// present too - scope the assertions to this test's property.
		var mine = result.Model.Where(x => x.Property == property.Name).ToList();
		Assert.That(mine, Has.Count.EqualTo(1));
		Assert.That(mine[0].DeadlineTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.Deadline.AddDays(-1).ToString(CultureInfo.InvariantCulture)));
		Assert.That(
			mine[0].NextExecutionTime.ToString(CultureInfo.InvariantCulture),
			Is.EqualTo(planning.NextExecutionTime?.ToString(CultureInfo.InvariantCulture)
			));
		Assert.That(mine[0].Property, Is.EqualTo(property.Name));
		Assert.That(mine[0].RepeatEvery, Is.EqualTo(planning.RepeatEvery));
		Assert.That(mine[0].StartTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.StartDate.ToString(CultureInfo.InvariantCulture)));
		Assert.That(mine[0].Tags, Is.EqualTo(planning.PlanningsTags.Select(x => x.PlanningTag).Select(x => new CommonTagModel(){Name = x.Name, Id = x.Id}).ToList()));
		Assert.That(mine[0].TaskName, Is.Null);
		Assert.That(mine[0].WorkerNames, Is.EqualTo(sites.Select(x => x.Name).ToList()));
	}

	[Test]
	public async Task BackendConfigurationTaskTrackerServiceHelper_IndexTasks_WithWorkerInFilters()
	{
		var core = await GetCore();
		// Arrange
		// Create property
		var propertyCreateModel = new PropertyCreateModel
		{
			Address = Guid.NewGuid().ToString(),
			Chr = Guid.NewGuid().ToString(),
			IndustryCode = Guid.NewGuid().ToString(),
			Cvr = Guid.NewGuid().ToString(),
			IsFarm = true,
			LanguagesIds = [1],
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
			// Property creation caps on a GLOBAL non-removed Property count
			// (BackendConfigurationPropertiesServiceHelper.cs:31-47). With the schema
			// replayed once per fixture, a (1,1) cap makes every arrange after the
			// first silently no-op and the follow-up FirstAsync throw.
			int.MaxValue, int.MaxValue);
		var property =
			await BackendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name);

		// create device user
		var deviceUserModel = new DeviceUserModel
		{
			CustomerNo = 0,
			HasWorkOrdersAssigned = false,
			IsBackendUser = false,
			IsLocked = false,
			LanguageCode = "da",
			TimeRegistrationEnabled = false,
			UserFirstName = Guid.NewGuid().ToString(),
			UserLastName = Guid.NewGuid().ToString(),
			WorkerEmail = Guid.NewGuid().ToString() + "@test.com"
		};

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
		await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
			TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);
		var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

		// create planning
		var timeNow = DateTime.Now;
		var planning = new Planning
		{
			WorkflowState = Constants.WorkflowStates.Created,
			StartDate = timeNow,
			Enabled = true,
			RepeatEvery = 1,
			RepeatType = RepeatType.Month,
			PlanningSites = sites.Where(x => x.Id == sites.Last().Id).Select(x => new PlanningSite { SiteId = x.Id, WorkflowState = Constants.WorkflowStates.Created }).ToList(),
			NextExecutionTime = timeNow.AddMonths(1),
			DayOfMonth = timeNow.Day,
			RepeatUntil = timeNow.AddMonths(6),
		};

		await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
		await ItemsPlanningPnDbContext.SaveChangesAsync();

		//create area
		var area = new Area
		{
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.Areas.AddAsync(area);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearule
		var areaRule = new AreaRule
		{
			AreaId = area.Id,
			WorkflowState = Constants.WorkflowStates.Created,
			PropertyId = property.Id
		};

		await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearuleplanning
		var areaRulePlanning = new AreaRulePlanning
		{
			AreaRuleId = areaRule.Id,
			AreaId = area.Id,
			ItemPlanningId = planning.Id,
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(areaRulePlanning);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create compliance
		var compliance = new Compliance()
		{
			Deadline = (DateTime)planning.RepeatUntil,
			PlanningId = planning.Id,
			PropertyId = property.Id,
			StartDate = planning.StartDate,
			WorkflowState = Constants.WorkflowStates.Created,
		};

		await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		var filters = new TaskTrackerFiltrationModel
		{
			PropertyIds = [],
			TagIds = [],
			WorkerIds = [sites.Last().Id]
		};

		// Assert
		var result = await BackendConfigurationTaskTrackerHelper.Index(filters, BackendConfigurationPnDbContext!, core, 1, ItemsPlanningPnDbContext!);

		// Assert result
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Success, Is.EqualTo(true));
		Assert.That(result.Model.Count, Is.EqualTo(1));
		Assert.That(result.Model[0].DeadlineTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.Deadline.AddDays(-1).ToString(CultureInfo.InvariantCulture)));
		Assert.That(
			result.Model[0].NextExecutionTime.ToString(CultureInfo.InvariantCulture),
			Is.EqualTo(planning.NextExecutionTime?.ToString(CultureInfo.InvariantCulture)
			));
		Assert.That(result.Model[0].Property, Is.EqualTo(property.Name));
		Assert.That(result.Model[0].RepeatEvery, Is.EqualTo(planning.RepeatEvery));
		Assert.That(result.Model[0].StartTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.StartDate.ToString(CultureInfo.InvariantCulture)));
		Assert.That(result.Model[0].Tags, Is.EqualTo(planning.PlanningsTags.Select(x => x.PlanningTag).Select(x => new CommonTagModel() { Name = x.Name, Id = x.Id }).ToList()));
		Assert.That(result.Model[0].TaskName, Is.Null);
		Assert.That(result.Model[0].WorkerNames, Is.EqualTo(sites.Where(x => x.Id == sites.Last().Id).Select(x => x.Name).ToList()));
	}

	[Test]
	public async Task BackendConfigurationTaskTrackerServiceHelper_IndexTasks_WithPropertyInFilters()
	{
		var core = await GetCore();
		// Arrange
		// Create property
		var propertyCreateModel = new PropertyCreateModel
		{
			Address = Guid.NewGuid().ToString(),
			Chr = Guid.NewGuid().ToString(),
			IndustryCode = Guid.NewGuid().ToString(),
			Cvr = Guid.NewGuid().ToString(),
			IsFarm = true,
			LanguagesIds = [1],
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
			// Property creation caps on a GLOBAL non-removed Property count
			// (BackendConfigurationPropertiesServiceHelper.cs:31-47). With the schema
			// replayed once per fixture, a (1,1) cap makes every arrange after the
			// first silently no-op and the follow-up FirstAsync throw.
			int.MaxValue, int.MaxValue);
		var property =
			await BackendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name);

		// create device user
		var deviceUserModel = new DeviceUserModel
		{
			CustomerNo = 0,
			HasWorkOrdersAssigned = false,
			IsBackendUser = false,
			IsLocked = false,
			LanguageCode = "da",
			TimeRegistrationEnabled = false,
			UserFirstName = Guid.NewGuid().ToString(),
			UserLastName = Guid.NewGuid().ToString(),
			WorkerEmail = Guid.NewGuid().ToString() + "@test.com"
		};

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
		await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
			TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);
		var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

		// create planning
		var timeNow = DateTime.Now;
		var planning = new Planning
		{
			WorkflowState = Constants.WorkflowStates.Created,
			StartDate = timeNow,
			Enabled = true,
			RepeatEvery = 1,
			RepeatType = RepeatType.Month,
			PlanningSites = sites.Where(x => x.Id == sites.Last().Id).Select(x => new PlanningSite { SiteId = x.Id, WorkflowState = Constants.WorkflowStates.Created }).ToList(),
			NextExecutionTime = timeNow.AddMonths(1),
			DayOfMonth = timeNow.Day,
			RepeatUntil = timeNow.AddMonths(6),
		};

		await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
		await ItemsPlanningPnDbContext.SaveChangesAsync();

		//create area
		var area = new Area
		{
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.Areas.AddAsync(area);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearule
		var areaRule = new AreaRule
		{
			AreaId = area.Id,
			WorkflowState = Constants.WorkflowStates.Created,
			PropertyId = property.Id
		};

		await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearuleplanning
		var areaRulePlanning = new AreaRulePlanning
		{
			AreaRuleId = areaRule.Id,
			AreaId = area.Id,
			ItemPlanningId = planning.Id,
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(areaRulePlanning);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create compliance
		var compliance = new Compliance()
		{
			Deadline = (DateTime)planning.RepeatUntil,
			PlanningId = planning.Id,
			PropertyId = property.Id,
			StartDate = planning.StartDate,
			WorkflowState = Constants.WorkflowStates.Created,
		};

		await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		var filters = new TaskTrackerFiltrationModel
		{
			PropertyIds = [property.Id],
			TagIds = [],
			WorkerIds = []
		};

		// Assert
		var result = await BackendConfigurationTaskTrackerHelper.Index(filters, BackendConfigurationPnDbContext!, core, 1, ItemsPlanningPnDbContext!);

		// Assert result
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Success, Is.EqualTo(true));
		Assert.That(result.Model.Count, Is.EqualTo(1));
		Assert.That(result.Model[0].DeadlineTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.Deadline.AddDays(-1).ToString(CultureInfo.InvariantCulture)));
		Assert.That(
			result.Model[0].NextExecutionTime.ToString(CultureInfo.InvariantCulture),
			Is.EqualTo(planning.NextExecutionTime?.ToString(CultureInfo.InvariantCulture)
			));
		Assert.That(result.Model[0].Property, Is.EqualTo(property.Name));
		Assert.That(result.Model[0].RepeatEvery, Is.EqualTo(planning.RepeatEvery));
		Assert.That(result.Model[0].StartTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.StartDate.ToString(CultureInfo.InvariantCulture)));
		Assert.That(result.Model[0].Tags, Is.EqualTo(planning.PlanningsTags.Select(x => x.PlanningTag).Select(x => new CommonTagModel() { Name = x.Name, Id = x.Id }).ToList()));
		Assert.That(result.Model[0].TaskName, Is.Null);
		Assert.That(result.Model[0].WorkerNames, Is.EqualTo(sites.Where(x => x.Id == sites.Last().Id).Select(x => x.Name).ToList()));
	}

	[Test]
	public async Task BackendConfigurationTaskTrackerServiceHelper_IndexTasks_WithWorkerAndPropertyInFilters()
	{
		var core = await GetCore();
		// Arrange
		// Create property
		var propertyCreateModel = new PropertyCreateModel
		{
			Address = Guid.NewGuid().ToString(),
			Chr = Guid.NewGuid().ToString(),
			IndustryCode = Guid.NewGuid().ToString(),
			Cvr = Guid.NewGuid().ToString(),
			IsFarm = true,
			LanguagesIds = [1],
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
			// Property creation caps on a GLOBAL non-removed Property count
			// (BackendConfigurationPropertiesServiceHelper.cs:31-47). With the schema
			// replayed once per fixture, a (1,1) cap makes every arrange after the
			// first silently no-op and the follow-up FirstAsync throw.
			int.MaxValue, int.MaxValue);
		var property =
			await BackendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name);

		// create device user
		var deviceUserModel = new DeviceUserModel
		{
			CustomerNo = 0,
			HasWorkOrdersAssigned = false,
			IsBackendUser = false,
			IsLocked = false,
			LanguageCode = "da",
			TimeRegistrationEnabled = false,
			UserFirstName = Guid.NewGuid().ToString(),
			UserLastName = Guid.NewGuid().ToString(),
			WorkerEmail = Guid.NewGuid().ToString() + "@test.com"
		};

        // Act
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        var userManager = IdentityTestUtils.CreateRealUserManager(BaseDbContext!);
		await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
			TimePlanningPnDbContext!, BaseDbContext!,
        userService,
        userManager);
		var sites = await MicrotingDbContext!.Sites.AsNoTracking().ToListAsync();

		// create planning
		var timeNow = DateTime.Now;
		var planning = new Planning
		{
			WorkflowState = Constants.WorkflowStates.Created,
			StartDate = timeNow,
			Enabled = true,
			RepeatEvery = 1,
			RepeatType = RepeatType.Month,
			PlanningSites = sites.Where(x => x.Id == sites.Last().Id).Select(x => new PlanningSite { SiteId = x.Id, WorkflowState = Constants.WorkflowStates.Created }).ToList(),
			NextExecutionTime = timeNow.AddMonths(1),
			DayOfMonth = timeNow.Day,
			RepeatUntil = timeNow.AddMonths(6),
		};

		await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
		await ItemsPlanningPnDbContext.SaveChangesAsync();

		//create area
		var area = new Area
		{
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.Areas.AddAsync(area);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearule
		var areaRule = new AreaRule
		{
			AreaId = area.Id,
			WorkflowState = Constants.WorkflowStates.Created,
			PropertyId = property.Id
		};

		await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearuleplanning
		var areaRulePlanning = new AreaRulePlanning
		{
			AreaRuleId = areaRule.Id,
			AreaId = area.Id,
			ItemPlanningId = planning.Id,
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(areaRulePlanning);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create compliance
		var compliance = new Compliance()
		{
			Deadline = (DateTime)planning.RepeatUntil,
			PlanningId = planning.Id,
			PropertyId = property.Id,
			StartDate = planning.StartDate,
			WorkflowState = Constants.WorkflowStates.Created,
		};

		await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		var filters = new TaskTrackerFiltrationModel
		{
			PropertyIds = [property.Id],
			TagIds = [],
			WorkerIds = [sites.Last().Id]
		};

		// Assert
		var result = await BackendConfigurationTaskTrackerHelper.Index(filters, BackendConfigurationPnDbContext!, core, 1, ItemsPlanningPnDbContext!);

		// Assert result
		Assert.That(result, Is.Not.Null);
		Assert.That(result.Success, Is.EqualTo(true));
		Assert.That(result.Model.Count, Is.EqualTo(1));
		Assert.That(result.Model[0].DeadlineTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.Deadline.AddDays(-1).ToString(CultureInfo.InvariantCulture)));
		Assert.That(
			result.Model[0].NextExecutionTime.ToString(CultureInfo.InvariantCulture),
			Is.EqualTo(planning.NextExecutionTime?.ToString(CultureInfo.InvariantCulture)
			));
		Assert.That(result.Model[0].Property, Is.EqualTo(property.Name));
		Assert.That(result.Model[0].RepeatEvery, Is.EqualTo(planning.RepeatEvery));
		Assert.That(result.Model[0].StartTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.StartDate.ToString(CultureInfo.InvariantCulture)));
		Assert.That(result.Model[0].Tags, Is.EqualTo(planning.PlanningsTags.Select(x => x.PlanningTag).Select(x => new CommonTagModel() { Name = x.Name, Id = x.Id }).ToList()));
		Assert.That(result.Model[0].TaskName, Is.Null);
		Assert.That(result.Model[0].WorkerNames, Is.EqualTo(sites.Where(x => x.Id == sites.Last().Id).Select(x => x.Name).ToList()));
	}

	[Test]
	public async Task BackendConfigurationTaskTrackerServiceHelper_IndexTasks_WithTagInFilters_MatchesDisplayedTagsOnly()
	{
		var core = await GetCore();
		// Arrange
		// Create property
		var propertyCreateModel = new PropertyCreateModel
		{
			Address = Guid.NewGuid().ToString(),
			Chr = Guid.NewGuid().ToString(),
			IndustryCode = Guid.NewGuid().ToString(),
			Cvr = Guid.NewGuid().ToString(),
			IsFarm = true,
			LanguagesIds = [1],
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!,
			// Property creation caps on a GLOBAL non-removed Property count
			// (BackendConfigurationPropertiesServiceHelper.cs:31-47). With the schema
			// replayed once per fixture, a (1,1) cap makes every arrange after the
			// first silently no-op and the follow-up FirstAsync throw.
			int.MaxValue, int.MaxValue);
		var property =
			await BackendConfigurationPnDbContext!.Properties.FirstAsync(x => x.Name == propertyCreateModel.Name);

		var sites = await MicrotingDbContext!.Sites.AsNoTracking().OrderBy(x => x.Name).ToListAsync();

		// create planning
		var timeNow = DateTime.Now;
		var planning = new Planning
		{
			WorkflowState = Constants.WorkflowStates.Created,
			StartDate = timeNow,
			Enabled = true,
			RepeatEvery = 1,
			RepeatType = RepeatType.Month,
			PlanningSites = sites.Select(x => new PlanningSite { SiteId = x.Id, WorkflowState = Constants.WorkflowStates.Created }).ToList(),
			NextExecutionTime = timeNow.AddMonths(1),
			DayOfMonth = timeNow.Day,
			RepeatUntil = timeNow.AddMonths(6),
		};

		await ItemsPlanningPnDbContext!.Plannings.AddAsync(planning);
		await ItemsPlanningPnDbContext.SaveChangesAsync();

		// liveTag is assigned on both sides. staleTag was unassigned (soft-deleted) on the
		// items-planning side. sourceMismatchTag is live on the items-planning side but was never
		// mirrored onto the area rule planning - the case that a WorkflowState guard alone misses.
		var liveTag = new PlanningTag { Name = Guid.NewGuid().ToString(), WorkflowState = Constants.WorkflowStates.Created };
		var staleTag = new PlanningTag { Name = Guid.NewGuid().ToString(), WorkflowState = Constants.WorkflowStates.Created };
		var sourceMismatchTag = new PlanningTag { Name = Guid.NewGuid().ToString(), WorkflowState = Constants.WorkflowStates.Created };
		await ItemsPlanningPnDbContext.PlanningTags.AddRangeAsync(liveTag, staleTag, sourceMismatchTag);
		await ItemsPlanningPnDbContext.SaveChangesAsync();

		await ItemsPlanningPnDbContext.PlanningsTags.AddRangeAsync(
			new PlanningsTags
			{
				PlanningId = planning.Id, PlanningTagId = liveTag.Id,
				WorkflowState = Constants.WorkflowStates.Created
			},
			new PlanningsTags
			{
				PlanningId = planning.Id, PlanningTagId = staleTag.Id,
				WorkflowState = Constants.WorkflowStates.Removed
			},
			new PlanningsTags
			{
				PlanningId = planning.Id, PlanningTagId = sourceMismatchTag.Id,
				WorkflowState = Constants.WorkflowStates.Created
			});
		await ItemsPlanningPnDbContext.SaveChangesAsync();

		//create area
		var area = new Area
		{
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.Areas.AddAsync(area);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearule
		var areaRule = new AreaRule
		{
			AreaId = area.Id,
			WorkflowState = Constants.WorkflowStates.Created,
			PropertyId = property.Id
		};

		await BackendConfigurationPnDbContext.AreaRules.AddAsync(areaRule);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create arearuleplanning
		var areaRulePlanning = new AreaRulePlanning
		{
			AreaRuleId = areaRule.Id,
			AreaId = area.Id,
			ItemPlanningId = planning.Id,
			WorkflowState = Constants.WorkflowStates.Created
		};

		await BackendConfigurationPnDbContext.AreaRulePlannings.AddAsync(areaRulePlanning);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		// only the live tag is on the area rule planning - this is what the Tags column renders
		await BackendConfigurationPnDbContext.AreaRulePlanningTags.AddAsync(new AreaRulePlanningTag
		{
			AreaRulePlanningId = areaRulePlanning.Id,
			ItemPlanningTagId = liveTag.Id,
			WorkflowState = Constants.WorkflowStates.Created
		});
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		//create compliance
		var compliance = new Compliance
		{
			Deadline = (DateTime)planning.RepeatUntil,
			PlanningId = planning.Id,
			PropertyId = property.Id,
			StartDate = planning.StartDate,
			WorkflowState = Constants.WorkflowStates.Created,
		};

		await BackendConfigurationPnDbContext.Compliances.AddAsync(compliance);
		await BackendConfigurationPnDbContext.SaveChangesAsync();

		// Act + Assert - filtering on the live tag returns the task, and it renders that tag
		var liveTagResult = await BackendConfigurationTaskTrackerHelper.Index(
			new TaskTrackerFiltrationModel { PropertyIds = [], TagIds = [liveTag.Id], WorkerIds = [] },
			BackendConfigurationPnDbContext!, core, 1, ItemsPlanningPnDbContext!);

		Assert.That(liveTagResult, Is.Not.Null);
		Assert.That(liveTagResult.Success, Is.EqualTo(true));
		Assert.That(liveTagResult.Model.Count, Is.EqualTo(1));
		Assert.That(liveTagResult.Model[0].Tags.Select(x => x.Id), Does.Contain(liveTag.Id));
		Assert.That(liveTagResult.Model[0].Tags.Select(x => x.Id), Does.Not.Contain(staleTag.Id));
		Assert.That(liveTagResult.Model[0].Tags.Select(x => x.Id), Does.Not.Contain(sourceMismatchTag.Id));

		// Act + Assert - neither of the tags the Tags column does not render may match. Before the
		// fix the filter read Planning.PlanningsTags, a different table than the column, and one
		// that is not guarded against soft-deleted rows, so both of these returned this task.
		foreach (var unmatchableTagId in new[] { staleTag.Id, sourceMismatchTag.Id })
		{
			var unmatchedResult = await BackendConfigurationTaskTrackerHelper.Index(
				new TaskTrackerFiltrationModel { PropertyIds = [], TagIds = [unmatchableTagId], WorkerIds = [] },
				BackendConfigurationPnDbContext!, core, 1, ItemsPlanningPnDbContext!);

			Assert.That(unmatchedResult, Is.Not.Null);
			Assert.That(unmatchedResult.Success, Is.EqualTo(true));
			Assert.That(unmatchedResult.Model, Is.Empty, $"tag {unmatchableTagId} is not rendered in the Tags column, so it must not match");
		}
	}
}
