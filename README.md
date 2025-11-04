# Santel.Redis.TypedKeys

Typed, discoverable Redis keys for .NET 9. Focus on developer ergonomics: concise key definitions, optional in‑memory caching, and lightweight pub/sub notifications – all on top of StackExchange.Redis.

- .NET: 9
- Redis client: StackExchange.Redis 2.x
- Package: Santel.Redis.TypedKeys

## Highlights
- Strongly-typed wrappers for simple keys and hash maps: `RedisKey<T>`, `RedisHashKey<T>`
- Prefixed string keys stored as separate keys: `RedisPrefixedKeys<T>` (format: `FullName:field`)
- One central context (`RedisDBContextModule`) where you declare all keys
- Optional per-key/per-field in-memory cache with easy invalidation
- Built-in lightweight pub/sub notifications for cross-process cache invalidation
- Opt-in custom serialization per key
- Pluggable key naming via `nameGeneratorStrategy` delegate
- **NEW**: Chunked operations for large datasets (`ReadInChunks`, `WriteInChunks`, `RemoveInChunks`)
- **NEW**: Memory usage tracking with `GetSize()` method for all key types
- **NEW**: Enhanced cache invalidation methods (single, bulk, and full)
- Helpers: hash paging, DB size, bulk write with chunking, soft safety limits

## Install
```
dotnet add package Santel.Redis.TypedKeys
```

## Requirements
- .NET 9
- A running Redis server

---

## Quick Start

1) Define your context (a class inheriting `RedisDBContextModule`) and declare your keys. You can omit constructors entirely – DI will initialize the context automatically:
```csharp
using Santel.Redis.TypedKeys;

public class AppRedisContext : RedisDBContextModule
{
    public RedisKey<string> AppVersion { get; set; } = new(0);
    public RedisHashKey<UserProfile> Users { get; set; } = new(1);
    public RedisHashKey<Invoice> Invoices { get; set; } = new(2);
    public RedisPrefixedKeys<UserProfile> UserById { get; set; } = new(3);
}

public record UserProfile(int Id, string Name);
public record Invoice(string Id, decimal Amount);
```

Note: Do not call any Init methods. `RedisDBContextModule` automatically initializes all declared `RedisKey<T>`, `RedisHashKey<T>`, and `RedisPrefixedKeys<T>` via reflection when the context instance is constructed by DI.

2) Register with DI
```csharp
using Microsoft.Extensions.DependencyInjection;
using Santel.Redis.TypedKeys;
using StackExchange.Redis;

var services = new ServiceCollection();
services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddLogging();

// Registers your derived context via generic extension.
services.AddRedisDBContext<AppRedisContext>(
    keepDataInMemory: true,
    nameGeneratorStrategy: name => $"Prod_{name}",
    channelName: "Prod");
```

3) Use it
```csharp
var sp = services.BuildServiceProvider();
var ctx = sp.GetRequiredService<AppRedisContext>();

ctx.AppVersion.Write("1.5.0");
var version = ctx.AppVersion.Read();

ctx.Users.Write("42", new UserProfile(42, "Alice"));
var alice = ctx.Users.Read("42");

await ctx.UserById.WriteAsync("42", new UserProfile(42, "Alice"));
var byId = await ctx.UserById.ReadAsync("42");
```

---

## Key Naming & Pub/Sub
- Naming: by default, key name = `PropertyName`.
- If you supply `nameGeneratorStrategy`, it receives `PropertyName` and returns the final Redis key name.
  - Examples:
    - Prefix per environment: `name => $"Prod_{name}"`
    - Kebab-case: `name => Regex.Replace(name, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant()`
    - Tenant-scoped: `name => $"{tenantId}:{name}`
- Publish channel: controlled by `channelName`
  - `RedisKey<T>` publish payload: `KeyName`
  - `RedisHashKey<T>` publish field: `HashName|{field}`
  - `RedisHashKey<T>` publish-all: `HashName|all`
  - `RedisPrefixedKeys<T>` follows the same pattern as hash

Subscribe example:
```csharp
var sub = readerMux.GetSubscriber();
await sub.SubscribeAsync("Prod", (ch, msg) =>
{
    var text = (string)msg;
    if (text.EndsWith("|all"))
    {
        // Invalidate entire cache for that name (hash or prefixed)
    }
    else if (text.Contains('|'))
    {
        var parts = text.Split('|'); // parts[0] = name, parts[1] = field
        // Invalidate a single field cache
    }
    else
    {
        // Simple key invalidation
    }
});
```

Note: Publishing is performed via the write multiplexer; subscribing can use the read multiplexer.

---

## Ctors and DI
You can optionally define your own constructors in the derived context (e.g., to do extra wiring), but it is not required. The DI extension supports automatic initialization and will provide the connections and options. No manual Init calls are needed.

Previously documented constructor overloads are still supported when present on your derived context:
1) `(IConnectionMultiplexer mux, bool keepDataInMemory, ILogger logger, Func<string,string>? nameGeneratorStrategy, string? channelName)`
2) `(IConnectionMultiplexer write, IConnectionMultiplexer read, bool keepDataInMemory, ILogger logger, Func<string,string>? nameGeneratorStrategy, string? channelName)`

If you omit constructors, the base parameterless ctor is used and the context is initialized by the framework during activation.

---

## Caching & Invalidation
- `RedisKey<T>`: caches the last `RedisDataWrapper<T>` read or written
- `RedisHashKey<T>`: caches individual field wrappers on-demand
- `RedisPrefixedKeys<T>`: caches individual field wrappers on-demand

Invalidation methods:
```csharp
// RedisKey<T>
ctx.AppVersion.InvalidateCache();          // Clear cache for the key

// RedisHashKey<T>
ctx.Users.InvalidateCache("42");           // Clear cache for single field
ctx.Users.InvalidateCache(new[] {"1","2"}); // Clear cache for multiple fields
ctx.Users.InvalidateCache();                // Clear entire hash cache

// RedisPrefixedKeys<T>
ctx.UserById.InvalidateCache("42");
ctx.UserById.InvalidateCache(new[] {"1","2"});
ctx.UserById.InvalidateCache();

// Legacy methods (still supported)
ctx.AppVersion.ForceToReFetch();
ctx.Users.ForceToReFetch("42");
ctx.Users.ForceToReFetchAll();
```

---

## API Cheatsheet (most used)

RedisKey<T>
- Construction in context: `public RedisKey<T> SomeKey { get; set; } = new(dbIndex);`
- Write: `Write(T value)` / `Task WriteAsync(T value)`
- Read: `T? Read(bool force = false)` / `Task<T?> ReadAsync(bool force = false)`
- Read full wrapper (timestamps): `RedisDataWrapper<T>? ReadFull()`
- Exists: `bool Exists()`
- Remove: `bool Remove()` / `Task<bool> RemoveAsync()`
- **Memory size**: `long GetSize()` - Returns memory usage in bytes
- Cache control: `InvalidateCache()` / `ForceToReFetch()`

RedisHashKey<T>
- Construction: `public RedisHashKey<T> SomeHash { get; set; } = new(dbIndex, serialize?, deSerialize?);`
- Write single: `Write(string field, T value)` / `Task<bool> WriteAsync(string field, T value)`
- Write bulk: `Write(IDictionary<string,T> data)` / `Task<bool> WriteAsync(IDictionary<string,T> data)`
- **Write chunked**: `WriteInChunks(IDictionary<string,T> data, int chunkSize = 1000)` / `Task<bool> WriteInChunksAsync(...)`
- Read single: `T? Read(string field, bool force = false)` / `Task<T?> ReadAsync(string field, bool force = false)`
- Read multi: `Dictionary<string,T>? Read(IEnumerable<string> fields, bool force = false)` / async variant
- **Read chunked**: `ReadInChunks(IEnumerable<string> keys, int chunkSize = 1000, bool force = false)` / async variant
- **Get all keys**: `RedisValue[] GetAllKeys()` / `Task<RedisValue[]> GetAllKeysAsync()`
- Remove: `Remove(string key)` / `RemoveAsync(string key)` / multi-field overload
- **Remove chunked**: `RemoveInChunks(IEnumerable<string> keys, int chunkSize = 1000)` / async variant
- Remove whole hash: `Task<bool> RemoveAsync()`
- **Memory size**: `long GetSize()` - Returns hash memory usage in bytes
- **Cache control**: `InvalidateCache(string key)` / `InvalidateCache(IEnumerable<string> keys)` / `InvalidateCache()`
- **Indexer**: `T? this[string key]` - Read via indexer syntax

RedisPrefixedKeys<T>
- Construction: `public RedisPrefixedKeys<T> SomeGroup { get; set; } = new(dbIndex);`
- Write single: `Write(string field, T value)` / `Task<bool> WriteAsync(string field, T value)`
- Write bulk: `Write(IDictionary<string,T> data)` / `Task<bool> WriteAsync(IDictionary<string,T> data)`
- **Write chunked**: `WriteInChunks(IDictionary<string,T> data, int chunkSize = 1000)` / async variant
- Read single: `T? Read(string field, bool force = false)` / `Task<T?> ReadAsync(string field, bool force = false)`
- Read multi: `Dictionary<string,T>? Read(IEnumerable<string> fields, bool force = false)` / async variant
- **Read chunked**: `ReadInChunks(IEnumerable<string> keys, int chunkSize = 1000, bool force = false)` / async variant
- Remove: `Remove(string key)` / `RemoveAsync(string key)` / multi-field overload
- **Remove chunked**: `RemoveInChunks(IEnumerable<string> keys, int chunkSize = 1000)` / async variant
- **Memory size**: `long GetSize()` - Returns total memory usage of all prefixed keys in bytes (uses SCAN)
- **Cache control**: `InvalidateCache(string key)` / `InvalidateCache(IEnumerable<string> keys)` / `InvalidateCache()`

Context helpers
- `Task<long> GetDbSize(int database)`
- `Task<(List<string>? Keys, long Total)> GetHashKeysByPage(int database, string hashKey, int pageNumber = 1, int pageSize = 10)`
- `Task<string?> GetValues(int database, string key)` (reads raw string value for a simple key)

---

## Paging Example (Hash fields)
```csharp
var (fields, total) = await ctx.GetHashKeysByPage(
    database: 1,
    hashKey: ctx.Users.FullName, // underlying redis key
    pageNumber: 2,
    pageSize: 25);
```

---

## Chunked Operations (NEW)
When working with large datasets, use chunked methods to avoid blocking Redis and prevent timeouts:

### Write in chunks
```csharp
var manyUsers = new Dictionary<string, UserProfile>();
for (int i = 0; i < 10000; i++)
    manyUsers[$"{i}"] = new UserProfile(i, $"User{i}", $"user{i}@example.com");

// Hash: Write in chunks of 500
await ctx.Users.WriteInChunksAsync(manyUsers, chunkSize: 500);

// Prefixed keys: Write in chunks
await ctx.UserSettings.WriteInChunksAsync(manyUsers, chunkSize: 500);
```

### Read in chunks
```csharp
var userIds = Enumerable.Range(0, 10000).Select(i => i.ToString()).ToList();

// Read 10,000 users in chunks of 500
var users = await ctx.Users.ReadInChunksAsync(userIds, chunkSize: 500);
Console.WriteLine($"Loaded {users?.Count} users");
```

### Remove in chunks
```csharp
var idsToRemove = Enumerable.Range(0, 10000).Select(i => i.ToString());

// Remove in chunks
await ctx.Users.RemoveInChunksAsync(idsToRemove, chunkSize: 500);
```

**Benefits:**
- Prevents Redis from blocking on large operations
- Reduces memory pressure
- Avoids network timeouts
- Production-safe for datasets with thousands of items

---

## Memory Usage Tracking (NEW)
Track Redis memory usage for monitoring and optimization:

```csharp
// Get size of a simple key
var versionSize = ctx.AppVersion.GetSize();
Console.WriteLine($"AppVersion: {versionSize} bytes");

// Get size of an entire hash
var usersSize = ctx.Users.GetSize();
Console.WriteLine($"Users hash: {usersSize} bytes");

// Get total size of all prefixed keys (uses SCAN - production safe)
var settingsSize = ctx.UserSettings.GetSize();
Console.WriteLine($"All user settings: {settingsSize} bytes");

// Monitor all keys
Console.WriteLine("Memory Usage Summary:");
Console.WriteLine($"  AppVersion: {ctx.AppVersion.GetSize()} bytes");
Console.WriteLine($"  Users: {ctx.Users.GetSize()} bytes");
Console.WriteLine($"  UserSettings: {ctx.UserSettings.GetSize()} bytes");
```

**Notes:**
- Uses Redis `MEMORY USAGE` command
- Returns `0` if command is not supported or disabled
- For `RedisPrefixedKeys`, scans all matching keys using production-safe SCAN (not KEYS)
- Useful for monitoring, capacity planning, and cost optimization

---

## Custom Serialization
You can override serialization per key to integrate any serializer. The library always wraps your data inside `RedisDataWrapper<T>` for timestamps/metadata.
```csharp
public RedisHashKey<Invoice> Invoices { get; set; } = new(2,
    serialize: inv => JsonSerializer.Serialize(inv),
    deSerialize: s => JsonSerializer.Deserialize<Invoice>(s)!);
```

---

## Dependency Injection
A generic DI extension is provided:
```csharp
services.AddRedisDBContext<AppRedisContext>(
    keepDataInMemory: true,
    nameGeneratorStrategy: name => $"Prod_{name}",  // becomes final Redis key (e.g., Prod_Users)
    channelName: "Prod");                            // pub/sub channel name (omit/empty to disable publishing)
```
The factory tries these constructors in order:
1) `(IConnectionMultiplexer mux, bool keepDataInMemory, ILogger logger, Func<string,string>? nameGeneratorStrategy, string? channelName)`
2) `(IConnectionMultiplexer write, IConnectionMultiplexer read, bool keepDataInMemory, ILogger logger, Func<string,string>? nameGeneratorStrategy, string? channelName)`

---

## Best Practices
- Use a separate read multiplexer pointing at a replica if you have heavy read traffic.
- Keep `channelName` consistent per environment/tenant to avoid cross-talk.
- Use `InvalidateCache()` methods after receiving pub/sub messages to keep caches coherent.
- Prefer async methods for high-throughput paths.
- **Use chunked operations** (`ReadInChunks`, `WriteInChunks`, `RemoveInChunks`) for datasets with 1000+ items.
- **Monitor memory usage** with `GetSize()` for capacity planning and cost optimization.
- Set appropriate `chunkSize` based on your data size (default 1000 is good for most cases).
- For `force` parameter: use `true` to bypass cache and always read from Redis.

---

## Troubleshooting
- No pub/sub events? Ensure `channelName` was provided and the publisher uses the write connection.
- Seeing stale data? Verify `keepDataInMemory` settings and that your subscribers invalidate caches.
- Timeouts on bulk writes? Lower `maxChunkSizeInBytes`.
- DB size returns 0? Some Redis providers disable commands (e.g., `DBSIZE`).

---

## Versioning
- Target framework: .NET 9
- Redis client: StackExchange.Redis 2.7.x

---

## License
MIT

## Contributing
Issues and PRs are welcome.

---

## TODO
- Auto-invalidate cache when you receive publish messages
- Add TTL (Time-To-Live) support for `RedisPrefixedKeys<T>`
- Custome DataWrapper options (e.g., include/exclude timestamps)