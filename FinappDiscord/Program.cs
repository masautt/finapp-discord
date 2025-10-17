using Database;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Repos.Shared;
using Services.Interfaces;
using Services.Tables;
using System;

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
        var token = _config["Discord:Token"] ?? throw new InvalidOperationException("Discord token missing");
        var channelId = ulong.Parse(_config["Discord:ChannelId"] ?? throw new InvalidOperationException("Channel ID missing"));

        _client = new DiscordSocketClient();
        _client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _client.Ready += async () =>
        {
            Console.WriteLine("🤖 Bot is online!");

            if (_client.GetChannel(channelId) is IMessageChannel channel)
            {
                await channel.SendMessageAsync("👋 Hello from FinappDiscord (with Finapp.Core loaded)!");
                Console.WriteLine("✅ Message sent successfully.");

                // Example: resolve a Finapp service and use it
                using var scope = _services!.CreateScope();
                var carSvc = scope.ServiceProvider.GetRequiredService<ICarSvc>();
                var cars = await carSvc.FetchTotalCount();
                await channel.SendMessageAsync($"🚗 There are currently {cars} cars in the Finapp DB!");
            }
            else
            {
                Console.WriteLine("⚠️ Couldn't find channel — check the ID and permissions.");
            }
        };

        await Task.Delay(-1);
    }
}
