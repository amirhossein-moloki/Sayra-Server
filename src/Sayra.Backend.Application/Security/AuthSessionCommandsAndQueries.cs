using System;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;
using Sayra.Backend.Domain.Entities;

namespace Sayra.Backend.Application.Security
{
    public class LogoutCommand : ICommand<LogoutResponseDto>
    {
        public string? SessionToken { get; set; }
        public Guid? UserId { get; set; }
        public Guid? GamerId { get; set; }
        public string Reason { get; set; } = "USER_LOGOUT";
    }

    public class GetCurrentAuthenticationSessionQuery : IQuery<AuthenticationSession>
    {
        public string SessionToken { get; set; } = string.Empty;
    }
}
