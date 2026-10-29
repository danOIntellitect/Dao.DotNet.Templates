var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Dao_Aspire_Angular_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var frontend = builder.AddJavaScriptApp("frontend", "../Dao.Aspire.Angular.Web")
#if (IncludeAuthentication)
    .WithRunScript("start:auth")
#else
    .WithRunScript("start")
#endif
    .WithReference(api)
    .WaitFor(api)
#if (IncludeAuthentication)
    .WithHttpsEndpoint(port: 4200, targetPort: 4200, env: "PORT", isProxied: false)
#else
    .WithHttpEndpoint(env: "PORT")
#endif
    .WithHttpHealthCheck("/");

api.PublishWithContainerFiles(frontend, "wwwroot");

builder.Build().Run();
