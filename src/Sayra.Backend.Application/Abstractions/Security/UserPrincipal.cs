using System;
using System.Collections.Generic;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Application.Abstractions.Security
{
    public class UserPrincipal
    {
        public Guid? UserId { get; set; }
        public string? Username { get; set; }
        public Guid? GamerId { get; set; }
        public string? GamerBusinessId { get; set; }
        public string? PcId { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? SiteId { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public List<string> Permissions { get; set; } = new List<string>();
        public UserAccountState AccountStatus { get; set; } = UserAccountState.Active;
        public bool IsAuthenticated { get; set; }

        public static UserPrincipal Anonymous => new UserPrincipal { IsAuthenticated = false };
    }
}
