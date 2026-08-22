using System;

namespace Sayra.Backend.Domain.Entities
{
    public class UserRoleEntity : BaseEntity
    {
        public Guid UserEntityId { get; set; }
        public Guid RoleId { get; set; }

        public User? User { get; set; }
        public Role? Role { get; set; }
    }
}
