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

using System;
using System.Text.RegularExpressions;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microting.eForm.Dto;

namespace BackendConfiguration.Pn.Services.RebusService;

using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using eFormCore;
using Installers;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Factories;
using Rebus.Bus;

public class RebusService : IRebusService
{
    private IBus _bus;
    private IWindsorContainer _container;
    private string _connectionString;
    private readonly IEFormCoreService _coreHelper;
    private BackendConfigurationDbContextHelper _backendConfigurationDbContextHelper;
    private ChemicalDbContextHelper _chemicalDbContextHelper;
    private DocumentDbContextHelper _documentDbContextHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;

    public RebusService(IEFormCoreService coreHelper, IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
    {
        _coreHelper = coreHelper;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
        _container = new WindsorContainer();
    }

    public async Task Start(string connectionString)
    {
        Core core = await _coreHelper.GetCore();
        _connectionString = connectionString;
        var dbPrefix = Regex.Match(_connectionString, @"Database=(\d*)_").Groups[1].Value;
        var rabbitmqHost = core.GetSdkSetting(Settings.rabbitMqHost).GetAwaiter().GetResult();
        Console.WriteLine($"rabbitmqHost: {rabbitmqHost}");
        var rabbitMqUser = core.GetSdkSetting(Settings.rabbitMqUser).GetAwaiter().GetResult();
        Console.WriteLine($"rabbitMqUser: {rabbitMqUser}");
        var rabbitMqPassword = core.GetSdkSetting(Settings.rabbitMqPassword).GetAwaiter().GetResult();
        Console.WriteLine($"rabbitMqPassword: {rabbitMqPassword}");

        _backendConfigurationDbContextHelper = new BackendConfigurationDbContextHelper(connectionString);
        var chemicalBaseConnectionString = connectionString.Replace(
            "eform-backend-configuration-plugin",
            "chemical-base-plugin");
        var documentBaseConnectionString = connectionString.Replace(
            "eform-backend-configuration-plugin",
            "eform-angular-case-template-plugin");
        _chemicalDbContextHelper = new ChemicalDbContextHelper(chemicalBaseConnectionString);
        _documentDbContextHelper = new DocumentDbContextHelper(documentBaseConnectionString);
        _container.Register(Component.For<Core>().Instance(core));
        _container.Register(Component.For<BackendConfigurationDbContextHelper>().Instance(_backendConfigurationDbContextHelper));
        _container.Register(Component.For<ChemicalDbContextHelper>().Instance(_chemicalDbContextHelper));
        _container.Register(Component.For<DocumentDbContextHelper>().Instance(_documentDbContextHelper));
        _container.Register(Component.For<IBackendConfigurationLocalizationService>().Instance(_backendConfigurationLocalizationService));
        _container.Install(
            new RebusHandlerInstaller()
            , new RebusInstaller(dbPrefix, connectionString, 1, 1, rabbitMqUser, rabbitMqPassword, rabbitmqHost)
        );

        _bus = _container.Resolve<IBus>();
    }

    public IBus GetBus()
    {
        return _bus;
    }
    private BackendConfigurationPnDbContext GetContext()
    {
        var contextFactory = new BackendConfigurationPnContextFactory();
        return contextFactory.CreateDbContext([_connectionString]);
    }
    public WindsorContainer GetContainer()
    {
        return (WindsorContainer)_container;
    }
}