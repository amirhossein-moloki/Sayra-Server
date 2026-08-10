using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Workstations
{
    public class BindWorkstationConnectionCommand : ICommand<Workstation>
    {
        public string PcId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
    }
}
