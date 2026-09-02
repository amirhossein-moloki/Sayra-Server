using System;
using Sayra.Backend.Domain.Exceptions;

namespace Sayra.Backend.Domain
{
    public sealed class ConfigurationVersion : IComparable<ConfigurationVersion>, IEquatable<ConfigurationVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public ConfigurationVersion(int major, int minor = 0, int patch = 0)
        {
            if (major < 0 || minor < 0 || patch < 0)
            {
                throw new InvalidDomainException("INVALID_VERSION", "Configuration version components cannot be negative.");
            }

            if (major == 0 && minor == 0 && patch == 0)
            {
                throw new InvalidDomainException("INVALID_VERSION", "Configuration version must be greater than 0.0.0.");
            }

            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public static ConfigurationVersion Create(int major, int minor = 0, int patch = 0)
        {
            return new ConfigurationVersion(major, minor, patch);
        }

        public static ConfigurationVersion Parse(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new InvalidDomainException("INVALID_VERSION", "Configuration version string cannot be null or empty.");
            }

            var parts = input.Trim().Split('.');
            if (parts.Length < 1 || parts.Length > 3)
            {
                throw new InvalidDomainException("INVALID_VERSION", $"Invalid version format: '{input}'. Expected format 'Major[.Minor[.Patch]]'.");
            }

            if (!int.TryParse(parts[0], out var major))
            {
                throw new InvalidDomainException("INVALID_VERSION", $"Invalid major version component: '{parts[0]}'.");
            }

            var minor = 0;
            if (parts.Length > 1 && !int.TryParse(parts[1], out minor))
            {
                throw new InvalidDomainException("INVALID_VERSION", $"Invalid minor version component: '{parts[1]}'.");
            }

            var patch = 0;
            if (parts.Length > 2 && !int.TryParse(parts[2], out patch))
            {
                throw new InvalidDomainException("INVALID_VERSION", $"Invalid patch version component: '{parts[2]}'.");
            }

            return new ConfigurationVersion(major, minor, patch);
        }

        public static bool TryParse(string? input, out ConfigurationVersion? version)
        {
            version = null;
            try
            {
                version = Parse(input);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public int CompareTo(ConfigurationVersion? other)
        {
            if (other is null) return 1;

            if (Major != other.Major)
                return Major.CompareTo(other.Major);

            if (Minor != other.Minor)
                return Minor.CompareTo(other.Minor);

            return Patch.CompareTo(other.Patch);
        }

        public bool Equals(ConfigurationVersion? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as ConfigurationVersion);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Major, Minor, Patch);
        }

        public override string ToString()
        {
            return $"{Major}.{Minor}.{Patch}";
        }

        public static bool operator ==(ConfigurationVersion? left, ConfigurationVersion? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(ConfigurationVersion? left, ConfigurationVersion? right)
        {
            return !(left == right);
        }

        public static bool operator <(ConfigurationVersion? left, ConfigurationVersion? right)
        {
            if (left is null) return right is not null;
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(ConfigurationVersion? left, ConfigurationVersion? right)
        {
            if (left is null) return true;
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(ConfigurationVersion? left, ConfigurationVersion? right)
        {
            if (left is null) return false;
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(ConfigurationVersion? left, ConfigurationVersion? right)
        {
            if (left is null) return right is null;
            return left.CompareTo(right) >= 0;
        }
    }
}
