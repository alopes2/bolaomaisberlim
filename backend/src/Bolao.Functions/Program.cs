using Bolao.Functions;

var builder = WebApplication.CreateBuilder(args);
AppBootstrap.ConfigureServices(builder.Services, builder.Environment);

var app = builder.Build();
AppBootstrap.ConfigurePipeline(app);
app.Run();

public partial class Program;
