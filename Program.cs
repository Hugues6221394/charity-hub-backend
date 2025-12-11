using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using StudentCharityHub;
using static StudentCharityHub.PermissionCatalogLocal;

using StudentCharityHub.Repositories;
using StudentCharityHub.Services;
using StudentCharityHub.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = Environment.GetEnvironmentVariable("DefaultConnection")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();


builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;

    // Two-factor authentication
    options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// 2FA is configured via Identity options above

// Add authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "JWT_OR_COOKIE";
    options.DefaultChallengeScheme = "JWT_OR_COOKIE";
})
.AddJwtBearer("Bearer", options =>
{
    var jwtSettings = builder.Configuration.GetSection("JwtSettings");
    var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
    
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };

    // Configure SignalR JWT authentication
    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
})
.AddCookie("Cookies")
.AddGoogle(options =>
{
    var googleClientId = builder.Configuration["GoogleOAuth:ClientId"];
    var googleClientSecret = builder.Configuration["GoogleOAuth:ClientSecret"];

    if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
    }
})
.AddPolicyScheme("JWT_OR_COOKIE", "JWT or Cookie", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
            return "Bearer";
        return "Cookies";
    };
});

// Add CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactApp", policy =>
    {
        var allowedOrigins = new List<string>
        {
            "http://localhost:5173",
            "http://localhost:5174",
            "http://localhost:3000",
            "https://localhost:5173",
            "https://localhost:5174",
            "https://localhost:3000"
        }; 
        
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins("https://<frontend-service>.onrender.com")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });   

        var configOrigins = builder.Configuration["AllowedOrigins"];
        if (!string.IsNullOrEmpty(configOrigins))
        {
            allowedOrigins.AddRange(configOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        policy.WithOrigins(allowedOrigins.ToArray())
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Student Charity Hub API",
        Version = "v1",
        Description = "API for Student Charity Hub - connecting donors with students",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "Student Charity Hub Team",
            Email = "support@studentcharityhub.com"
        }
    });

    // Add JWT Bearer authentication to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add SignalR
builder.Services.AddSignalR();

// Add repository pattern
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Add services
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddHttpClient();
builder.Services.AddRazorPages();

// Add controllers and views
builder.Services.AddControllersWithViews();

// Add authorization policies
builder.Services.AddAuthorization(options =>
{
    // Existing role-based policies (keep for routing/dashboards)
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("DonorOnly", policy => policy.RequireRole("Donor"));
    options.AddPolicy("StudentOnly", policy => policy.RequireRole("Student"));
    options.AddPolicy("ManagerOnly", policy => policy.RequireRole("Manager"));
    options.AddPolicy("AdminOrManager", policy => policy.RequireRole("Admin", "Manager"));

    // Permission-based policies (PBAC) - roles remain active in parallel
    options.AddPolicy("Permissions.Manage", policy => policy.RequireClaim("permission", PermissionsManagement.ManagePermissions));
    options.AddPolicy("Permissions.Audit.View", policy => policy.RequireClaim("permission", PermissionsManagement.ViewAuditLog));

    options.AddPolicy("Users.View", policy => policy.RequireClaim("permission", Users.View));
    options.AddPolicy("Users.Manage", policy => policy.RequireClaim("permission", Users.Manage));

    options.AddPolicy("Students.View", policy => policy.RequireClaim("permission", Students.View));
    options.AddPolicy("Students.Manage", policy => policy.RequireClaim("permission", Students.Manage));

    options.AddPolicy("Donations.Create", policy => policy.RequireClaim("permission", Donations.Create));
    options.AddPolicy("Donations.View", policy => policy.RequireClaim("permission", Donations.View));
    options.AddPolicy("Donations.Verify", policy => policy.RequireClaim("permission", Donations.Verify));

    options.AddPolicy("Progress.View", policy => policy.RequireClaim("permission", Progress.View));
    options.AddPolicy("Progress.Manage", policy => policy.RequireClaim("permission", Progress.Manage));

    options.AddPolicy("Reports.View", policy => policy.RequireClaim("permission", Reports.View));
    options.AddPolicy("Reports.Manage", policy => policy.RequireClaim("permission", Reports.Manage));

    options.AddPolicy("Messages.View", policy => policy.RequireClaim("permission", Messages.View));
    options.AddPolicy("Messages.Manage", policy => policy.RequireClaim("permission", Messages.Manage));

    options.AddPolicy("Notifications.View", policy => policy.RequireClaim("permission", Notifications.View));
    options.AddPolicy("Notifications.Manage", policy => policy.RequireClaim("permission", Notifications.Manage));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
    // Enable Swagger in development
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Student Charity Hub API v1");
        options.RoutePrefix = "swagger"; // Access at /swagger
    });
}

app.UseHttpsRedirection();

// Configure static files with CORS support
var staticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Allow CORS for static files (images, documents)
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Methods", "GET");
    }
};
app.UseStaticFiles(staticFileOptions);

app.UseRouting();

// Enable CORS
app.UseCors("ReactApp");
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

// COMMENTED OUT MVC ROUTES - Using React Frontend Only
// Uncomment these lines if you want to use MVC views again
// app.MapControllerRoute(
//     name: "default",
//     pattern: "{controller=Home}/{action=Index}/{id?}");
// app.MapRazorPages();

// API Controllers only
app.MapControllers();

// SignalR Hubs
app.MapHub<StudentCharityHub.Hubs.NotificationHub>("/hubs/notifications");
app.MapHub<StudentCharityHub.Hubs.MessageHub>("/hubs/messages");

// Seed database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        // Ensure database is created
        context.Database.EnsureCreated();

        // Seed roles
        await SeedRolesAsync(roleManager);

        // Seed admin user
        await SeedAdminUserAsync(userManager);

        // Seed permission claims based on roles (PBAC runs alongside RBAC)
        await SeedPermissionClaimsAsync(userManager, roleManager);
        await EnsureAdminHasAllPermissionsAsync(userManager);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();

static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
{
    string[] roles = { "Admin", "Donor", "Student", "Manager" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}

static async Task SeedAdminUserAsync(UserManager<ApplicationUser> userManager)
{
    var adminEmail = "admin@studentcharityhub.com";
    var adminUser = await userManager.FindByEmailAsync(adminEmail);

    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "Admin",
            LastName = "User",
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        var result = await userManager.CreateAsync(adminUser, "Admin@123");
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
    }
}

static async Task SeedPermissionClaimsAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
{
    const string permissionClaimType = "permission";

    var rolePermissions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["Admin"] = AllPermissions.ToList(),
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
            var existingClaims = await userManager.GetClaimsAsync(user);
            foreach (var permission in permissions)
            {
                if (!existingClaims.Any(c => c.Type == permissionClaimType && c.Value == permission))
                {
                    await userManager.AddClaimAsync(user, new System.Security.Claims.Claim(permissionClaimType, permission));
                }
            }
        }
    }
}

static async Task EnsureAdminHasAllPermissionsAsync(UserManager<ApplicationUser> userManager)
{
    const string permissionClaimType = "permission";
    var adminEmail = "admin@studentcharityhub.com";
    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null) return;

    var existingClaims = await userManager.GetClaimsAsync(adminUser);
    foreach (var permission in AllPermissions)
    {
        if (!existingClaims.Any(c => c.Type == permissionClaimType && c.Value == permission))
        {
            await userManager.AddClaimAsync(adminUser, new System.Security.Claims.Claim(permissionClaimType, permission));
        }
    }
}

