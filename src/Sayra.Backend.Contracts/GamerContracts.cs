using System;

namespace Sayra.Backend.Contracts
{
    public class CreateGamerRequestDto
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

    public class UpdateGamerProfileRequestDto
    {
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? BirthDate { get; set; }
    }

    public class ChangeGamerPasswordRequestDto
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class AuthenticateGamerRequestDto
    {
        public string UsernameOrEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class GamerResponseDto
    {
        public Guid Id { get; set; }
        public string GamerId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? OrganizationEntityId { get; set; }
        public Guid? SiteEntityId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class GamerAccountResponseDto
    {
        public Guid Id { get; set; }
        public Guid GamerEntityId { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public decimal BonusBalance { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AuthenticateGamerResponseDto
    {
        public bool IsSuccess { get; set; }
        public Guid? GamerId { get; set; }
        public string? GamerBusinessId { get; set; }
        public string? Username { get; set; }
        public string? AccountNumber { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
