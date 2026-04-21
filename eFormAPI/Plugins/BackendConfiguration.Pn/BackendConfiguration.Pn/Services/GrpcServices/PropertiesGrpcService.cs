using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc;
using BackendConfiguration.Pn.Services.BackendConfigurationPropertiesService;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;

namespace BackendConfiguration.Pn.Services.GrpcServices;

public class PropertiesGrpcService(
    IBackendConfigurationPropertiesService propertiesService,
    IBackendConfigurationUserPropertyAccess userPropertyAccess,
    IGrpcSiteResolver siteResolver)
    : BackendConfigurationPropertiesGrpc.BackendConfigurationPropertiesGrpcBase
{
    public override async Task<GetCommonDictionaryResponse> GetCommonDictionary(
        GetCommonDictionaryRequest request,
        ServerCallContext context)
    {
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();
        var accessibleIds = (await userPropertyAccess
            .GetAccessiblePropertyIdsAsync(sdkSiteId)).ToHashSet();

        var result = await propertiesService.GetCommonDictionary(request.FullNames);

        var response = new GetCommonDictionaryResponse
        {
            Success = result.Success,
            Message = result.Message ?? string.Empty
        };

        if (!result.Success || result.Model == null)
        {
            return response;
        }

        foreach (var item in result.Model)
        {
            if (item.Id is null || !accessibleIds.Contains(item.Id.Value))
            {
                continue;
            }

            response.Items.Add(new CommonDictionaryItem
            {
                Id = item.Id.Value,
                Name = item.Name ?? string.Empty,
                Description = item.Description ?? string.Empty
            });
        }

        return response;
    }
}
