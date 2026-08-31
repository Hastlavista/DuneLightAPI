using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using X.PortUtility;

namespace BlueDragon.DuneLight.API;

public class Program
{
    public const long MaxRequestBodySize = 100000000L;
    public static int Port { get; set; }

    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .WriteTo.Console()
            .WriteTo.Seq("http://localhost:5341")
            .CreateBootstrapLogger();

        try
        {
            IHost webHost = BuildWebHost(args);
            await webHost.StartAsync();

            Log.Information("BlueDragon.DuneLight.API started. Listening on: http://localhost:{Port}/ Swagger: http://localhost:{Port}/swagger", Port, Port);

            await webHost.WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static IHost BuildWebHost(string[] args)
    {
        IConfiguration bindingConfig = new ConfigurationBuilder()
            .AddCommandLine(args)
            .Build();

        Port = bindingConfig.GetValue<int?>("port") ?? FreePorts.Find();

        return Host.CreateDefaultBuilder(args)
            .UseSerilog((context, services, configuration) => configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .WriteTo.Console()
                .WriteTo.Seq("http://localhost:5341"))
            .UseDefaultServiceProvider(opts => opts.ValidateScopes = false)
            .ConfigureWebHostDefaults(ConfigureWebHost)
            .Build();
    }

    private static void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseStartup<Startup>();
        builder.UseKestrel(options =>
        {
            options.Listen(IPAddress.Any, Port);
            options.Limits.MaxRequestBodySize = MaxRequestBodySize;
            options.Limits.MaxConcurrentConnections = 100;
        });
    }
}
