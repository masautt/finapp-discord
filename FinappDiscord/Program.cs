using Database;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repos.Shared;
using Services.Interfaces;
using Services.Tables;
using System.Reflection;

namespace FinappDiscord;

internal class Program
{
    private static DiscordSocketClient? _client;
    private static IServiceProvider? _services;
    private static IConfiguration? _config;

    // Map command names to service types
    private static readonly Dictionary<string, Type> ServiceMap = new()
    {
        { "car", typeof(ICarSvc) },
        { "budget", typeof(IBudgetSvc) },
        { "paycheck", typeof(IPaycheckSvc) },
        { "investment", typeof(IInvestmentSvc) },
        { "sidegig", typeof(ISideGigSvc) },
        { "housing", typeof(IHousingSvc) },
        { "contribution", typeof(IContributionSvc) },
        { "transaction", typeof(ITransactionSvc) },
    };

    private static async Task Main()
    {
        // Load configuration (same pattern as Finapp.API)
        _config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Setup DI container
        var services = new ServiceCollection();

        // Register DbContext
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                _config.GetConnectionString("Finapp"),
                npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()
            )
        );

        // Register Finapp.Core services
        services.AddScoped<IBudgetSvc, BudgetSvc>();
        services.AddScoped<ISideGigSvc, SideGigSvc>();
        services.AddScoped<IHousingSvc, HousingSvc>();
        services.AddScoped<IContributionSvc, ContributionSvc>();
        services.AddScoped<ICarSvc, CarSvc>();
        services.AddScoped<IPaycheckSvc, PaycheckSvc>();
        services.AddScoped<IInvestmentSvc, InvestmentSvc>();
        services.AddScoped<ITransactionSvc, TransactionSvc>();
        services.AddScoped<EntityRepo>();
        services.AddScoped<CommonRepo>();
        services.AddScoped<DateRepo>();

        // Build DI container
        _services = services.BuildServiceProvider();

        Console.WriteLine("✅ Finapp.Core services initialized.");

        // Start the Discord bot
        await StartDiscordAsync();
    }

    private static async Task StartDiscordAsync()
    {
        var token = _config?["Discord:Token"] ?? throw new InvalidOperationException("Discord token missing");

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged
        });

        _client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _client.Ready += async () =>
        {
            Console.WriteLine("🤖 Bot is online!");

            // Dynamically register a global slash command for each service
            var commands = ServiceMap.Keys.Select(serviceName =>
                new SlashCommandBuilder()
                    .WithName(serviceName)
                    .WithDescription($"{serviceName}-related commands")
                    .AddOption(new SlashCommandOptionBuilder()
                        .WithName("count")
                        .WithDescription($"Get total number of {serviceName} entries")
                        .WithType(ApplicationCommandOptionType.SubCommand))
                    .Build()
            ).ToList();

            try
            {
                foreach (var cmd in commands)
                {
                    await _client.CreateGlobalApplicationCommandAsync(cmd);
                }

                Console.WriteLine($"✅ Registered {commands.Count} commands globally.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Command registration failed: {ex.Message}");
            }
        };

        _client.SlashCommandExecuted += HandleSlashCommandAsync;

        await Task.Delay(-1);
    }

    private static async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        var commandName = command.Data.Name; // e.g. "car", "budget"
        var subCommand = command.Data.Options.FirstOrDefault()?.Name;

        if (subCommand == "count" && ServiceMap.TryGetValue(commandName, out var serviceType))
        {
            await command.DeferAsync(); // shows "thinking..."

            using var scope = _services!.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService(serviceType);

            // Use reflection to call FetchTotalCount()
            var method = serviceType.GetMethod("FetchTotalCount", BindingFlags.Public | BindingFlags.Instance);
            if (method != null)
            {
                var resultTask = (Task<int>)method.Invoke(service, null)!;
                var count = await resultTask;

                await command.FollowupAsync($"📊 There are currently **{count}** {commandName} records in Finapp!");
            }
            else
            {
                await command.FollowupAsync($"⚠️ `{commandName}` does not implement FetchTotalCount().");
            }
        }
        else
        {
            await command.RespondAsync($"⚠️ Unknown command `{command.Data.Name}` or subcommand `{subCommand}`.");
        }
    }
}
