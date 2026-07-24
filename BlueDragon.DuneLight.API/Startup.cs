using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueDragon.DuneLight.API.Authentication;
using BlueDragon.DuneLight.API.Middleware;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Employees;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace BlueDragon.DuneLight.API;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    #region ServicesConfiguration

    public void ConfigureServices(IServiceCollection services)
    {
        #region Mvc

        services.AddCors();
        services.AddControllers()
            .AddJsonOptions(ConfigureJsonOptions)
            .ConfigureApiBehaviorOptions(ConfigureApiBehavior);
        services.AddOptions();

        #endregion

        #region Settings

        services.AddSingleton(Configuration.GetSection("DatabaseSettings").Get<DatabaseSettings>());

        JwtSettings jwtSettings = Configuration.GetSection("JwtSettings").Get<JwtSettings>();
        services.AddSingleton(jwtSettings);

        #endregion

        #region Authentication

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { });

        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme,
                    "ApiKey")
                .RequireAuthenticatedUser()
                .Build();
        });

        #endregion

        #region Services

        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IServiceCategoryService, ServiceCategoryService>();
        services.AddScoped<IServiceCatalogService, ServiceCatalogService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IPackageService, PackageService>();
        services.AddSingleton<IPriceResolutionService, PriceResolutionService>();

        services.AddScoped<IEngagementTypeService, EngagementTypeService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddSingleton<IFutureAppointmentsProvider, FutureAppointmentsProvider>();

        services.AddScoped<IClientTagService, ClientTagService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IClientPackageService, ClientPackageService>();
        services.AddSingleton<IClientFutureActivityProvider, ClientFutureActivityProvider>();

        services.AddScoped<IAppointmentService, AppointmentService>();

        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IGroupAttendanceService, GroupAttendanceService>();

        services.AddScoped<IRosterTypeService, RosterTypeService>();
        services.AddScoped<IRosterEntryService, RosterEntryService>();

        #endregion

        #region Handlers

        services.AddSingleton<IAuthHandler, AuthHandler>();

        services.AddSingleton<ILocationHandler, LocationHandler>();
        services.AddSingleton<IServiceCategoryHandler, ServiceCategoryHandler>();
        services.AddSingleton<IServiceHandler, ServiceHandler>();
        services.AddSingleton<IPriceListItemHandler, PriceListItemHandler>();
        services.AddSingleton<IPackageHandler, PackageHandler>();

        services.AddSingleton<IEngagementTypeHandler, EngagementTypeHandler>();
        services.AddSingleton<IEmployeeHandler, EmployeeHandler>();
        services.AddSingleton<IEmployeeAuditLogHandler, EmployeeAuditLogHandler>();

        services.AddSingleton<IClientTagHandler, ClientTagHandler>();
        services.AddSingleton<IClientHandler, ClientHandler>();
        services.AddSingleton<IClientPackageHandler, ClientPackageHandler>();

        services.AddSingleton<IAppointmentHandler, AppointmentHandler>();
        services.AddSingleton<IAppointmentAuditLogHandler, AppointmentAuditLogHandler>();

        services.AddSingleton<IGroupHandler, GroupHandler>();
        services.AddSingleton<IGroupAuditLogHandler, GroupAuditLogHandler>();
        services.AddSingleton<IGroupAttendanceHandler, GroupAttendanceHandler>();

        services.AddSingleton<IRosterTypeHandler, RosterTypeHandler>();
        services.AddSingleton<IRosterEntryHandler, RosterEntryHandler>();
        services.AddSingleton<IRosterAuditLogHandler, RosterAuditLogHandler>();

        #endregion

        #region Swagger

        services.AddSwaggerGen(ConfigureSwaggerGen);

        #endregion
    }

    private void ConfigureJsonOptions(JsonOptions options)
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }

    /// <summary>Preusmjerava automatsku [ApiController] model-validaciju u isti error oblik koji koristi ExceptionHandlingMiddleware.</summary>
    private void ConfigureApiBehavior(ApiBehaviorOptions options)
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            Dictionary<string, string[]> details = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Errors.Select(e => e.ErrorMessage).ToArray());

            ErrorResponse response = new ErrorResponse(new ErrorDetail(ErrorCodes.ValidationError, "Neispravan zahtjev.", details));
            return new BadRequestObjectResult(response);
        };
    }

    private void ConfigureSwaggerGen(SwaggerGenOptions swaggerGenOptions)
    {
        swaggerGenOptions.SwaggerDoc("v1.0", new OpenApiInfo
        {
            Title = "BlueDragon.DuneLight.API",
            Version = "v1.0",
            Description = "Postman collection: Not available"
        });
        swaggerGenOptions.CustomSchemaIds(x => x.FullName);

        string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        swaggerGenOptions.IncludeXmlComments(xmlPath);

        swaggerGenOptions.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Unesite JWT token (bez 'Bearer ' prefiksa)"
        });
    }

    #endregion

    #region ApplicationConfiguration

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseSerilogRequestLogging();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseCors(ConfigureCors);

        app.UseAuthentication();
        app.UseAuthorization();
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(ConfigureSwaggerUI);
        }
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.UseEndpoints(ConfigureEndpoints);
    }

    private void ConfigureCors(CorsPolicyBuilder builder)
    {
        builder.SetIsOriginAllowed(origin => true)
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader();
    }

    private void ConfigureSwaggerUI(SwaggerUIOptions swaggerUiOptions)
    {
        swaggerUiOptions.SwaggerEndpoint("../swagger/v1.0/swagger.json", "BlueDragon.DuneLight.API documentation");
        swaggerUiOptions.DefaultModelExpandDepth(2);
        swaggerUiOptions.DefaultModelRendering(ModelRendering.Model);
        swaggerUiOptions.DefaultModelsExpandDepth(-1);
        swaggerUiOptions.DisplayRequestDuration();
        swaggerUiOptions.DocExpansion(DocExpansion.None);
        swaggerUiOptions.EnableDeepLinking();
        swaggerUiOptions.EnableFilter();
        swaggerUiOptions.MaxDisplayedTags(50);
        swaggerUiOptions.ShowExtensions();
        swaggerUiOptions.SupportedSubmitMethods(SubmitMethod.Get, SubmitMethod.Head, SubmitMethod.Post, SubmitMethod.Put, SubmitMethod.Delete, SubmitMethod.Patch);
    }

    private void ConfigureEndpoints(IEndpointRouteBuilder builder)
    {
        builder.MapControllers();
    }

    #endregion
}
