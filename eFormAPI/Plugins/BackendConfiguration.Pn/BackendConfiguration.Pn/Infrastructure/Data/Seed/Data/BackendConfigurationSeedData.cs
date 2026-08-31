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

namespace BackendConfiguration.Pn.Infrastructure.Data.Seed.Data;

using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;

public class BackendConfigurationSeedData : IPluginConfigurationSeedData
{
    private const string TagBackendConfigurationSettingsName = "BackendConfigurationSettings";
    public PluginConfigurationValue[] Data =>
    [
        new PluginConfigurationValue
        {
            Name = $"{TagBackendConfigurationSettingsName}:ReportSubHeaderName",
            Value = ""
        },
        new PluginConfigurationValue
        {
            Name = $"{TagBackendConfigurationSettingsName}:ReportHeaderName",
            Value = ""
        },
        new PluginConfigurationValue
        {
            Name = $"{TagBackendConfigurationSettingsName}:MaxChrNumbers",
            Value = "1000"
        },
        new PluginConfigurationValue
        {
            Name = $"{TagBackendConfigurationSettingsName}:MaxCvrNumbers",
            Value = "1000"
        }
        // NOT seeded here, deliberately:
        // BackendConfigurationSettings:EformFirebaseServiceAccountJson, the
        // Firebase service-account key read by
        // Services/PushNotificationService.
        //
        // It follows its sibling secret,
        // BackendConfigurationSettings:AdhocFirebaseServiceAccountJson (read by
        // AdhocReminderJob in eform-service-backendconfiguration-plugin), which
        // is likewise absent here and written out of band by the fleet script.
        // A seeded empty row would buy nothing - the sender reads the key with
        // FirstOrDefault(...)?.Value and treats absent and empty identically -
        // while adding a real hazard: BackendConfigurationPluginSeed.SeedData
        // is a non-atomic Any()/Add()/SaveChanges() against an unconstrained
        // Name column, so several hosts booting at once on the first deploy
        // after this change can each insert the row. BasePn's
        // PluginConfigurationProvider.Load then ToDictionary()s by Name and
        // throws on the duplicate, and the plugin fails to load on every host
        // from then on. The keys above predate that risk; a new one need not
        // take it.
    ];
}