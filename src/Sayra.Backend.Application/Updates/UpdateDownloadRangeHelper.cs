using System;

#nullable enable

namespace Sayra.Backend.Application.Updates
{
    public static class UpdateDownloadRangeHelper
    {
        private const string BytesUnitPrefix = "bytes=";

        /// <summary>
        /// Parses HTTP Range headers according to RFC 7233 byte-range specifications.
        /// Performance Optimization: Uses ReadOnlySpan&lt;char&gt; memory slicing and span-based long parsing
        /// to eliminate string allocations during range header parsing and chunk resume validation.
        /// </summary>
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

            // Zero-allocation ReadOnlySpan<char> slicing for header parsing
            ReadOnlySpan<char> headerSpan = rangeHeader.AsSpan().Trim();
            if (!headerSpan.StartsWith(BytesUnitPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Unrecognized range unit or malformed header -> fallback to full range
                return CreateFullRange(totalSize);
            }

            ReadOnlySpan<char> specifier = headerSpan.Slice(BytesUnitPrefix.Length).Trim();
            if (specifier.IsEmpty)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            // If header contains multiple comma-separated ranges, take the first range
            int commaIndex = specifier.IndexOf(',');
            if (commaIndex >= 0)
            {
                specifier = specifier.Slice(0, commaIndex).Trim();
            }

            int dashIndex = specifier.IndexOf('-');
            if (dashIndex < 0)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            ReadOnlySpan<char> startPart = specifier.Slice(0, dashIndex).Trim();
            ReadOnlySpan<char> endPart = specifier.Slice(dashIndex + 1).Trim();

            if (totalSize == 0)
            {
                return CreateUnsatisfiableRange(0);
            }

            // Case A: Suffix Range "bytes=-N" (last N bytes)
            if (startPart.IsEmpty)
            {
                if (!long.TryParse(endPart, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long suffixLength) || suffixLength <= 0)
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
            if (!long.TryParse(startPart, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsedStart) || parsedStart < 0)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            if (parsedStart >= totalSize)
            {
                return CreateUnsatisfiableRange(totalSize);
            }

            if (endPart.IsEmpty)
            {
                return CreateValidRange(parsedStart, totalSize - 1, totalSize, isRangeRequest: true);
            }

            // Case C: Explicit Range "bytes=N-M"
            if (!long.TryParse(endPart, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsedEnd) || parsedEnd < 0)
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
