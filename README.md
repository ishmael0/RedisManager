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

Invalidation helpers:
```csharp
ctx.AppVersion.ForceToReFetch();
ctx.Users.ForceToReFetch("42");
ctx.Users.ForceToReFetchAll();
ctx.Users.DoPublishAll();
ctx.UserById.ForceToReFetch("42");
ctx.UserById.ForceToReFetchAll();
ctx.UserById.DoPublishAll();
```

---

## API Cheatsheet (most used)

RedisKey<T>
- Construction in context: `public RedisKey<T> SomeKey { get; set; } = new(dbIndex);`
- Write: `Write(T value)` / `Task WriteAsync(T value)`
- Read: `T? Read()` / `Task<T?> ReadAsync()`
- Read full wrapper (timestamps): `RedisDataWrapper<T>? ReadFull()`
- Exists: `bool Exists()`
- Remove: `bool Remove()` / `Task<bool> RemoveAsync()`
- Cache control: `ForceToReFetch()`

RedisHashKey<T>
- Construction: `public RedisHashKey<T> SomeHash { get; set; } = new(dbIndex, serialize?, deSerialize?);`
- Write single: `Write(string field, T value)` / `Task WriteAsync(string field, T value)`
- Write bulk: `Task<bool> WriteAsync(IDictionary<string,T> items, bool forceToPublish = false, int maxChunkSizeInBytes = 1024*128)`
- Read single: `T? Read(string field)` / `Task<T?> ReadAsync(string field)`
- Read multi: `IDictionary<string,T?> Read(IEnumerable<string> fields)`
- Remove: `Task<bool> RemoveAsync(string field)` / multi-field overload
- Remove whole hash: `Task<bool> RemoveAsync()`
- Cache control: `ForceToReFetch(string field)` / `ForceToReFetchAll()`
- Publish all: `DoPublishAll()`

RedisPrefixedKeys<T>
- Construction: `public RedisPrefixedKeys<T> SomeGroup { get; set; } = new(dbIndex);`
- Write single: `Write(string field, T value)` / `Task WriteAsync(string field, T value)`
- Write bulk: `Task<bool> WriteAsync(IDictionary<string,T> items, bool forceToPublish = false)`
- Read single: `T? Read(string field)` / `Task<T?> ReadAsync(string field)`
- Read multi: `IDictionary<string,T> Read(IEnumerable<string> fields)`
- Remove: `Task<bool> RemoveAsync(string field)` / multi-field overload
- Cache control: `ForceToReFetch(string field)` / `ForceToReFetchAll()`
- Publish all: `DoPublishAll()`

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

## Bulk Write Chunking
When writing large dictionaries to a hash, you can pass a `maxChunkSizeInBytes` to split payloads:
```csharp
await ctx.Invoices.WriteAsync(
    items: bigDictionary,
    forceToPublish: false,
    maxChunkSizeInBytes: 256 * 1024);
```
This reduces the chance of timeouts due to oversized operations.

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
- Use `ForceToReFetch(All)` after receiving pub/sub messages to keep caches coherent.
- Prefer async methods for high-throughput paths.
- Consider setting a reasonable `maxChunkSizeInBytes` for very large bulk writes.

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