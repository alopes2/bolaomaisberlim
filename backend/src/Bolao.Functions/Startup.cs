using Amazon.Lambda.AspNetCoreServer;

namespace Bolao.Functions;

public class Startup(IWebHostEnvironment environment)
{
    public void ConfigureServices(IServiceCollection services) =>
        AppBootstrap.ConfigureServices(services, environment);

    public void Configure(IApplicationBuilder app) => AppBootstrap.ConfigurePipeline(app);
}

public class LambdaEntryPoint : APIGatewayHttpApiV2ProxyFunction<Startup>;
