namespace Sayra.Backend.Application.Security
{
    public static class PermissionCatalog
    {
        // Workstation & Device Permissions
        public const string ViewWorkstations = "ViewWorkstations";
        public const string ControlWorkstations = "ControlWorkstations";
        public const string LockWorkstation = "LockWorkstation";
        public const string UnlockWorkstation = "UnlockWorkstation";
        public const string ManageWorkstations = "ManageWorkstations";
        public const string ManageDevices = "ManageDevices";

        // Session Permissions
        public const string StartSession = "StartSession";
        public const string StopSession = "StopSession";
        public const string PauseSession = "PauseSession";
        public const string ResumeSession = "ResumeSession";
        public const string ExtendSession = "ExtendSession";
        public const string ViewSessions = "ViewSessions";

        // Reservation Permissions
        public const string CreateReservation = "CreateReservation";
        public const string ViewReservations = "ViewReservations";
        public const string ManageReservations = "ManageReservations";
        public const string CancelReservation = "CancelReservation";

        // Pricing Permissions
        public const string ViewPricing = "ViewPricing";
        public const string ManagePricing = "ManagePricing";

        // Financial Permissions
        public const string ViewFinancialData = "ViewFinancialData";
        public const string ManageFinancialData = "ManageFinancialData";
        public const string ProcessPayment = "ProcessPayment";
        public const string ViewLedger = "ViewLedger";

        // Software Update & Distribution Platform Permissions
        public const string ManageUpdates = "ManageUpdates";
        public const string ViewUpdates = "ViewUpdates";

        // Administration & Security Permissions
        public const string ManageUsers = "ManageUsers";
        public const string ManageRoles = "ManageRoles";
        public const string ManagePermissions = "ManagePermissions";
        public const string ViewAuditLogs = "ViewAuditLogs";
        public const string ViewSecurityEvents = "ViewSecurityEvents";
    }
}
