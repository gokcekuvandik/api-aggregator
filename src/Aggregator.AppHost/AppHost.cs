using Aggregator.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddDotnetProject("api", "../Aggregator.Api/Aggregator.Api.csproj");

// Off by default so a plain run needs only the .NET SDK.
var scaleOut = ScaleOutSettings.Read(builder.Configuration);
if (scaleOut.Enabled)
{
    api.WithReplicas(scaleOut.Replicas);
    if (scaleOut.Redis)
    {
        var cache = builder.AddRedis("cache");
        api.WithReference(cache).WaitFor(cache);
    }
}

builder.Build().Run();
