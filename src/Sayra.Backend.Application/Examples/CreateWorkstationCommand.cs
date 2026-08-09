using System;
using Sayra.Backend.Application.Abstractions.Messaging;

namespace Sayra.Backend.Application.Examples
{
    public class CreateWorkstationCommand : ICommand<Guid>
    {
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
    }
}
