using System.Threading.Tasks;
using BackendConfiguration.Pn.Messages;
using Rebus.Handlers;

namespace BackendConfiguration.Pn.Handlers;

public class WorkOrderUpdatedHandler : IHandleMessages<WorkOrderUpdated>
{
    public Task Handle(WorkOrderUpdated message)
    {
        throw new System.NotImplementedException();
    }
}