using Database;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repos.Shared;
using Services.Interfaces;
using Services.Tables;

namespace FinappDiscord;

internal class Program
{
    private static DiscordSocketClient? _client;
    private static IServiceProvider? _services;
    private static IConfiguration? _config;

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

        // Register DbContext (same as in Finapp.API)
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                _config.GetConnectionString("Finapp"),
                npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()
            )
        );

        // Register all your Finapp.Core services
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

        // Build the DI container
        _services = services.BuildServiceProvider();

        Console.WriteLine("✅ Finapp.Core services initialized.");

        // Now boot the Discord bot
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

            // Register the slash command (if not already registered)
            var globalCommand = new SlashCommandBuilder()
                .WithName("car")
                .WithDescription("Car-related commands")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("count")
                    .WithDescription("Get total number of cars")
                    .WithType(ApplicationCommandOptionType.SubCommand));

            try
            {
                await _client.CreateGlobalApplicationCommandAsync(globalCommand.Build());
                Console.WriteLine("✅ Registered /car command globally.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Command registration failed: {ex.Message}");
            }
        };

        // Handle the command when it’s used
        _client.SlashCommandExecuted += HandleSlashCommandAsync;

        await Task.Delay(-1);
    }

    private static async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        if (command.Data.Name == "car")
        {
            var subCommand = command.Data.Options.FirstOrDefault()?.Name;

            if (subCommand == "count")
            {
                await command.DeferAsync(); // optional, shows "thinking..."

                using var scope = _services!.CreateScope();
                var carSvc = scope.ServiceProvider.GetRequiredService<ICarSvc>();
                var count = await carSvc.FetchTotalCount();

                await command.FollowupAsync($"🚗 There are currently **{count}** cars in the Finapp database!");
            }
            else
            {
                await command.RespondAsync("⚠️ Unknown car subcommand.");
            }
        }
    }
}
