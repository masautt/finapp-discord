using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;

namespace FinappDiscord;

internal class Program
{
    private static DiscordSocketClient? _client;

    private static async Task Main()
    {
        // 1️⃣ Load configuration from appsettings.Local.json
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Local.json", optional: false, reloadOnChange: true)
            .Build();

        var token = config["Discord:Token"] ?? throw new InvalidOperationException("Discord token missing");
        var channelId = ulong.Parse(config["Discord:ChannelId"] ?? throw new InvalidOperationException("Channel ID missing"));

        // 2️⃣ Setup and start bot
        _client = new DiscordSocketClient();
        _client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _client.Ready += async () =>
        {
            Console.WriteLine("Bot is online!");

            if (_client.GetChannel(channelId) is IMessageChannel channel)
            {
                await channel.SendMessageAsync("👋 Hello from my CLI Discord bot!");
            }
            else
            {
                Console.WriteLine("Couldn't find channel — check the ID and permissions.");
            }
        };

        await Task.Delay(-1);
    }
}