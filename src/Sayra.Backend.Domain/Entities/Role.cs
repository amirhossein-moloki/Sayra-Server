using System;

namespace Sayra.Backend.Domain.Entities
{
    public class Role : BaseEntity
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = "Active";
        public bool IsSystemRole { get; set; } = true;

        public void NormalizeAndValidate()
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                throw new Exceptions.InvalidDomainException("INVALID_ROLE_CODE", "Role Code is required.");
            }
            Code = Code.Trim();

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = Code;
            }
            Name = Name.Trim();

            Status = string.IsNullOrWhiteSpace(Status) ? "Active" : Status.Trim();
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        }

        public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

        public void Disable()
        {
            Status = "Disabled";
            UpdatedAt = DateTime.UtcNow;
        }

        public void Enable()
        {
            Status = "Active";
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
