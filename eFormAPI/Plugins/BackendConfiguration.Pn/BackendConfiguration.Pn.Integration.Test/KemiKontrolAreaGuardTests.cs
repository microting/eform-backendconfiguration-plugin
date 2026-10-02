/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

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

using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Infrastructure.Models.AreaRules;
using BackendConfiguration.Pn.Infrastructure.Models.PropertyAreas;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertyAreasService;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>
/// The legacy chemical flow was removed (#1362), so the Type9 area
/// "25. KemiKontrol" must not be offered, assigned, given rules or planned.
/// Assignments that already exist are left for LegacyChemicalCleanupService.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class KemiKontrolAreaGuardTests : TestBaseSetup
{
    private const string Refused = "LegacyChemicalAreaRemoved";

    private async Task<(Property Property, Area Area)> SeedPropertyAndKemiKontrolAreaAsync()
    {
        // Area.IsFarm defaults to true and Read only offers areas matching the property's IsFarm.
        var property = new Property { Name = Guid.NewGuid().ToString(), IsFarm = true, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await property.Create(BackendConfigurationPnDbContext!);
        var area = new Area { Type = AreaTypesEnum.Type9, IsDisabled = true, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await area.Create(BackendConfigurationPnDbContext!);
        return (property, area);
    }

    private async Task<AreaProperty> AssignAsync(Property property, Area area)
    {
        var assignment = new AreaProperty
        {
            AreaId = area.Id, PropertyId = property.Id, Checked = true, CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await assignment.Create(BackendConfigurationPnDbContext!);
        return assignment;
    }

    [Test]
    public async Task Read_DoesNotOfferKemiKontrol_NorListAnExistingAssignment()
    {
        var core = await GetCore();
        var (property, unassignedArea) = await SeedPropertyAndKemiKontrolAreaAsync();
        var assignedArea = new Area { Type = AreaTypesEnum.Type9, IsDisabled = true, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await assignedArea.Create(BackendConfigurationPnDbContext!);
        await AssignAsync(property, assignedArea);
        var otherArea = new Area { Type = AreaTypesEnum.Type1, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await otherArea.Create(BackendConfigurationPnDbContext!);

        var language = await MicrotingDbContext!.Languages.FirstAsync();
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserLanguage().Returns(Task.FromResult(language));
        var coreHelper = Substitute.For<IEFormCoreService>();
        coreHelper.GetCore().Returns(Task.FromResult(core));
        var sut = new BackendConfigurationPropertyAreasService(coreHelper, userService,
            BackendConfigurationPnDbContext!, Substitute.For<IBackendConfigurationLocalizationService>(),
            ItemsPlanningPnDbContext!);

        var result = await sut.Read(property.Id);

        Assert.That(result.Success, Is.True, result.Message);
        var areaIds = result.Model.Select(x => x.AreaId).ToList();
        Assert.That(areaIds, Does.Contain(otherArea.Id));
        Assert.That(areaIds, Does.Not.Contain(unassignedArea.Id), "offered");
        Assert.That(areaIds, Does.Not.Contain(assignedArea.Id), "listed as assigned");
    }

    [Test]
    public async Task Update_RefusesToAssignKemiKontrol_AndCreatesNothing()
    {
        var core = await GetCore();
        var (property, area) = await SeedPropertyAndKemiKontrolAreaAsync();

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(new PropertyAreasUpdateModel
        {
            PropertyId = property.Id,
            Areas = [new PropertyAreaModel { AreaId = area.Id, Activated = true }]
        }, core, BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(Refused));
        Assert.That(await BackendConfigurationPnDbContext!.AreaProperties
            .AnyAsync(x => x.PropertyId == property.Id && x.AreaId == area.Id), Is.False);
    }

    [Test]
    public async Task Update_LeavesAnExistingKemiKontrolAssignmentForTheLegacyCleanup()
    {
        // Read no longer lists the assignment, so the client's next save omits it.
        // That must not delete it here: LegacyChemicalCleanupService owns its removal.
        var core = await GetCore();
        var (property, area) = await SeedPropertyAndKemiKontrolAreaAsync();
        var assignment = await AssignAsync(property, area);

        var result = await BackendConfigurationPropertyAreasServiceHelper.Update(new PropertyAreasUpdateModel
        {
            PropertyId = property.Id,
            Areas = []
        }, core, BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, 1);

        Assert.That(result.Success, Is.True, result.Message);
        var stored = await BackendConfigurationPnDbContext!.AreaProperties.AsNoTracking()
            .SingleAsync(x => x.Id == assignment.Id);
        Assert.That(stored.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Created));
    }

    [Test]
    public async Task CreateAreaRule_OnAKemiKontrolAssignment_IsRefused()
    {
        var core = await GetCore();
        var (property, area) = await SeedPropertyAndKemiKontrolAreaAsync();
        var assignment = await AssignAsync(property, area);
        var language = await MicrotingDbContext!.Languages.FirstAsync();

        var result = await BackendConfigurationAreaRulesServiceHelper.Create(new AreaRulesCreateModel
        {
            PropertyAreaId = assignment.Id,
            AreaRules =
            [
                new AreaRuleCreateModel
                {
                    TranslatedNames = [new CommonDictionaryModel { Id = language.Id, Name = "Kemi" }],
                    TypeSpecificFields = new TypeSpecificFields()
                }
            ]
        }, core, 1, BackendConfigurationPnDbContext!, language);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(Refused));
        Assert.That(await BackendConfigurationPnDbContext!.AreaRules
            .AnyAsync(x => x.PropertyId == property.Id && x.AreaId == area.Id), Is.False);
    }

    [Test]
    public async Task UpdatePlanning_ForAKemiKontrolRule_IsRefused()
    {
        var core = await GetCore();
        var (property, area) = await SeedPropertyAndKemiKontrolAreaAsync();
        await AssignAsync(property, area);
        var rule = new AreaRule
        {
            AreaId = area.Id, PropertyId = property.Id, EformId = 7, CreatedInGuide = true,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        await rule.Create(BackendConfigurationPnDbContext!);

        var result = await BackendConfigurationAreaRulePlanningsServiceHelper.UpdatePlanning(new AreaRulePlanningModel
        {
            RuleId = rule.Id,
            PropertyId = property.Id,
            Status = true,
            StartDate = DateTime.UtcNow.Date
        }, core, 1, BackendConfigurationPnDbContext!, ItemsPlanningPnDbContext!, null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo(Refused));
        Assert.That(await BackendConfigurationPnDbContext!.AreaRulePlannings
            .AnyAsync(x => x.AreaRuleId == rule.Id), Is.False);
    }
}
