var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Dao_Aspire_Angular_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var frontend = builder.AddJavaScriptApp("frontend", "../Dao.Aspire.Angular.Web")
    .WithRunScript("start")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/");

api.PublishWithContainerFiles(frontend, "wwwroot");

builder.Build().Run();
