using Quartz;
using Serilog;
using TNRD.Zeepkist.WorkshopApi.Drone;
using TNRD.Zeepkist.WorkshopApi.Drone.Api;
using TNRD.Zeepkist.WorkshopApi.Drone.Google;
using TNRD.Zeepkist.WorkshopApi.Drone.Jobs;
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
        // services.AddHostedService<Worker>();

        services.AddQuartzHostedService(options =>
        {
            options.AwaitApplicationStarted = true;
            options.WaitForJobsToComplete = true;
        });

        services.AddQuartz(options =>
        {
            options.AddJob<FullScanJob>(FullScanJob.JobKey, 
                    configurator => configurator.DisallowConcurrentExecution()).UseDefaultThreadPool(1);
            options.AddJob<CreatedScanJob>(CreatedScanJob.JobKey,
                configurator => configurator.DisallowConcurrentExecution()).UseDefaultThreadPool(1);
            options.AddJob<ModifiedScanJob>(ModifiedScanJob.JobKey,
                configurator => configurator.DisallowConcurrentExecution()).UseDefaultThreadPool(1);
            
            options.AddTrigger(configure =>
            {
                configure
                    .WithIdentity("FullScanJob")
                    .ForJob(FullScanJob.JobKey)
                    .WithCronSchedule("0 0 0 1/14 * ? *");
            });
            
            options.AddTrigger(configure =>
            {
                configure
                    .WithIdentity("CreatedScanJob")
                    .ForJob(CreatedScanJob.JobKey)
                    .WithCronSchedule("0 15/5 * ? * * *");
            });
            
            options.AddTrigger(configure =>
            {
                configure
                    .WithIdentity("ModifiedScanJob")
                    .ForJob(ModifiedScanJob.JobKey)
                    .WithCronSchedule("0 15/10 * ? * * *");
            });

            options.AddTrigger(configure =>
            {
                configure
                    .WithIdentity("RequestsScanJob")
                    .ForJob(RequestsScanJob.JobKey)
                    .WithCronSchedule("0 15/15 * ? * * *");
            });
        });

        services.Configure<ApiOptions>(context.Configuration.GetSection("Api"));
        services.AddHttpClient<ApiClient>();
        
        services.Configure<GoogleOptions>(context.Configuration.GetSection("Google"));
        services.AddSingleton<IUploadService, CloudStorageUploadService>();

        services.Configure<SteamOptions>(context.Configuration.GetSection("Steam"));
        services.AddHttpClient<SteamClient>();
    })
    .Build();

host.Run();
