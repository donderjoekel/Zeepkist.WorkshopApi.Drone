using Serilog;
using TNRD.Zeepkist.WorkshopApi.Drone;
using TNRD.Zeepkist.WorkshopApi.Drone.Api;
using TNRD.Zeepkist.WorkshopApi.Drone.Google;
using TNRD.Zeepkist.WorkshopApi.Drone.Steam;

IHost host = Host.CreateDefaultBuilder(args)
    .UseConsoleLifetime()
    .UseSerilog((context, configuration) =>
    {
        configuration
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Source", "Zworpshop")
            .MinimumLevel.Debug()
            .WriteTo.Seq(context.Configuration["Seq:Url"], apiKey: context.Configuration["Seq:Key"])
            .WriteTo.Console();
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
    })
    .Build();

host.Run();
