using Amazon.CognitoIdentityProvider;
using Amazon.DynamoDBv2;
using Amazon.Scheduler;
using Amazon.SimpleEmailV2;
using Bolao.Functions.Admin;
using Bolao.Functions.Api;
using Bolao.Functions.Auth;
using Bolao.Functions.Domain;
using Bolao.Functions.E2E;
using Bolao.Functions.FootballApi;
using Bolao.Functions.Jobs;
using Bolao.Functions.Notifications;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;
using Microsoft.AspNetCore.Authentication;

namespace Bolao.Functions;

public static class AppBootstrap
{
    public static void ConfigureServices(IServiceCollection services, IWebHostEnvironment environment)
    {
        E2EMode.EnsureSafe(environment.EnvironmentName, Environment.GetEnvironmentVariable("AWS_EXECUTION_ENV"));
        services.AddLogging(logging => logging.AddLambdaLogger(new LambdaLoggerOptions
        {
            IncludeException = true
        }));
        services.AddAuthentication("Gateway")
            .AddScheme<AuthenticationSchemeOptions, GatewayAuthenticationHandler>("Gateway", _ => { });
        services.AddAuthorization(options =>
            options.AddPolicy("admins", policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("is_admin", "true")));
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()));

        if (environment.IsEnvironment("E2E"))
        {
            ConfigureE2E(services);
            return;
        }

        ConfigureAws(services);
    }

    public static void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapPublicEndpoints();
            endpoints.MapParticipantEndpoints();
            endpoints.MapAdminEndpoints();
            if (app.ApplicationServices.GetRequiredService<IWebHostEnvironment>().IsEnvironment("E2E"))
            {
                endpoints.MapPost("/e2e/close", (E2EState state) =>
                {
                    state.ClosePredictions();
                    return Results.NoContent();
                });
                endpoints.MapPost("/e2e/reset", (E2EState state) =>
                {
                    state.Reset();
                    return Results.NoContent();
                });
            }
        });
    }

    private static void ConfigureE2E(IServiceCollection services)
    {
        services.AddSingleton<MutableE2ETimeProvider>();
        services.AddSingleton<E2EState>();
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<MutableE2ETimeProvider>());
        services.AddSingleton<IApiQueries>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IUserProfileService>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IMatchRepository>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IPredictionRepository>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IAdminApi>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IMatchManagementStore>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IWorldCupSyncService>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IWorldCupSyncLock>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IMatchScheduleService>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IResultConfirmationStore>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IConfirmedResultPublisher>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IWinnerNotificationService>(provider => provider.GetRequiredService<E2EState>());
        services.AddSingleton<IRosterCatalog>(_ => new JsonRosterCatalog(RosterPath()));
        services.AddSingleton<MatchStatusService>();
        services.AddSingleton<IMatchStatusLock, InMemoryMatchStatusLock>();
        services.AddSingleton<IMatchStatusWaiter, MatchStatusWaiter>();
        services.AddSingleton<MatchStatusCoordinator>();
        services.AddScoped<PredictionService>();
        services.AddScoped<ResultConfirmationService>();
    }

    private static void ConfigureAws(IServiceCollection services)
    {
        var options = new DynamoDbOptions
        {
            ParticipantsTableName = Required("PARTICIPANTS_TABLE_NAME"),
            MatchesTableName = Required("MATCHES_TABLE_NAME"),
            PredictionsTableName = Required("PREDICTIONS_TABLE_NAME"),
            StandingsTableName = Required("STANDINGS_TABLE_NAME"),
            ApiUsageTableName = Required("API_USAGE_TABLE_NAME")
        };
        services.AddSingleton(options);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IAmazonDynamoDB, AmazonDynamoDBClient>();
        services.AddSingleton<IAmazonScheduler, AmazonSchedulerClient>();
        services.AddSingleton<IAmazonCognitoIdentityProvider, AmazonCognitoIdentityProviderClient>();
        services.AddSingleton<IAmazonSimpleEmailServiceV2, AmazonSimpleEmailServiceV2Client>();
        services.AddHttpClient();
        services.AddSingleton<IRosterCatalog>(_ => new JsonRosterCatalog(RosterPath()));

        services.AddScoped<IMatchRepository, DynamoMatchRepository>();
        services.AddScoped<IPredictionRepository, DynamoPredictionRepository>();
        services.AddScoped<IStandingRepository, DynamoStandingRepository>();
        services.AddScoped<IResultRepository, DynamoResultRepository>();
        services.AddScoped<IApiQueries, DynamoApiQueries>();
        services.AddScoped<IUserProfileService, DynamoUserProfileService>();
        services.AddScoped<IMatchPollingStore, DynamoMatchPollingStore>();
        services.AddScoped<IProvisionalResultStore, DynamoProvisionalResultStore>();
        services.AddScoped<IResultConfirmationStore, DynamoResultConfirmationStore>();
        services.AddScoped<IApiQuotaRepository, DynamoApiQuotaRepository>();
        services.AddScoped<ApiQuotaGuard>();
        services.AddScoped<IFootballApiClient>(provider => new FootballApiClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(),
            provider.GetRequiredService<ApiQuotaGuard>(),
            provider.GetRequiredService<ILogger<FootballApiClient>>()));
        services.AddScoped<IMatchScheduleService>(provider => new MatchScheduleService(
            provider.GetRequiredService<IAmazonScheduler>(),
            Required("SCHEDULER_GROUP_NAME"),
            Required("MATCH_POLLING_FUNCTION_ARN"),
            Required("SCHEDULER_INVOKE_ROLE_ARN")));
        services.AddScoped<MatchPollingHandler>();
        services.AddScoped<IAdminApi, DynamoAdminApi>();
        services.AddScoped<IMatchManagementStore, DynamoMatchManagementStore>();
        services.AddScoped<IWorldCupSyncLock, DynamoWorldCupSyncLock>();
        services.AddScoped<MatchStatusService>();
        services.AddScoped<IMatchStatusLock, DynamoMatchStatusLock>();
        services.AddScoped<IMatchStatusWaiter, MatchStatusWaiter>();
        services.AddScoped<MatchStatusCoordinator>();
        services.AddScoped<IWorldCupSyncService, WorldCupSyncService>();
        services.AddScoped<PredictionService>();
        services.AddScoped<ResultPublicationService>();
        services.AddScoped<IConfirmedResultPublisher, ConfirmedResultPublisher>();
        var sesFromEmail = Environment.GetEnvironmentVariable("SES_FROM_EMAIL");
        if (string.IsNullOrWhiteSpace(sesFromEmail))
        {
            services.AddScoped<IWinnerNotificationService, DisabledWinnerNotificationService>();
        }
        else
        {
            services.AddScoped<IWinnerNotificationStore, DynamoWinnerNotificationStore>();
            services.AddScoped<IWinnerLookup>(provider => new DynamoCognitoWinnerLookup(
                provider.GetRequiredService<IAmazonDynamoDB>(),
                provider.GetRequiredService<IAmazonCognitoIdentityProvider>(),
                options,
                provider.GetRequiredService<IPredictionRepository>(),
                Required("COGNITO_USER_POOL_ID")));
            services.AddScoped<IWinnerEmailSender>(provider => new SesWinnerEmailSender(
                provider.GetRequiredService<IAmazonSimpleEmailServiceV2>(),
                sesFromEmail));
            services.AddScoped<IWinnerNotificationService, SesWinnerNotificationService>();
        }
        services.AddScoped<ResultConfirmationService>();
    }

    private static string RosterPath() =>
        Path.Combine(AppContext.BaseDirectory, "assets", "teams.json");

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required.");
}
