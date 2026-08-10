using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Workstations
{
    public class AuthorizeWorkstationCommand : ICommand<Workstation>
    {
        public string PcId { get; set; } = string.Empty;
    }
}
