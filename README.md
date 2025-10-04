# RedisManager

High-level .NET (net9.0) helper around StackExchange.Redis that:

- Auto-initializes strongly-typed `RedisKey<T>` (string keys) and `RedisHashKey<T>` (hash keys) defined as properties on your context class.
- Adds optional in-memory caching per key / hash field.
- Publishes change notifications (Pub/Sub) for individual fields or whole-hash updates.
- Wraps values with metadata (UTC timestamp + Persian date string) using `RedisDataWrapper<T>`.
- Provides batched bulk read/write helpers & size (MEMORY USAGE) inspection.

## Install
(If published as a NuGet package)
```
 dotnet add package RedisManager
```
*(Adjust to actual package id when published.)*

## Core Types

| Type | Purpose |
|------|---------|
| `RedisDBContextModule` | Reflection bootstrapper: finds `RedisKey<>` / `RedisHashKey<>` properties and wires them up. |
| `RedisKey<T>` | Simple string key with optional in-memory cache + publish callback. |
| `RedisHashKey<T>` | Hash key abstraction with per-field cache, bulk ops, size limiting (default 4000 fields). |
| `RedisDataWrapper<T>` | Adds timestamp + Persian formatted date around stored data. |

## Quick Start
```csharp
public class MyRedisContext : RedisDBContextModule
{
    // Will be auto-initialized (Db 0)
    public RedisKey<string> AppConfig { get; set; } = new(0);

    // Hash of user profiles (Db 1)
    public RedisHashKey<UserProfile> Users { get; set; } = new(1);

    public MyRedisContext(ILogger<MyRedisContext> logger,
                          IConnectionMultiplexer writer,
                          IConnectionMultiplexer reader,
                          string env,
                          bool cache = true)
        : base(logger, writer, reader, env, cache, usePushNotification: true) { }
}

// Usage (e.g. in a scoped service)
var ctx = new MyRedisContext(logger, writerMux, readerMux, "Prod", keepDataInMemory: true);

// String key
ctx.AppConfig.Write("v1.2.3");
var version = ctx.AppConfig.Read();

// Hash key write & read
ctx.Users.Write("42", new UserProfile { Id = 42, Name = "Alice" });
var user = ctx.Users.Read("42");

// Bulk write
await ctx.Users.WriteAsync(usersDictionary, forceToPublish: true);

// Paged hash field names (cursor-based approximation)
var (keys, total) = await ctx.GetHashKeysByPage(1, ctx.Users.FullName, pageNumber: 2, pageSize: 25);
```

```csharp
public record UserProfile(int Id, string Name)
{
    public UserProfile() : this(0, "") { }
}
```

## Pub/Sub Notifications
Each key/hash publishes to a channel named after the environment (e.g. `Prod`).
- `RedisKey<T>` publishes: `KeyName`
- `RedisHashKey<T>` publishes: `HashName|field` or `HashName|all` (bulk write)

Subscribe example:
```csharp
var sub = readerMux.GetSubscriber();
await sub.SubscribeAsync("Prod", (channel, message) =>
{
    // Parse messages like: Users|all  or Users|42  or AppConfig
});
```

## Caching Behavior
Set `keepDataInMemory=true` in the context constructor to enable:
- `RedisKey<T>` caches the last wrapper.
- `RedisHashKey<T>` caches individual field wrappers on first read.
Use `ForceToReFetch()` / `ForceToReFetchAll()` to invalidate.

## Size & Limits
- `RedisHashKey<T>` refuses writes if current + incoming fields exceed 4000 (guard). Adjust in source if needed.
- `GetSize()` uses Redis `MEMORY USAGE` (approximate, may require proper permissions / Redis version >= 4). 

## Custom Serialization
Provide lambdas when constructing a key/hash:
```csharp
public RedisHashKey<MyType> Items { get; set; } = new(2,
    serialize: v => JsonConvert.SerializeObject(v, customSettings),
    deSerialize: s => JsonConvert.DeserializeObject<MyType>(s, customSettings)!);
```
You still receive the wrapping metadata via `RedisDataWrapper<T>`.

## Thread Safety Notes
- Hash per-field cache uses `ConcurrentDictionary`.
- Single key cache uses a lock object.
- Bulk write uses chunking (`maxChunkSizeInBytes`) to avoid very large payloads.

## Extending
- Add new `RedisKey<>` / `RedisHashKey<>` properties to your derived context.
- Optionally expose domain-specific helpers around raw read/write calls.

## Testing Concurrency
`RedisHashKey<int>.TestConcurrency_IN_ForceToReFetchAll` stress-tests cache invalidation vs. parallel reads.

## Logging
All operations log errors with the provided `ILogger` to aid diagnostics.

## Roadmap Ideas
- Configurable max hash length.
- Optional compression layer.
- Strongly typed pub/sub eventing helpers.

## License
MIT (add LICENSE file if not present).

---
Generated README based on project source.