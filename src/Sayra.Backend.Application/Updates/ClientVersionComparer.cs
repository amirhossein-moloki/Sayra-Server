using System;
using Sayra.Backend.Domain.Exceptions;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public static class ClientVersionComparer
    {
        public static bool TryParseVersion(string rawVersion, out Version parsedVersion, out string? prerelease)
        {
            parsedVersion = new Version(0, 0, 0, 0);
            prerelease = null;

            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return false;
            }

            ReadOnlySpan<char> span = rawVersion.AsSpan().Trim();

            if (span.IsEmpty)
            {
                return false;
            }

            // Strip optional 'v' or 'V' prefix
            if (span[0] == 'v' || span[0] == 'V')
            {
                span = span.Slice(1);
            }

            if (span.IsEmpty)
            {
                return false;
            }

            // Extract optional build metadata tag starting after '+'
            int plusIndex = span.IndexOf('+');
            if (plusIndex >= 0)
            {
                ReadOnlySpan<char> buildMeta = span.Slice(plusIndex + 1);
                if (buildMeta.IsEmpty || !IsValidTag(buildMeta))
                {
                    return false;
                }
                span = span.Slice(0, plusIndex);
            }

            // Extract optional prerelease tag starting after '-'
            int dashIndex = span.IndexOf('-');
            if (dashIndex >= 0)
            {
                ReadOnlySpan<char> pre = span.Slice(dashIndex + 1);
                if (pre.IsEmpty || !IsValidTag(pre))
                {
                    return false;
                }
                prerelease = pre.ToString();
                span = span.Slice(0, dashIndex);
            }

            // Parse dot-separated numeric version components: major.minor[.build[.revision]]
            int major = 0;
            int minor = 0;
            int build = 0;
            int revision = 0;

            int dotCount = 0;
            int currentStart = 0;

            for (int i = 0; i <= span.Length; i++)
            {
                if (i == span.Length || span[i] == '.')
                {
                    ReadOnlySpan<char> segment = span.Slice(currentStart, i - currentStart);
                    if (segment.IsEmpty)
                    {
                        return false;
                    }

                    if (!int.TryParse(segment, out int val) || val < 0)
                    {
                        return false;
                    }

                    switch (dotCount)
                    {
                        case 0:
                            major = val;
                            break;
                        case 1:
                            minor = val;
                            break;
                        case 2:
                            build = val;
                            break;
                        case 3:
                            revision = val;
                            break;
                        default:
                            return false; // Exceeds 4 numeric components
                    }

                    dotCount++;
                    currentStart = i + 1;
                }
            }

            if (dotCount < 2) // Minimum major.minor required
            {
                return false;
            }

            parsedVersion = new Version(major, minor, build, revision);
            return true;
        }

        private static bool IsValidTag(ReadOnlySpan<char> tag)
        {
            foreach (char c in tag)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-')
                {
                    return false;
                }
            }
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
