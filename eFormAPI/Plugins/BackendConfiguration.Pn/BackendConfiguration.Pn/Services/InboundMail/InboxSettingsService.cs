#nullable enable
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface IInboxSettingsService
{
    Task<OperationDataResult<InboxSettingsModel>> GetAsync(int userId);
    Task<OperationResult> UpdateAsync(InboxSettingsModel model, int userId);
    Task<OperationDataResult<InboxSettingsModel>> RotateAddressAsync(int userId);
    Task<OperationResult> ApproveSenderAsync(int inboxDocumentId, int userId);
    Task<OperationResult> RejectSenderAsync(int inboxDocumentId, bool block, int userId);
}

/// <summary>Task 4 stub so the controller compiles; Task 5 replaces the body.</summary>
public class InboxSettingsService : IInboxSettingsService
{
    private const string NotImplemented = "not implemented";

    public Task<OperationDataResult<InboxSettingsModel>> GetAsync(int userId) =>
        Task.FromResult(new OperationDataResult<InboxSettingsModel>(false, NotImplemented));

    public Task<OperationResult> UpdateAsync(InboxSettingsModel model, int userId) =>
        Task.FromResult(new OperationResult(false, NotImplemented));

    public Task<OperationDataResult<InboxSettingsModel>> RotateAddressAsync(int userId) =>
        Task.FromResult(new OperationDataResult<InboxSettingsModel>(false, NotImplemented));

    public Task<OperationResult> ApproveSenderAsync(int inboxDocumentId, int userId) =>
        Task.FromResult(new OperationResult(false, NotImplemented));

    public Task<OperationResult> RejectSenderAsync(int inboxDocumentId, bool block, int userId) =>
        Task.FromResult(new OperationResult(false, NotImplemented));
}
