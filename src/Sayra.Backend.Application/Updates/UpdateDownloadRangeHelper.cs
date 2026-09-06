using System;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public static class UpdateDownloadRangeHelper
    {
        private const string BytesUnitPrefix = "bytes=";

        public static UpdateDownloadRange ParseRangeHeader(string? rangeHeader, long totalSize)
        {
            if (totalSize < 0)
            {
                totalSize = 0;
            }

            // Case 1: No Range header provided or empty
            if (string.IsNullOrWhiteSpace(rangeHeader))
            {
                return CreateFullRange(totalSize);
            }

            var trimmedHeader = rangeHeader.Trim();
            if (!trimmedHeader.StartsWith(BytesUnitPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Unrecognized range unit or malformed header -> fallback to full range
                return CreateFullRange(totalSize);
            }

            var specifier = trimmedHeader.Substring(BytesUnitPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(specifier))
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            // If header contains multiple comma-separated ranges, take the first range
            int commaIndex = specifier.IndexOf(',');
            if (commaIndex >= 0)
            {
                specifier = specifier.Substring(0, commaIndex).Trim();
            }

            int dashIndex = specifier.IndexOf('-');
            if (dashIndex < 0)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            string startPart = specifier.Substring(0, dashIndex).Trim();
            string endPart = specifier.Substring(dashIndex + 1).Trim();

            if (totalSize == 0)
            {
                return CreateUnsatisfiableRange(0);
            }

            // Case A: Suffix Range "bytes=-N" (last N bytes)
            if (string.IsNullOrEmpty(startPart))
            {
                if (!long.TryParse(endPart, out long suffixLength) || suffixLength <= 0)
                {
                    return CreateUnsatisfiableRange(totalSize);
                }

                if (suffixLength >= totalSize)
                {
                    return CreateValidRange(0, totalSize - 1, totalSize, isRangeRequest: true);
                }

                long start = totalSize - suffixLength;
                return CreateValidRange(start, totalSize - 1, totalSize, isRangeRequest: true);
            }

            // Case B: Open-Ended Range "bytes=N-" (from N to EOF)
            if (!long.TryParse(startPart, out long parsedStart) || parsedStart < 0)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            if (parsedStart >= totalSize)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            if (string.IsNullOrEmpty(endPart))
            {
                return CreateValidRange(parsedStart, totalSize - 1, totalSize, isRangeRequest: true);
            }

            // Case C: Explicit Range "bytes=N-M"
            if (!long.TryParse(endPart, out long parsedEnd) || parsedEnd < 0)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            if (parsedStart > parsedEnd)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            long clampedEnd = Math.Min(parsedEnd, totalSize - 1);
            return CreateValidRange(parsedStart, clampedEnd, totalSize, isRangeRequest: true);
        }

        private static UpdateDownloadRange CreateFullRange(long totalSize)
        {
            if (totalSize == 0)
            {
                return new UpdateDownloadRange
                {
                    Start = 0,
                    End = 0,
                    TotalSize = 0,
                    IsRangeRequest = false,
                    IsSatisfiable = true,
                    ContentRangeHeader = "bytes 0-0/0"
                };
            }

            return new UpdateDownloadRange
            {
                Start = 0,
                End = totalSize - 1,
                TotalSize = totalSize,
                IsRangeRequest = false,
                IsSatisfiable = true,
                ContentRangeHeader = $"bytes 0-{totalSize - 1}/{totalSize}"
            };
        }

        private static UpdateDownloadRange CreateValidRange(long start, long end, long totalSize, bool isRangeRequest)
        {
            return new UpdateDownloadRange
            {
                Start = start,
                End = end,
                TotalSize = totalSize,
                IsRangeRequest = isRangeRequest,
                IsSatisfiable = true,
                ContentRangeHeader = $"bytes {start}-{end}/{totalSize}"
            };
        }

        private static UpdateDownloadRange CreateUnsatisfiableRange(long totalSize)
        {
            return new UpdateDownloadRange
            {
                Start = 0,
                End = 0,
                TotalSize = totalSize,
                IsRangeRequest = true,
                IsSatisfiable = false,
                ContentRangeHeader = $"bytes */{totalSize}"
            };
        }
    }
}
