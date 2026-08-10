using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Workstations
{
    public class UnbindWorkstationConnectionCommand : ICommand<Workstation?>
    {
        public string PcId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
    }
}
