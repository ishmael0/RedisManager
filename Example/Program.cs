using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Santel.Redis.TypedKeys;
using StackExchange.Redis;

// Simple console demo for Santel.Redis.TypedKeys
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddConsole()
        .SetMinimumLevel(LogLevel.Information);
});

// Create a typed logger (ILogger<Program>)
var logger = loggerFactory.CreateLogger<AppRedisContext>();
var ctx = new AppRedisContext(ConnectionMultiplexer.Connect("localhost:6379"), true, logger);

// DI setup
//{
//    var services = new ServiceCollection();
//    services.AddLogging();
//    services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
//    services.AddRedisDBContext<AppRedisContext>(
//        keepDataInMemory: true,
//        nameGeneratorStrategy: name => $"Demo_{name}",
//        channelName: "Demo");

//    var sp = services.BuildServiceProvider();
//    var ctx = sp.GetRequiredService<AppRedisContext>();

//}

// Simple key
ctx.AppVersion.Write("1.0.0");
var version = ctx.AppVersion.Read();
Console.WriteLine($"AppVersion = {version}");

// Hash key
await ctx.Users.WriteAsync("42", new UserProfile(42, "Alice"));
var alice = await ctx.Users.ReadAsync("42");
Console.WriteLine($"Users[42] = {alice?.Name}");

// Prefixed keys (stored as "UserById:{id}")
await ctx.UserById.WriteAsync("100", new UserProfile(100, "Bob"));
var bob = await ctx.UserById.ReadAsync("100");
Console.WriteLine($"UserById[100] = {bob?.Name}");

Console.WriteLine("Done.");

// App context and models used in the example
public class AppRedisContext : RedisDBContextModule
{
    public RedisKey<string> AppVersion { get; set; } = new(0);
    public RedisHashKey<UserProfile> Users { get; set; } = new(1);
    public RedisPrefixedKeys<UserProfile> UserById { get; set; } = new(2);

    public AppRedisContext(IConnectionMultiplexer mux,
                           bool keepDataInMemory,
                           ILogger<AppRedisContext> logger,
                           Func<string, string>? nameGeneratorStrategy = null,
                           string? channelName = null)
        : base(mux, keepDataInMemory, logger, nameGeneratorStrategy, channelName)
    {
    }

}

public record UserProfile(int Id, string Name)
{
    public UserProfile() : this(0, string.Empty) { }
}
