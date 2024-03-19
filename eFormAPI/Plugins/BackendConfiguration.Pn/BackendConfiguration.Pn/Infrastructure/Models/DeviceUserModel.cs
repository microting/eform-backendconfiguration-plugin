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

using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models;

public class DeviceUserModel
{
    public int SiteMicrotingUid { get; set; }
    public int SiteId { get; set; }
    public int? SiteUid { get; set; }
    public string SiteName { get; set; }
    public string PropertyNames { get; set; }
    public List<int> PropertyIds { get; set; }
    public string UserFirstName { get; set; }
    public string UserLastName { get; set; }
    public int LanguageId { get; set; }
    public string Language { get; set; }
    public string LanguageCode { get; set; }
    public bool? TimeRegistrationEnabled { get; set; }
    public bool? TaskManagementEnabled { get; set; }
    public int? CustomerNo { get; set; }
    public int? OtpCode { get; set; }
    public int? UnitId { get; set; }
    public int? WorkerUid { get; set; }
    public bool IsLocked { get; set; }
    public bool IsBackendUser { get; set; }
    public bool HasWorkOrdersAssigned { get; set; }

    public string Manufacturer { get; set; }

    public string Model { get; set; }
    public string Os { get; set; }
    public string OsVersion { get; set; }

    public string Version { get; set; }

    public static implicit operator DeviceUserModel(Microting.EformAngularFrontendBase.Infrastructure.Data.Models.DeviceUserModel model)
    {
        return new DeviceUserModel
        {
            SiteMicrotingUid = model.Id,
            LanguageId = model.Language,
            LanguageCode = model.LanguageCode,
            TimeRegistrationEnabled = null,
            UserFirstName = model.UserFirstName,
            UserLastName = model.UserLastName
        };
    }

    public static implicit operator Microting.EformAngularFrontendBase.Infrastructure.Data.Models.DeviceUserModel(DeviceUserModel model)
    {

        return new Microting.EformAngularFrontendBase.Infrastructure.Data.Models.DeviceUserModel
        {
            Id = model.SiteMicrotingUid,
            Language = model.LanguageId,
            LanguageCode = model.LanguageCode,
            UserFirstName = model.UserFirstName,
            UserLastName = model.UserLastName
        };
    }
}