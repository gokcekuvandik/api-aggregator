var builder = DistributedApplication.CreateBuilder(args);

builder.AddDotnetProject("api", "../Aggregator.Api/Aggregator.Api.csproj");

builder.Build().Run();
