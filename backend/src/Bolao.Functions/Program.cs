using Bolao.Functions.Api;
using Bolao.Functions.Domain;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PredictionService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapPublicEndpoints();
app.MapParticipantEndpoints();

app.Run();

public partial class Program;
