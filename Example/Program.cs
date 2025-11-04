using Santel.Redis.TypedKeys;
using StackExchange.Redis;

Console.WriteLine("=== Santel.Redis.TypedKeys - Comprehensive Demo ===\n");

// Initialize Redis connection and context
var ctx = new AppRedisContext(new RedisDBContextOptions 
{ 
    ConnectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379") 
});

// DI setup example (commented out - use this approach in production)
//{
//    var services = new ServiceCollection();
//    services.AddLogging();
//    services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
//    services.AddRedisDBContext<AppRedisContext>(
//        keepDataInMemory: true,
//        nameGeneratorStrategy: name => $"Demo_{name}",
//        channelName: "Demo");
//
//    var sp = services.BuildServiceProvider();
//    var ctx = sp.GetRequiredService<AppRedisContext>();
//}

// ============================================
// 1. RedisKey<T> Examples - Single key/value storage
// ============================================
Console.WriteLine("--- 1. RedisKey<T> Examples ---");

// Basic read/write
ctx.AppVersion.Write("1.0.0");
var version = ctx.AppVersion.Read();
Console.WriteLine($"AppVersion: {version}");

// Async operations
await ctx.AppVersion.WriteAsync("1.1.0");
version = await ctx.AppVersion.ReadAsync();
Console.WriteLine($"AppVersion (updated): {version}");

// Force refresh from Redis (bypassing cache)
var forcedVersion = ctx.AppVersion.Read(force: true);
Console.WriteLine($"AppVersion (forced): {forcedVersion}");

// Get memory size
var versionSize = ctx.AppVersion.GetSize();
Console.WriteLine($"AppVersion size in Redis: {versionSize} bytes");

// Invalidate cache
ctx.AppVersion.InvalidateCache();
Console.WriteLine("AppVersion cache invalidated\n");

// ============================================
// 2. RedisHashKey<T> Examples - Hash storage
// ============================================
Console.WriteLine("--- 2. RedisHashKey<T> Examples ---");

// Single item operations
await ctx.Users.WriteAsync("1", new UserProfile(1, "Alice", "alice@example.com"));
await ctx.Users.WriteAsync("2", new UserProfile(2, "Bob", "bob@example.com"));
await ctx.Users.WriteAsync("3", new UserProfile(3, "Charlie", "charlie@example.com"));

var alice = await ctx.Users.ReadAsync("1");
Console.WriteLine($"User[1]: {alice?.Name} ({alice?.Email})");

// Index-based access
var bob = ctx.Users["2"];
Console.WriteLine($"User[2] via indexer: {bob?.Name}");

// Bulk read operations
var userIds = new[] { "1", "2", "3" };
var users = await ctx.Users.ReadAsync(userIds);
Console.WriteLine($"Bulk read: {users?.Count} users loaded");
foreach (var (id, user) in users ?? new Dictionary<string, UserProfile>())
{
    Console.WriteLine($"  - {id}: {user.Name}");
}

// Bulk write operations
var newUsers = new Dictionary<string, UserProfile>
{
    ["4"] = new UserProfile(4, "David", "david@example.com"),
    ["5"] = new UserProfile(5, "Eve", "eve@example.com"),
    ["6"] = new UserProfile(6, "Frank", "frank@example.com")
};
await ctx.Users.WriteAsync(newUsers);
Console.WriteLine($"Bulk write: {newUsers.Count} users added");

// Get all keys in hash
var allUserKeys = await ctx.Users.GetAllKeysAsync();
Console.WriteLine($"Total users in hash: {allUserKeys.Length}");

// Chunked operations for large datasets
var manyUsers = new Dictionary<string, UserProfile>();
for (var i = 100; i < 10100; i++)
{
    manyUsers[$"{i}"] = new UserProfile(i, $"User{i}", $"user{i}@example.com");
}

// Write in chunks (default 1000 per chunk)
await ctx.Users.WriteInChunksAsync(manyUsers, chunkSize: 500);
Console.WriteLine($"Wrote {manyUsers.Count} users in chunks of 500");

// Read in chunks
var manyUserIds = Enumerable.Range(100, 10000).Select(i => i.ToString()).ToList();
var loadedUsers = await ctx.Users.ReadInChunksAsync(manyUserIds, chunkSize: 500);
Console.WriteLine($"Read {loadedUsers?.Count} users in chunks");

// Remove operations
await ctx.Users.RemoveAsync("6");
Console.WriteLine("Removed user 6");

// Bulk remove
var idsToRemove = new[] { "4", "5" };
await ctx.Users.RemoveAsync(idsToRemove);
Console.WriteLine($"Removed {idsToRemove.Length} users");

// Remove in chunks
var chunkedRemoveIds = Enumerable.Range(100, 10000).Select(i => i.ToString());
await ctx.Users.RemoveInChunksAsync(chunkedRemoveIds, chunkSize: 500);
Console.WriteLine("Removed 10000 users in chunks");

// Cache invalidation
ctx.Users.InvalidateCache("1");
Console.WriteLine("Invalidated cache for user 1");

ctx.Users.InvalidateCache(new[] { "2", "3" });
Console.WriteLine("Invalidated cache for users 2 and 3");

ctx.Users.InvalidateCache();
Console.WriteLine("Invalidated entire Users cache");

// Get memory size
var usersSize = ctx.Users.GetSize();
Console.WriteLine($"Users hash size in Redis: {usersSize} bytes\n");

// ============================================
// 3. RedisPrefixedKeys<T> Examples - Prefixed key storage
// ============================================
Console.WriteLine("--- 3. RedisPrefixedKeys<T> Examples ---");

// Single item operations (stored as "UserSettings:user1" etc.)
await ctx.UserSettings.WriteAsync("user1", new UserSettings("user1", "dark", "en-US"));
await ctx.UserSettings.WriteAsync("user2", new UserSettings("user2", "light", "fr-FR"));
await ctx.UserSettings.WriteAsync("user3", new UserSettings("user3", "auto", "de-DE"));

var user1Settings = await ctx.UserSettings.ReadAsync("user1");
Console.WriteLine($"UserSettings[user1]: Theme={user1Settings?.Theme}, Language={user1Settings?.Language}");

// Bulk operations
var settingsToWrite = new Dictionary<string, UserSettings>
{
    ["user4"] = new UserSettings("user4", "dark", "es-ES"),
    ["user5"] = new UserSettings("user5", "light", "it-IT"),
    ["user6"] = new UserSettings("user6", "dark", "pt-BR")
};
await ctx.UserSettings.WriteAsync(settingsToWrite);
Console.WriteLine($"Bulk write: {settingsToWrite.Count} settings added");

// Read multiple
var settingsKeys = new[] { "user1", "user2", "user3" };
var loadedSettings = await ctx.UserSettings.ReadAsync(settingsKeys);
Console.WriteLine($"Bulk read: {loadedSettings?.Count} settings loaded");

// Chunked write for large datasets
var manySettings = new Dictionary<string, UserSettings>();
for (var i = 1000; i < 11000; i++)
{
    manySettings[$"user{i}"] = new UserSettings($"user{i}", "dark", "en-US");
}
await ctx.UserSettings.WriteInChunksAsync(manySettings, chunkSize: 500);
Console.WriteLine($"Wrote {manySettings.Count} settings in chunks");

// Read in chunks
var manySettingsKeys = Enumerable.Range(1000, 10000).Select(i => $"user{i}").ToList();
var loadedChunkedSettings = await ctx.UserSettings.ReadInChunksAsync(manySettingsKeys, chunkSize: 500);
Console.WriteLine($"Read {loadedChunkedSettings?.Count} settings in chunks");

// Remove operations
await ctx.UserSettings.RemoveAsync("user6");
Console.WriteLine("Removed settings for user6");

// Bulk remove
await ctx.UserSettings.RemoveAsync(new[] { "user4", "user5" });
Console.WriteLine("Removed settings for user4 and user5");

// Remove in chunks
var chunkedRemoveSettings = Enumerable.Range(1000, 10000).Select(i => $"user{i}");
await ctx.UserSettings.RemoveInChunksAsync(chunkedRemoveSettings, chunkSize: 500);
Console.WriteLine("Removed 10000 settings in chunks");

// Cache invalidation
ctx.UserSettings.InvalidateCache("user1");
ctx.UserSettings.InvalidateCache(new[] { "user2", "user3" });
ctx.UserSettings.InvalidateCache();
Console.WriteLine("Cache invalidation operations completed");

// Get total memory size (scans all prefixed keys)
var settingsSize = ctx.UserSettings.GetSize();
Console.WriteLine($"UserSettings total size in Redis: {settingsSize} bytes\n");

// ============================================
// 4. Advanced Scenarios
// ============================================
Console.WriteLine("--- 4. Advanced Scenarios ---");

// Scenario: User session management with TTL
var sessionData = new Dictionary<string, UserProfile>
{
    ["session_abc123"] = new UserProfile(1, "Alice", "alice@example.com"),
    ["session_def456"] = new UserProfile(2, "Bob", "bob@example.com")
};
await ctx.Sessions.WriteAsync(sessionData);
Console.WriteLine("Session data written");

// Scenario: Complex object with custom serialization
await ctx.ComplexData.WriteAsync("config1", new ComplexConfig
{
    Name = "ProductionConfig",
    Settings = new Dictionary<string, string>
    {
        ["MaxConnections"] = "100",
        ["Timeout"] = "30"
    },
    IsEnabled = true
});
var config = await ctx.ComplexData.ReadAsync("config1");
Console.WriteLine($"Complex config loaded: {config?.Name}, Settings count: {config?.Settings?.Count}");

// Scenario: Working with force refresh to bypass cache
ctx.AppVersion.Write("2.0.0");
var cachedVersion = ctx.AppVersion.Read(); // From cache
var freshVersion = ctx.AppVersion.Read(force: true); // From Redis
Console.WriteLine($"Cached: {cachedVersion}, Fresh: {freshVersion}");

// Scenario: Monitoring memory usage across different key types
Console.WriteLine("\n--- Memory Usage Summary ---");
Console.WriteLine($"AppVersion (RedisKey): {ctx.AppVersion.GetSize()} bytes");
Console.WriteLine($"Users (RedisHashKey): {ctx.Users.GetSize()} bytes");
Console.WriteLine($"UserSettings (RedisPrefixedKeys): {ctx.UserSettings.GetSize()} bytes");
Console.WriteLine($"Sessions (RedisHashKey): {ctx.Sessions.GetSize()} bytes");
Console.WriteLine($"ComplexData (RedisPrefixedKeys): {ctx.ComplexData.GetSize()} bytes");

Console.WriteLine("\n=== Demo Complete ===");

// ============================================
// App Context and Models
// ============================================

/// <summary>
/// Redis context defining all Redis keys used in the application.
/// Each property represents a different Redis key or key pattern.
/// </summary>
public class AppRedisContext : RedisDBContextModule
{
    // Simple key-value storage (stored as single Redis key)
    public RedisKey<string> AppVersion { get; set; } = new(0);
    
    // Hash storage - all users in one Redis hash (DB index 1)
    public RedisHashKey<UserProfile> Users { get; set; } = new(1);
    
    // Prefixed keys - each setting stored as separate key with prefix (DB index 2)
    public RedisPrefixedKeys<UserSettings> UserSettings { get; set; } = new(2);
    
    // Additional examples
    public RedisHashKey<UserProfile> Sessions { get; set; } = new(1);
    public RedisPrefixedKeys<ComplexConfig> ComplexData { get; set; } = new(3);

    public AppRedisContext(RedisDBContextOptions opts) : base(opts)
    {
    }
}

/// <summary>
/// User profile model with basic information.
/// </summary>
public record UserProfile(int Id, string Name, string Email)
{
    public UserProfile() : this(0, string.Empty, string.Empty) { }
}

/// <summary>
/// User settings model for preferences.
/// </summary>
public record UserSettings(string UserId, string Theme, string Language)
{
    public UserSettings() : this(string.Empty, "light", "en-US") { }
}

/// <summary>
/// Complex configuration object demonstrating nested data storage.
/// </summary>
public class ComplexConfig
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = new();
    public bool IsEnabled { get; set; }
}
