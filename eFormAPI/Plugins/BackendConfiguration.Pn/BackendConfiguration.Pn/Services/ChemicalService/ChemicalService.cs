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

using BackendConfiguration.Pn.Infrastructure.Models.Chemical;
using Chemicals.Pn.Infrastructure.Models.Chemical;
using ChemicalsBase.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.ChemicalService;

public class ChemicalService : IChemicalService
{
    private readonly ChemicalsDbContext _chemicalsDb;
    //private readonly IUserService _userService;
    //private readonly IEFormCoreService _coreService;
    private readonly BackendConfigurationPnDbContext _backendConfigurationPnDb;

    public ChemicalService(
        //ChemicalsDbContext dbContext,
        //IUserService userService,
        //IEFormCoreService coreService,
        ChemicalsDbContext chemicalsDb,
        BackendConfigurationPnDbContext backendConfigurationPnDb
    )
    {
        //_coreService = coreService;
        _chemicalsDb = chemicalsDb;
        _backendConfigurationPnDb = backendConfigurationPnDb;
        //_userService = userService;
    }

    public async Task<OperationDataResult<Paged<ChemicalPnModel>>> Index(ChemicalsRequestModel pnRequestModel)
    {
        try
        {
            var chemicalProductProperties = await _backendConfigurationPnDb.ChemicalProductProperties
                .Where(x => x.PropertyId == pnRequestModel.PropertyId)
                .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync().ConfigureAwait(false);
            var property =
                await _backendConfigurationPnDb.Properties.SingleAsync(x => x.Id == pnRequestModel.PropertyId);

            var theList = new List<ChemicalPnModel>();
            foreach (var chemicalProductProperty in chemicalProductProperties)
            {
                var chemical = await _chemicalsDb.Chemicals.Include(x => x.Products)
                    .SingleOrDefaultAsync(x => chemicalProductProperty.ChemicalId == x.Id);
                if (chemical != null)
                {
                    var chemicalPnModel = new ChemicalPnModel
                    {
                        AuthorisationDate = chemical.AuthorisationDate,
                        AuthorisationExpirationDate = chemical.AuthorisationExpirationDate,
                        AuthorisationTerminationDate = chemical.AuthorisationTerminationDate,
                        Barcode = chemical.Products.FirstOrDefault() == null
                            ? ""
                            : chemical.Products.First().Barcode,
                        FileName = chemical.Products.FirstOrDefault() == null
                            ? ""
                            : chemical.Products.First().FileName,
                        Id = chemical.Id,
                        Name = chemical.Name,
                        PossessionDeadline = chemical.PossessionDeadline,
                        RegistrationNo = chemical.RegistrationNo,
                        SalesDeadline = chemical.SalesDeadline,
                        Status = chemical.Status,
                        ProductName = chemical.Products.FirstOrDefault() == null
                            ? ""
                            : chemical.Products.First().Name,
                        ProductId = chemical.Products.FirstOrDefault() == null ? 0 : chemical.Products.First().Id,
                        Verified = chemical.Verified,
                        Locations = chemicalProductProperty.Locations.Replace("|", ", "),
                        PropertyName = property.Name,
                        ExpiredState = GetExpiredState(chemicalProductProperty.ExpireDate),
                        ExpiredDate = chemicalProductProperty.ExpireDate,
                        UseAndPossesionDeadline = chemical.UseAndPossesionDeadline
                    };
                    theList.Add(chemicalPnModel);
                }
            }

            theList = theList.OrderBy(x => x.ExpiredDate).ToList();

            var chemicalsModel = new Paged<ChemicalPnModel>
            {
                Total = theList.Count,
                Entities = theList
            };

            return new OperationDataResult<Paged<ChemicalPnModel>>(true, chemicalsModel);
        }
        catch (Exception e)
        {
            Trace.TraceError(e.Message);
            return new OperationDataResult<Paged<ChemicalPnModel>>(false,
                "ErrorObtainingLists");
        }
    }

    private string GetExpiredState(DateTime? expireDateTime)
    {
        if (expireDateTime <= DateTime.UtcNow)
        {
            return "Udløber i dag eller er udløbet";
        }

        if (expireDateTime <= DateTime.UtcNow.AddMonths(1))
        {
            return "Udløber om senest 1 mdr.";
        }

        if (expireDateTime <= DateTime.UtcNow.AddMonths(3))
        {
            return "Udløber om senest 3 mdr.";
        }

        if (expireDateTime <= DateTime.UtcNow.AddMonths(6))
        {
            return "Udløber om senest 6 mdr.";
        }

        if (expireDateTime <= DateTime.UtcNow.AddMonths(12))
        {
            return "Udløber om senest 12 mdr.";
        }

        return "Udløber om mere end 12 mdr.";
    }
}