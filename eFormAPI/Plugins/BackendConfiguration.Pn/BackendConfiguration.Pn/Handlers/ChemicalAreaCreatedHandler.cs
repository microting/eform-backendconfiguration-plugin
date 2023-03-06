using System;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Messages;
using ChemicalsBase.Infrastructure.Data.Entities;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Rebus.Handlers;

namespace BackendConfiguration.Pn.Handlers;

public class ChemicalAreaCreatedHandler : IHandleMessages<ChemicalAreaCreated>
{
    private readonly Core _sdkCore;
    private readonly BackendConfigurationDbContextHelper _backendConfigurationDbContextHelper;
    private readonly ChemicalDbContextHelper _chemicalDbContextHelper;

    public ChemicalAreaCreatedHandler(BackendConfigurationDbContextHelper backendConfigurationDbContextHelper, ChemicalDbContextHelper chemicalDbContextHelper, Core sdkCore)
    {
        _backendConfigurationDbContextHelper = backendConfigurationDbContextHelper;
        _chemicalDbContextHelper = chemicalDbContextHelper;
        _sdkCore = sdkCore;
    }

    public async Task Handle(ChemicalAreaCreated message)
    {
        await using var sdkDbContext = _sdkCore.DbContextHelper.GetDbContext();
        await using var backendConfigurationPnDbContext =
            _backendConfigurationDbContextHelper.GetDbContext();
        await using var chemicalsDbContext = _chemicalDbContextHelper.GetDbContext();
        var property = await backendConfigurationPnDbContext.Properties.SingleAsync(x => x.Id == message.PropertyId).ConfigureAwait(false);

        if (property.EntitySearchListChemicals == null && property.EntitySearchListChemicalRegNos == null)
        {
            var entityGroup = await sdkDbContext.EntityGroups.FirstOrDefaultAsync(x => x.Name == "Chemicals - Barcode").ConfigureAwait(false) ??
                              await _sdkCore.EntityGroupCreate(Constants.FieldTypes.EntitySearch, $"Chemicals - Barcode", "", true, false).ConfigureAwait(false);
            //var
            property.EntitySearchListChemicals = Convert.ToInt32(entityGroup.MicrotingUid);

            var entityGroupRegNo = await sdkDbContext.EntityGroups.FirstOrDefaultAsync(x => x.Name == "Chemicals - RegNo").ConfigureAwait(false) ??
                                   await _sdkCore.EntityGroupCreate(Constants.FieldTypes.EntitySearch, $"Chemicals - RegNo", "", true, false).ConfigureAwait(false);

            property.EntitySearchListChemicalRegNos = Convert.ToInt32(entityGroupRegNo.MicrotingUid);
            property.ChemicalLastUpdatedAt = DateTime.UtcNow;

            await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);

            if (sdkDbContext.EntityItems.Count(x => x.EntityGroupId == entityGroup.Id) == 0)
            {
                var nextItemUid = 0;
                var chemicals = await chemicalsDbContext.Chemicals.Include(x => x.Products).ToListAsync();
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 20
                };

                await Parallel.ForEachAsync(chemicals, options, async (chemical, token) =>
                {
                    foreach (Product product in chemical.Products)
                    {
                        if (product.Verified)
                        {
                            await _sdkCore.EntitySearchItemCreate(entityGroup.Id, product.Barcode,
                                chemical.Name,
                                nextItemUid.ToString()).ConfigureAwait(false);
                            nextItemUid++;
                        }
                    }

                    if (chemical.Verified)
                    {
                        await _sdkCore.EntitySearchItemCreate(entityGroupRegNo.Id, chemical.RegistrationNo,
                            chemical.Name,
                            nextItemUid.ToString()).ConfigureAwait(false);
                        nextItemUid++;
                    }
                }).ConfigureAwait(false);
            }
        }
    }
}