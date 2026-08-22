using System;

namespace Sayra.Backend.Domain.Entities
{
    public class RolePermission : BaseEntity
    {
        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }

        public Role? Role { get; set; }
        public Permission? Permission { get; set; }
    }
}
