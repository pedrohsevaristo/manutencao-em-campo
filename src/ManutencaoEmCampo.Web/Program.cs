using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ManutencaoEmCampo.Web.Data;
using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var sqlite = builder.Environment.IsDevelopment() && builder.Configuration["Database:Provider"] == "Sqlite";
var connection = builder.Configuration.GetConnectionString(sqlite ? "Sqlite" : "SqlServer")
    ?? throw new InvalidOperationException("Configure a cadeia de conexão do banco de dados.");

builder.Services.AddDbContext<WorkDbContext>(options =>
{
    if (sqlite)
        options.UseSqlite(connection);
    else
        options.UseSqlServer(connection);
});

builder.Services.AddIdentity<WorkUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<WorkDbContext>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<WorkUser>, WorkUserClaims>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/entrar";
    options.AccessDeniedPath = "/acesso-negado";
    options.Cookie.Name = "ManutencaoEmCampo.Session";
});
builder.Services.AddAuthentication().AddBearerToken(SessionSchemes.Api, options =>
    options.BearerTokenExpiration = TimeSpan.FromHours(2));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var mobileOrigin = builder.Configuration["Mobile:WebOrigin"];
builder.Services.AddCors(options => options.AddPolicy("mobile-web", policy =>
{
    if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(mobileOrigin))
        policy.WithOrigins(mobileOrigin).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddControllersWithViews().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddScoped<TicketService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("pt-BR"),
    SupportedCultures = [CultureInfo.GetCultureInfo("pt-BR")],
    SupportedUICultures = [CultureInfo.GetCultureInfo("pt-BR")]
});

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseCors("mobile-web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllers();

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Demo:Enabled"))
    await DemoData.InitializeAsync(app.Services, sqlite, app.Configuration["Demo:Password"]);

await app.RunAsync();
