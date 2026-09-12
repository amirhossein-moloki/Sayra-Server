namespace Sayra.Backend.Application.OfflineQueue
{
    public class OfflineOrderingOptions
    {
        public const string SectionName = "OfflineOrdering";

        public string GapPolicy { get; set; } = "WAIT"; // WAIT, ACCEPT_WITH_GAP
        public int MaxFutureClockSkewMinutes { get; set; } = 5;
        public int CriticalRetentionDays { get; set; } = 30;
        public int ImportantRetentionDays { get; set; } = 7;
        public int NormalRetentionDays { get; set; } = 1;
    }
}
