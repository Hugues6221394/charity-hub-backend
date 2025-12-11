using Microsoft.AspNetCore.Identity;
using StudentCharityHub.Models;
using static StudentCharityHub.PermissionCatalogLocal;

namespace StudentCharityHub.Data
{
    /// <summary>
    /// Seeds permission claims for existing roles/users to run PBAC in parallel with RBAC.
    /// </summary>
    public static class PermissionSeeder
    {
        private const string PermissionClaimType = "permission";

        public static IReadOnlyList<string> AllPermissions => new List<string>
        {
            PermissionsManagement.ManagePermissions,
            PermissionsManagement.ViewAuditLog,

            Users.View,
            Users.Manage,

            Students.View,
            Students.Manage,

            Donations.Create,
            Donations.View,
            Donations.Verify,

            Progress.View,
            Progress.Manage,

            Reports.View,
            Reports.Manage,

            Messages.View,
            Messages.Manage,

            Notifications.View,
            Notifications.Manage,
        };

        /// <summary>
        /// Map existing roles to permissions and apply as claims to users in those roles.
        /// Roles remain active; this only adds permission claims to keep behavior working.
        /// </summary>
        public static async Task SeedRolePermissionsAsync(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            // Map roles to permission sets
            var rolePermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Admin"] = AllPermissions.ToList(), // Admin gets everything
                ["Manager"] = new List<string>
                {
                    Users.View,
                    Students.View, Students.Manage,
                    Donations.View, Donations.Verify,
                    Progress.View,
                    Reports.View,
                    Messages.View, Messages.Manage,
                    Notifications.View, Notifications.Manage,
                },
                ["Donor"] = new List<string>
                {
                    Donations.Create,
                    Donations.View,
                    Progress.View,
                    Notifications.View,
                    Messages.View,
                },
                ["Student"] = new List<string>
                {
                    Students.View,
                    Progress.View, Progress.Manage,
                    Messages.View,
                    Notifications.View,
                },
            };

            foreach (var kvp in rolePermissions)
            {
                var roleName = kvp.Key;
                var permissions = kvp.Value;

                if (!await roleManager.RoleExistsAsync(roleName))
                    continue;

                var usersInRole = await userManager.GetUsersInRoleAsync(roleName);
                foreach (var user in usersInRole)
                {
                    foreach (var permission in permissions)
                    {
                        if (!(await userManager.GetClaimsAsync(user)).Any(c => c.Type == PermissionClaimType && c.Value == permission))
                        {
                            await userManager.AddClaimAsync(user, new System.Security.Claims.Claim(PermissionClaimType, permission));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Ensure the seeded admin user has every permission claim.
        /// </summary>
        public static async Task EnsureAdminHasAllPermissionsAsync(UserManager<ApplicationUser> userManager)
        {
            var adminEmail = "admin@studentcharityhub.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null) return;

            var existingClaims = await userManager.GetClaimsAsync(adminUser);
            foreach (var permission in AllPermissions)
            {
                if (!existingClaims.Any(c => c.Type == PermissionClaimType && c.Value == permission))
                {
                    await userManager.AddClaimAsync(adminUser, new System.Security.Claims.Claim(PermissionClaimType, permission));
                }
            }
        }
    }
}

