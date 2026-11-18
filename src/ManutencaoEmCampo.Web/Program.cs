using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ManutencaoEmCampo.Web.Data;
using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

var initializeSqlDemo = args.FirstOrDefault() == "--initialize-sql-demo";
var builder = WebApplication.CreateBuilder(initializeSqlDemo ? args[1..] : args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = TrafficPolicy.MaxBodyBytes);
var trustedForwarding = TrafficPolicy.TrustedForwarding(builder.Configuration);
// Generic hosting forwarding must not replace the client IP or Host using arbitrary headers.
builder.Services.PostConfigure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.None);
var allowedHosts = TransportPolicy.AllowedHosts(builder.Configuration["AllowedHosts"]);
builder.Services.PostConfigure<HostFilteringOptions>(options =>
{
    options.AllowedHosts = allowedHosts;
    options.AllowEmptyHosts = false;
});
var demo = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Demo:Enabled");
var allowLocalHttp = demo && builder.Configuration.GetValue<bool>("Demo:AllowHttp");
var cookieSecurity = allowLocalHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
if (demo)
    builder.Services.AddDataProtection().AddKeyManagementOptions(options => options.XmlRepository = new DemoKeyRepository());
var sqlite = builder.Environment.IsDevelopment() && builder.Configuration["Database:Provider"] == "Sqlite";
if (initializeSqlDemo && (!demo || builder.Configuration["Database:Provider"] != "SqlServer"))
    throw new InvalidOperationException("Use --initialize-sql-demo somente em Development com Demo:Enabled e Database:Provider=SqlServer.");

var connection = sqlite
    ? builder.Configuration.GetConnectionString("Sqlite")
        ?? throw new InvalidOperationException("Configure ConnectionStrings__Sqlite.")
    : SqlServerPolicy.Connection(builder.Configuration.GetConnectionString(
        initializeSqlDemo ? "SqlServerMigrations" : "SqlServer"), runtime: !initializeSqlDemo);

builder.Services.AddSingleton<RuntimeSqlPrivileges>();
builder.Services.AddDbContext<WorkDbContext>((services, options) =>
{
    if (sqlite)
        options.UseSqlite(connection);
    else
    {
        options.UseSqlServer(connection);
        if (!initializeSqlDemo)
            options.AddInterceptors(services.GetRequiredService<RuntimeSqlPrivileges>());
    }
});

builder.Services.AddIdentity<WorkUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<WorkDbContext>()
    .AddUserStore<WorkUserStore>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<WorkUser>, WorkUserClaims>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/entrar";
    options.AccessDeniedPath = "/acesso-negado";
    options.Cookie.Name = "ManutencaoEmCampo.Session";
    options.Cookie.SecurePolicy = cookieSecurity;
    options.ExpireTimeSpan = ActiveSessions.CookieLifetime;
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context =>
    {
        var sessions = context.HttpContext.RequestServices.GetRequiredService<ActiveSessions>();
        if (!await sessions.ValidateAsync(context.Principal, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});
builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = cookieSecurity);
builder.Services.Configure<CookieTempDataProviderOptions>(options => options.Cookie.SecurePolicy = cookieSecurity);
builder.Services.AddAuthentication()
    .AddBearerToken(SessionSchemes.Token, options => options.BearerTokenExpiration = ActiveSessions.BearerLifetime)
    .AddScheme<AuthenticationSchemeOptions, ActiveBearerHandler>(SessionSchemes.Api, _ => { });
var loginPerSource = TrafficPolicy.Limit(builder.Configuration, "LoginPerSource", 60);
var operationsPerAccount = TrafficPolicy.Limit(builder.Configuration, "OperationsPerAccount", 120);
var listPerAccount = TrafficPolicy.Limit(builder.Configuration, "ListPerAccount", 40);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var http = context.HttpContext;
        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        var kind = policy == TrafficPolicy.Login ? SecurityEvent.LoginBlocked : SecurityEvent.RequestLimited;
        http.RequestServices.GetRequiredService<SecurityEvents>().Record(kind, SecurityEvents.Surface(http));
        TrafficPolicy.RetryAfter(context.HttpContext.Response, context.Lease);
        await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
            title: "Muitas requisições. Aguarde antes de tentar novamente.").ExecuteAsync(context.HttpContext);
    };
    options.AddPolicy(TrafficPolicy.Login, context => RateLimitPartition.GetFixedWindowLimiter(
        TrafficPolicy.Source(context), _ => TrafficPolicy.Window(loginPerSource)));
    options.AddPolicy(TrafficPolicy.Operations, context => RateLimitPartition.GetFixedWindowLimiter(
        TrafficPolicy.Account(context), _ => TrafficPolicy.Window(operationsPerAccount)));
    options.AddPolicy(TrafficPolicy.TicketList, context => RateLimitPartition.GetFixedWindowLimiter(
        TrafficPolicy.Account(context), _ => TrafficPolicy.Window(listPerAccount)));
});
builder.Services.AddSingleton<LoginAttempts>();

var mobileOrigin = TransportPolicy.MobileOrigin(builder.Configuration["Mobile:WebOrigin"], allowLocalHttp);
builder.Services.AddCors(options => options.AddPolicy("mobile-web", policy =>
{
    if (mobileOrigin is not null)
        policy.WithOrigins(mobileOrigin).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddControllersWithViews()
    .AddViewOptions(options => options.HtmlHelperOptions.ClientValidationEnabled = false)
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<ActiveSessions>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SecurityEvents>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationEvents>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("pt-BR"),
    SupportedCultures = [CultureInfo.GetCultureInfo("pt-BR")],
    SupportedUICultures = [CultureInfo.GetCultureInfo("pt-BR")]
});

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException invalid
        ? invalid.StatusCode : StatusCodes.Status500InternalServerError
});
app.Use(async (context, next) =>
{
    context.Response.OnStarting(static state =>
    {
        var http = (HttpContext)state;
        var response = http.Response;
        response.Headers.XContentTypeOptions = "nosniff";
        if (http.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null
            && MediaTypeHeaderValue.TryParse(response.ContentType, out var contentType)
            && contentType.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers.ContentSecurityPolicy =
                "default-src 'none'; style-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
            response.Headers.XFrameOptions = "DENY";
        }

        return Task.CompletedTask;
    }, context);
    await next(context);
});
app.Use(async (context, next) =>
{
    if (context.Request.IsHttps && context.Features.Get<ITlsHandshakeFeature>() is not null
        || allowLocalHttp && TransportPolicy.IsLocalHttp(context))
    {
        await next(context);
        return;
    }

    await Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Use uma conexão HTTPS.")
        .ExecuteAsync(context);
});
if (trustedForwarding is not null)
    app.UseForwardedHeaders(trustedForwarding);
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
else
    app.UseHsts();

app.UseRouting();
app.UseCors("mobile-web");
app.Use(async (context, next) =>
{
    if (context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
    {
        context.Response.OnStarting(static state =>
        {
            var response = (HttpResponse)state;
            response.Headers.CacheControl = "no-store";
            response.Headers.Pragma = "no-cache";
            return Task.CompletedTask;
        }, context.Response);
    }
    if (context.Request.ContentLength > TrafficPolicy.MaxBodyBytes)
    {
        context.RequestServices.GetRequiredService<SecurityEvents>()
            .Record(SecurityEvent.RequestBodyRejected, SecurityEvents.Surface(context));
        await Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Corpo da requisição muito grande.")
            .ExecuteAsync(context);
        return;
    }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapStaticAssets();
app.MapControllers();

if (initializeSqlDemo)
{
    await DemoData.InitializeAsync(app.Services, sqlite: false, app.Configuration["Demo:Password"]);
    return;
}

if (demo && sqlite)
    await DemoData.InitializeAsync(app.Services, sqlite: true, app.Configuration["Demo:Password"]);

await app.RunAsync();
