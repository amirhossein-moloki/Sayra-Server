using System;
using System.Text.RegularExpressions;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public static class ClientVersionComparer
    {
        private static readonly Regex CleanVersionRegex = new(@"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:-([a-zA-Z0-9\.\-]+))?(?:\+([a-zA-Z0-9\.\-]+))?$", RegexOptions.Compiled);

        public static bool TryParseVersion(string rawVersion, out Version parsedVersion, out string? prerelease)
        {
            parsedVersion = new Version(0, 0, 0, 0);
            prerelease = null;

            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return false;
            }

            var trimmed = rawVersion.Trim();
            var match = CleanVersionRegex.Match(trimmed);
            if (!match.Success)
            {
                return false;
            }

            if (!int.TryParse(match.Groups[1].Value, out int major) ||
                !int.TryParse(match.Groups[2].Value, out int minor))
            {
                return false;
            }

            int build = 0;
            if (match.Groups[3].Success && !int.TryParse(match.Groups[3].Value, out build))
            {
                return false;
            }

            int revision = 0;
            if (match.Groups[4].Success && !int.TryParse(match.Groups[4].Value, out revision))
            {
                return false;
            }

            parsedVersion = new Version(major, minor, build, revision);
            prerelease = match.Groups[5].Success ? match.Groups[5].Value : null;

            return true;
        }

        public static Version ParseVersion(string rawVersion)
        {
            if (!TryParseVersion(rawVersion, out var version, out _))
            {
                throw new InvalidDomainException("INVALID_VERSION_FORMAT", $"Version string '{rawVersion}' is not in a recognized version format.");
            }

            return version;
        }

        public static int Compare(string rawVersionA, string rawVersionB)
        {
            if (!TryParseVersion(rawVersionA, out var versionA, out var prereleaseA))
            {
                throw new InvalidDomainException("INVALID_VERSION_FORMAT", $"Version A '{rawVersionA}' is invalid.");
            }

            if (!TryParseVersion(rawVersionB, out var versionB, out var prereleaseB))
            {
                throw new InvalidDomainException("INVALID_VERSION_FORMAT", $"Version B '{rawVersionB}' is invalid.");
            }

            int versionResult = versionA.CompareTo(versionB);
            if (versionResult != 0)
            {
                return versionResult;
            }

            // If versions are equal, compare prerelease tags according to SemVer rules
            // Release version (null prerelease) is greater than prerelease version
            if (prereleaseA == null && prereleaseB != null)
            {
                return 1;
            }
            if (prereleaseA != null && prereleaseB == null)
            {
                return -1;
            }
            if (prereleaseA != null && prereleaseB != null)
            {
                return string.Compare(prereleaseA, prereleaseB, StringComparison.OrdinalIgnoreCase);
            }

            return 0;
        }

        public static bool IsUpdateAvailable(string currentVersion, string targetVersion)
        {
            return Compare(targetVersion, currentVersion) > 0;
        }

        public static bool IsBelowMinimumVersion(string currentVersion, string? minimumSupportedVersion)
        {
            if (string.IsNullOrWhiteSpace(minimumSupportedVersion))
            {
                return false;
            }

            return Compare(currentVersion, minimumSupportedVersion) < 0;
        }

        public static bool IsDowngrade(string currentVersion, string targetVersion)
        {
            return Compare(targetVersion, currentVersion) < 0;
        }
    }
}
