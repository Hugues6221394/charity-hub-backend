using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StudentCharityHub;
using StudentCharityHub.Data;
using StudentCharityHub.Hubs;
using StudentCharityHub.Models;
using StudentCharityHub.Repositories;
using StudentCharityHub.Services;
using static StudentCharityHub.PermissionCatalogLocal;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// CONFIGURATION
// ----------------------------------------------------
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

// ----------------------------------------------------
// DATABASE (PostgreSQL on Render)
// ----------------------------------------------------
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException("Database connection string not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// ----------------------------------------------------
// IDENTITY
// ----------------------------------------------------
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ----------------------------------------------------
// AUTHENTICATION (JWT + Cookies)
// ----------------------------------------------------
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "JWT_OR_COOKIE";
    options.DefaultChallengeScheme = "JWT_OR_COOKIE";
})
.AddPolicyScheme("JWT_OR_COOKIE", "JWT or Cookie", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        return authHeader?.StartsWith("Bearer ") == true ? "Bearer" : "Cookies";
    };
})
.AddJwtBearer("Bearer", options =>
{
    var jwt = builder.Configuration.GetSection("JwtSettings");
    var secret = jwt["SecretKey"]
        ?? throw new InvalidOperationException("JWT SecretKey missing");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
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
    options.ClientId = builder.Configuration["GoogleOAuth:ClientId"]!;
    options.ClientSecret = builder.Configuration["GoogleOAuth:ClientSecret"]!;
});

// ----------------------------------------------------
// CORS (React on Render)
// ----------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:3000",
                "https://charity-hub-frontend.onrender.com"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ----------------------------------------------------
// SERVICES
// ----------------------------------------------------
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddRazorPages();

// ----------------------------------------------------
// AUTHORIZATION POLICIES
// ----------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    options.AddPolicy("ManagerOnly", p => p.RequireRole("Manager"));
    options.AddPolicy("AdminOrManager", p => p.RequireRole("Admin", "Manager"));
});

// ----------------------------------------------------
// SWAGGER
// ----------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ----------------------------------------------------
// BUILD APP
// ----------------------------------------------------
var app = builder.Build();

// ----------------------------------------------------
// PIPELINE
// ----------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHttpsRedirection(); // ONLY in dev
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts(); // Render handles HTTPS
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors("ReactApp");
app.UseAuthentication();
app.UseAuthorization();

// ----------------------------------------------------
// ENDPOINTS
// ----------------------------------------------------
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<MessageHub>("/hubs/messages");
app.MapGet("/", () => Results.Ok("Backend running"));

// ----------------------------------------------------
// DATABASE MIGRATION + SEEDING
// ----------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    // ✅ CORRECT FOR POSTGRES + IDENTITY
    context.Database.Migrate();

    await SeedRolesAsync(roleManager);
    await SeedAdminUserAsync(userManager);
    await SeedPermissionClaimsAsync(userManager, roleManager);
    await EnsureAdminHasAllPermissionsAsync(userManager);
}

app.Run();


// ====================================================
// SEEDERS
// ====================================================
static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
{
    string[] roles = { "Admin", "Donor", "Student", "Manager" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

static async Task SeedAdminUserAsync(UserManager<ApplicationUser> userManager)
{
    const string email = "admin@studentcharityhub.com";
    const string password = "Admin@123";

    var user = await userManager.FindByEmailAsync(email);
    if (user != null) return;

    user = new ApplicationUser
    {
        UserName = email,
        Email = email,
        EmailConfirmed = true,
        FirstName = "Admin",
        LastName = "User",
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    var result = await userManager.CreateAsync(user, password);
    if (result.Succeeded)
        await userManager.AddToRoleAsync(user, "Admin");
}

static async Task SeedPermissionClaimsAsync(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager)
{
    const string type = "permission";

    var adminRole = await roleManager.FindByNameAsync("Admin");
    if (adminRole == null) return;

    var users = await userManager.GetUsersInRoleAsync("Admin");
    foreach (var user in users)
    {
        var claims = await userManager.GetClaimsAsync(user);
        foreach (var permission in AllPermissions)
        {
            if (!claims.Any(c => c.Type == type && c.Value == permission))
            {
                await userManager.AddClaimAsync(user,
                    new System.Security.Claims.Claim(type, permission));
            }
        }
    }
}

static async Task EnsureAdminHasAllPermissionsAsync(UserManager<ApplicationUser> userManager)
{
    const string email = "admin@studentcharityhub.com";
    const string type = "permission";

    var user = await userManager.FindByEmailAsync(email);
    if (user == null) return;

    var claims = await userManager.GetClaimsAsync(user);
    foreach (var permission in AllPermissions)
    {
        if (!claims.Any(c => c.Type == type && c.Value == permission))
        {
            await userManager.AddClaimAsync(user,
                new System.Security.Claims.Claim(type, permission));
        }
    }
}
