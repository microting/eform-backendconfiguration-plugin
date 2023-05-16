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
			IsFarm = false,
			LanguagesIds = new List<int>
			{
				1
			},
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);
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
			UserLastName = Guid.NewGuid().ToString()
		};
		await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
			TimePlanningPnDbContext);
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
			PlanningSites = sites.Select(x => new PlanningSite{ SiteId = x.Id, WorkflowState = Constants.WorkflowStates.Created }).ToList(),
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
			PropertyIds = new List<int>(),
			TagIds = new List<int>(),
			WorkerIds = new List<int>()
		};

		// Assert
		var result = await BackendConfigurationTaskTrackerHelper.Index(filters, BackendConfigurationPnDbContext, core, 1, ItemsPlanningPnDbContext);

		// Assert result
		Assert.NotNull(result);
		Assert.That(result.Success, Is.EqualTo(true));
		Assert.That(result.Model.Count, Is.EqualTo(1));
		Assert.That(result.Model[0].DeadlineTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.Deadline.ToString(CultureInfo.InvariantCulture)));
		Assert.That(
			result.Model[0].NextExecutionTime.ToString(CultureInfo.InvariantCulture),
			Is.EqualTo(planning.NextExecutionTime?.ToString(CultureInfo.InvariantCulture)
			));
		Assert.That(result.Model[0].Property, Is.EqualTo(property.Name));
		Assert.That(result.Model[0].RepeatEvery, Is.EqualTo(planning.RepeatEvery));
		Assert.That(result.Model[0].StartTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.StartDate.ToString(CultureInfo.InvariantCulture)));
		Assert.That(result.Model[0].Tags, Is.EqualTo(planning.PlanningsTags.Select(x => x.PlanningTag).Select(x => new CommonTagModel(){Name = x.Name, Id = x.Id}).ToList()));
		Assert.That(result.Model[0].TaskName, Is.Null);
		Assert.That(result.Model[0].Workers, Is.EqualTo(sites.Select(x => x.Name).ToList()));
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
			IsFarm = false,
			LanguagesIds = new List<int>
			{
				1
			},
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);
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
			UserLastName = Guid.NewGuid().ToString()
		};
		await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
			TimePlanningPnDbContext);
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
			PropertyIds = new List<int> (),
			TagIds = new List<int> (),
			WorkerIds = new List<int> { sites.Last().Id, }
		};

		// Assert
		var result = await BackendConfigurationTaskTrackerHelper.Index(filters, BackendConfigurationPnDbContext, core, 1, ItemsPlanningPnDbContext);

		// Assert result
		Assert.NotNull(result);
		Assert.That(result.Success, Is.EqualTo(true));
		Assert.That(result.Model.Count, Is.EqualTo(1));
		Assert.That(result.Model[0].DeadlineTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.Deadline.ToString(CultureInfo.InvariantCulture)));
		Assert.That(
			result.Model[0].NextExecutionTime.ToString(CultureInfo.InvariantCulture),
			Is.EqualTo(planning.NextExecutionTime?.ToString(CultureInfo.InvariantCulture)
			));
		Assert.That(result.Model[0].Property, Is.EqualTo(property.Name));
		Assert.That(result.Model[0].RepeatEvery, Is.EqualTo(planning.RepeatEvery));
		Assert.That(result.Model[0].StartTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.StartDate.ToString(CultureInfo.InvariantCulture)));
		Assert.That(result.Model[0].Tags, Is.EqualTo(planning.PlanningsTags.Select(x => x.PlanningTag).Select(x => new CommonTagModel() { Name = x.Name, Id = x.Id }).ToList()));
		Assert.That(result.Model[0].TaskName, Is.Null);
		Assert.That(result.Model[0].Workers, Is.EqualTo(sites.Where(x => x.Id == sites.Last().Id).Select(x => x.Name).ToList()));
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
			IsFarm = false,
			LanguagesIds = new List<int>
			{
				1
			},
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);
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
			UserLastName = Guid.NewGuid().ToString()
		};
		await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
			TimePlanningPnDbContext);
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
			PropertyIds = new List<int> { property.Id, },
			TagIds = new List<int>(),
			WorkerIds = new List<int>()
		};

		// Assert
		var result = await BackendConfigurationTaskTrackerHelper.Index(filters, BackendConfigurationPnDbContext, core, 1, ItemsPlanningPnDbContext);

		// Assert result
		Assert.NotNull(result);
		Assert.That(result.Success, Is.EqualTo(true));
		Assert.That(result.Model.Count, Is.EqualTo(1));
		Assert.That(result.Model[0].DeadlineTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.Deadline.ToString(CultureInfo.InvariantCulture)));
		Assert.That(
			result.Model[0].NextExecutionTime.ToString(CultureInfo.InvariantCulture),
			Is.EqualTo(planning.NextExecutionTime?.ToString(CultureInfo.InvariantCulture)
			));
		Assert.That(result.Model[0].Property, Is.EqualTo(property.Name));
		Assert.That(result.Model[0].RepeatEvery, Is.EqualTo(planning.RepeatEvery));
		Assert.That(result.Model[0].StartTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.StartDate.ToString(CultureInfo.InvariantCulture)));
		Assert.That(result.Model[0].Tags, Is.EqualTo(planning.PlanningsTags.Select(x => x.PlanningTag).Select(x => new CommonTagModel() { Name = x.Name, Id = x.Id }).ToList()));
		Assert.That(result.Model[0].TaskName, Is.Null);
		Assert.That(result.Model[0].Workers, Is.EqualTo(sites.Where(x => x.Id == sites.Last().Id).Select(x => x.Name).ToList()));
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
			IsFarm = false,
			LanguagesIds = new List<int>
			{
				1
			},
			MainMailAddress = Guid.NewGuid().ToString(),
			Name = Guid.NewGuid().ToString(),
			WorkorderEnable = true
		};
		await BackendConfigurationPropertiesServiceHelper.Create(propertyCreateModel, core, 1,
			BackendConfigurationPnDbContext, ItemsPlanningPnDbContext, 1, 1);
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
			UserLastName = Guid.NewGuid().ToString()
		};
		await BackendConfigurationAssignmentWorkerServiceHelper.CreateDeviceUser(deviceUserModel, core, 1,
			TimePlanningPnDbContext);
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
			PropertyIds = new List<int> { property.Id, },
			TagIds = new List<int> (),
			WorkerIds = new List<int> { sites.Last().Id, }
		};

		// Assert
		var result = await BackendConfigurationTaskTrackerHelper.Index(filters, BackendConfigurationPnDbContext, core, 1, ItemsPlanningPnDbContext);

		// Assert result
		Assert.NotNull(result);
		Assert.That(result.Success, Is.EqualTo(true));
		Assert.That(result.Model.Count, Is.EqualTo(1));
		Assert.That(result.Model[0].DeadlineTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.Deadline.ToString(CultureInfo.InvariantCulture)));
		Assert.That(
			result.Model[0].NextExecutionTime.ToString(CultureInfo.InvariantCulture),
			Is.EqualTo(planning.NextExecutionTime?.ToString(CultureInfo.InvariantCulture)
			));
		Assert.That(result.Model[0].Property, Is.EqualTo(property.Name));
		Assert.That(result.Model[0].RepeatEvery, Is.EqualTo(planning.RepeatEvery));
		Assert.That(result.Model[0].StartTask.ToString(CultureInfo.InvariantCulture), Is.EqualTo(compliance.StartDate.ToString(CultureInfo.InvariantCulture)));
		Assert.That(result.Model[0].Tags, Is.EqualTo(planning.PlanningsTags.Select(x => x.PlanningTag).Select(x => new CommonTagModel() { Name = x.Name, Id = x.Id }).ToList()));
		Assert.That(result.Model[0].TaskName, Is.Null);
		Assert.That(result.Model[0].Workers, Is.EqualTo(sites.Where(x => x.Id == sites.Last().Id).Select(x => x.Name).ToList()));
	}
}