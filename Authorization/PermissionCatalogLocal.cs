namespace StudentCharityHub
{
    /// <summary>
    /// Local permission catalog used for PBAC policies and seeding.
    /// </summary>
    public static class PermissionCatalogLocal
    {
        public static class PermissionsManagement
        {
            public const string ManagePermissions = "permissions.manage";
            public const string ViewAuditLog = "permissions.audit.view";
        }

        public static class Users
        {
            public const string View = "users.view";
            public const string Manage = "users.manage";
        }

        public static class Students
        {
            public const string View = "students.view";
            public const string Manage = "students.manage";
        }

        public static class Donations
        {
            public const string Create = "donations.create";
            public const string View = "donations.view";
            public const string Verify = "donations.verify";
        }

        public static class Progress
        {
            public const string View = "progress.view";
            public const string Manage = "progress.manage";
        }

        public static class Reports
        {
            public const string View = "reports.view";
            public const string Manage = "reports.manage";
        }

        public static class Messages
        {
            public const string View = "messages.view";
            public const string Manage = "messages.manage";
        }

        public static class Notifications
        {
            public const string View = "notifications.view";
            public const string Manage = "notifications.manage";
        }

        public static readonly List<string> AllPermissions = new()
        {
            PermissionsManagement.ManagePermissions,
            PermissionsManagement.ViewAuditLog,
            Users.View, Users.Manage,
            Students.View, Students.Manage,
            Donations.Create, Donations.View, Donations.Verify,
            Progress.View, Progress.Manage,
            Reports.View, Reports.Manage,
            Messages.View, Messages.Manage,
            Notifications.View, Notifications.Manage
        };
    }
}

