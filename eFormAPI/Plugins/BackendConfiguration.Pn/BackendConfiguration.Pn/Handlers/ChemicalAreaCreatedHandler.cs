using System;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Messages;
using ChemicalsBase.Infrastructure.Data.Entities;
using eFormCore;
using Microsoft.EntityFrameworkCore;
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
        var entityGroup = await _sdkCore.EntityGroupRead(property.EntitySearchListChemicals.ToString()).ConfigureAwait(false);
        var entityGroupRegNo = await _sdkCore.EntityGroupRead(property.EntitySearchListChemicalRegNos.ToString()).ConfigureAwait(false);

        if (property.ChemicalLastUpdatedAt == null)
        {
            property.ChemicalLastUpdatedAt = DateTime.UtcNow;
            await property.Update(backendConfigurationPnDbContext).ConfigureAwait(false);
            var nextItemUid = entityGroup.EntityGroupItemLst.Count;
            var chemicals = await chemicalsDbContext.Chemicals.Include(x => x.Products).ToListAsync();
            // foreach (Chemical chemical in chemicals)
            // {
            //     foreach (Product product in chemical.Products)
            //     {
            //         if (product.Verified)
            //         {
            //             await core.EntitySearchItemCreate(entityGroup.Id, product.Barcode,
            //                 chemical.Name,
            //                 nextItemUid.ToString()).ConfigureAwait(false);
            //             nextItemUid++;
            //         }
            //     }
            //
            //     if (chemical.Verified)
            //     {
            //         await core.EntitySearchItemCreate(entityGroupRegNo.Id, chemical.RegistrationNo,
            //             chemical.Name,
            //             nextItemUid.ToString()).ConfigureAwait(false);
            //         nextItemUid++;
            //     }
            // }
            var options = new ParallelOptions()
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