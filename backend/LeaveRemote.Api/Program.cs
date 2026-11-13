using LeaveRemote.Api.Controllers;
using LeaveRemote.Api.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    // Erorile client fără corp (ex. 415) sunt formatate uniform de UseStatusCodePages, nu ca ProblemDetails.
    options.SuppressMapClientErrors = true;
    options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new
    {
        code = "INVALID_INPUT",
        message = "Datele trimise lipsesc sau au un format invalid."
    });
});
builder.Services.AddScoped<LeaveBalanceService>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT secret key is missing.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity ||
                    !int.TryParse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                {
                    context.Fail("Invalid user identity.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var role = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId && u.IsActive)
                    .Select(u => (LeaveRemote.Api.Models.UserRole?)u.Role)
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                if (!role.HasValue)
                {
                    context.Fail("Inactive user.");
                    return;
                }

                foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList())
                {
                    identity.RemoveClaim(claim);
                }
                identity.AddClaim(new Claim(identity.RoleClaimType, role.Value.ToString()));
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await DbSeeder.SeedAsync(db, app.Environment.IsDevelopment());
}


// Orice excepție neprevăzută devine un 500 cu formatul unic { code, message }, fără detalii interne.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(error, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    var (code, message) = ApiErrors.ForStatus(StatusCodes.Status500InternalServerError);
    await context.Response.WriteAsJsonAsync(new { code, message });
}));

// Răspunsurile de eroare produse de framework fără corp (401/403 din [Authorize], 404 de rută etc.)
// primesc același format ca erorile întoarse de controllere.
app.UseStatusCodePages(async statusContext =>
{
    var response = statusContext.HttpContext.Response;
    var (code, message) = ApiErrors.ForStatus(response.StatusCode);
    await response.WriteAsJsonAsync(new { code, message });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
