using System;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Gamers
{
    public class CreateGamerCommand : ICommand<Gamer>
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? BirthDate { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
    }

    public class UpdateGamerProfileCommand : ICommand<Gamer>
    {
        public Guid GamerEntityId { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? BirthDate { get; set; }
    }

    public class DeactivateGamerCommand : ICommand<Gamer>
    {
        public Guid GamerEntityId { get; set; }
    }

    public class ChangeGamerPasswordCommand : ICommand<bool>
    {
        public Guid GamerEntityId { get; set; }
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class AuthenticateGamerCommand : ICommand<AuthenticateGamerResponseDto>
    {
        public string UsernameOrEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class GetGamerQuery : IQuery<Gamer>
    {
        public Guid GamerEntityId { get; set; }
    }

    public class GetGamerAccountQuery : IQuery<GamerAccount>
    {
        public Guid GamerEntityId { get; set; }
    }
}
