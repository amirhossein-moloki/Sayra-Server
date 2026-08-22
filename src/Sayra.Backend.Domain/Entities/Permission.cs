using System;

namespace Sayra.Backend.Domain.Entities
{
    public class Permission : BaseEntity
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = "General";

        public void NormalizeAndValidate()
        {
            if (string.IsNullOrWhiteSpace(Code))
            {
                throw new Exceptions.InvalidDomainException("INVALID_PERMISSION_CODE", "Permission Code is required.");
            }
            Code = Code.Trim();

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = Code;
            }
            Name = Name.Trim();

            Category = string.IsNullOrWhiteSpace(Category) ? "General" : Category.Trim();
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        }
    }
}
