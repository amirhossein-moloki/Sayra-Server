using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Workstations
{
    public class GetWorkstationByPcIdQuery : IQuery<Workstation?>
    {
        public string PcId { get; set; } = string.Empty;
    }
}
