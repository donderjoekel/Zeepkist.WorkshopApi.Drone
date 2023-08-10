using Logtail.NLog;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Config;
using Serilog;
using TNRD.Zeepkist.WorkshopApi.Drone;
using TNRD.Zeepkist.WorkshopApi.Drone.Api;
using TNRD.Zeepkist.WorkshopApi.Drone.Google;
using TNRD.Zeepkist.WorkshopApi.Drone.Steam;
using LogLevel = NLog.LogLevel;

IHost host = Host.CreateDefaultBuilder(args)
    .UseConsoleLifetime()
    .UseSerilog((context, configuration) =>
    {
        configuration
            .WriteTo.Console()
            .WriteTo.NLog()
            .MinimumLevel.Debug();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<Worker>();

        services.Configure<ApiOptions>(context.Configuration.GetSection("Api"));
        services.AddHttpClient<ApiClient>();
        
        services.Configure<GoogleOptions>(context.Configuration.GetSection("Google"));
        services.AddSingleton<IUploadService, CloudStorageUploadService>();

        services.Configure<SteamOptions>(context.Configuration.GetSection("Steam"));
        services.AddHttpClient<SteamClient>();

        services.Configure<LogtailOptions>(context.Configuration.GetSection("Logtail"));
    })
    .Build();

LogtailOptions logtailOptions = host.Services.GetRequiredService<IOptions<LogtailOptions>>().Value;

LoggingConfiguration config = new();
LogtailTarget logtailTarget = new()
{
    Name = "logtail",
    Layout = logtailOptions.Format,
    SourceToken = logtailOptions.SourceToken
};
config.AddRule(LogLevel.Trace, LogLevel.Fatal, logtailTarget, "*");

LogManager.Configuration = config;

host.Run();
