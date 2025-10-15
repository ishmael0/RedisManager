# Santel.Redis.TypedKeys

Strongly-typed Redis key & hash abstractions (string key + hash field semantics) with optional in‑memory caching, structured metadata, and lightweight pub/sub notifications. Built on StackExchange.Redis targeting .NET 9.

## Why
Typical Redis usage scatters string constants, serialization logic, caching flags, and pub/sub plumbing across the codebase. This library:
- Centralizes key definitions in a single derived context.
- Auto‑wires all declared `RedisKey<T>` / `RedisHashKey<T>` properties via reflection.
- Adds optional per-key / per-field in-memory caching.
- Adds lightweight publish notifications (single field or bulk) for cache invalidation in other processes.
- Wraps stored payloads with timestamp + Persian date metadata (`RedisDataWrapper<T>`).
- Provides bulk operations, pagination helpers, concurrency helpers, size inspection, and basic safety limits.

## Package Installation
```
dotnet add package Santel.Redis.TypedKeys
```
Or add to a project file:
```xml
<ItemGroup>
  <PackageReference Include="Santel.Redis.TypedKeys" Version="1.0.1" />
</ItemGroup>
```

## Core Types Overview
| Type | Summary |
|------|---------|
| `RedisDBContextModule` | Base class you inherit; discovers and initializes key/hash properties. |
| `RedisKey<T>` | Single Redis string key abstraction (value + metadata + optional cache). |
| `RedisHashKey<T>` | Redis hash abstraction with per-field cache & bulk helpers. |
| `RedisDataWrapper<T>` | Metadata wrapper (UTC `DateTime`, Persian formatted string, `Data`). |
| `IRedisCommonKeyMethods` / `IRedisCommonHashKeyMethods` | Internal capability contracts. |

## Constructors & Key Naming
Two constructor overloads are available:
```csharp
public RedisDBContextModule(
    IConnectionMultiplexer connectionMultiplexerWrite,
    IConnectionMultiplexer connectionMultiplexerRead,
    bool keepDataInMemory,
    ILogger logger,
    string? prefix = null,
    string? channelName = null)

public RedisDBContextModule(
    IConnectionMultiplexer connectionMultiplexer,
    bool keepDataInMemory,
    ILogger logger,
    string? prefix = null,
    string? channelName = null)
```
Notes:
- The single-multiplexer overload uses the same connection for both read and write.
- Key naming: if `prefix` is null/empty → key name = `PropertyName`; otherwise → `${prefix}_{PropertyName}`.
- Pub/Sub: if `channelName` is provided and not empty, publish operations are enabled on that channel; otherwise publishing is disabled.

## Defining a Context
```csharp
public class AppRedisContext : RedisDBContextModule
{
    // Database 0 simple key
    public RedisKey<string> AppVersion { get; set; } = new(0);

    // Database 1: hash of user profiles
    public RedisHashKey<UserProfile> Users { get; set; } = new(1);

    // Database 2: custom serialization example
    public RedisHashKey<Invoice> Invoices { get; set; } = new(2,
        serialize: inv => JsonConvert.SerializeObject(inv, Formatting.None),
        deSerialize: s => JsonConvert.DeserializeObject<Invoice>(s)!);

    public AppRedisContext(IConnectionMultiplexer writer,
                           IConnectionMultiplexer reader,
                           ILogger<AppRedisContext> logger,
                           string? prefix,
                           bool keepDataInMemory = true,
                           string? channelName = null)
        : base(writer, reader, keepDataInMemory, logger, prefix, channelName) { }

    // Or use the single-multiplexer overload
    public AppRedisContext(IConnectionMultiplexer mux,
                           ILogger<AppRedisContext> logger,
                           string? prefix,
                           bool keepDataInMemory = true,
                           string? channelName = null)
        : base(mux, keepDataInMemory, logger, prefix, channelName) { }
}

public record UserProfile(int Id, string Name)
{
    public UserProfile() : this(0, string.Empty) { }
}
public record Invoice(string Id, decimal Amount);
```

## Basic Usage
```csharp
var ctx = new AppRedisContext(writerMux, readerMux, logger, prefix: "Prod", keepDataInMemory: true);

// String key
ctx.AppVersion.Write("1.4.9");
var ver = ctx.AppVersion.Read();

// Hash single entry
ctx.Users.Write("42", new UserProfile(42, "Alice"));
var alice = ctx.Users.Read("42");

// Bulk hash write
await ctx.Users.WriteAsync(new Dictionary<string, UserProfile>
{
    ["1"] = new(1, "Bob"),
    ["2"] = new(2, "Carol")
}, forceToPublish: true); // triggers channel publish 'Users|all'

// Bulk read (cached after first fetch if keepDataInMemory=true)
var some = ctx.Users.Read(new [] { "1", "2" });

// Async field read
var carol = await ctx.Users.ReadAsync("2");
```

## Pub/Sub Model
Channel name = the provided `channelName` constructor parameter. If null/empty, publishing is disabled.
Messages:
- `RedisKey<T>`: `KeyName`
- `RedisHashKey<T>` single field update: `HashName|{field}`
- `RedisHashKey<T>` bulk/forced publish: `HashName|all`

Subscriber example:
```csharp
var sub = readerMux.GetSubscriber();
await sub.SubscribeAsync("Prod", (channel, msg) =>
{
    // Patterns: Users|123  Users|all  AppVersion
    var text = (string)msg;
    if (text.EndsWith("|all"))
    {
        // invalidate all cached fields for that hash locally
    }
    else if (text.Contains('|'))
    {
        var parts = text.Split('|'); // parts[0]=hash, parts[1]=field
    }
    else
    {
        // simple key changed
    }
});
```

## Caching & Invalidation
Enable by passing `keepDataInMemory: true` to the base constructor.
- `RedisKey<T>`: last value wrapper cached.
- `RedisHashKey<T>`: individual field wrappers cached lazily.
Invalidation helpers:
```csharp
ctx.AppVersion.ForceToReFetch();          // drop single key cache
ctx.Users.ForceToReFetch("42");          // drop one field
ctx.Users.ForceToReFetchAll();            // drop all cached fields
ctx.Users.DoPublishAll();                 // manual global publish (Users|all)
```
A pub/sub handler in other processes should call `ForceToReFetch` / `ForceToReFetchAll` accordingly.

## Paging Hash Fields
```csharp
var (fieldNames, total) = await ctx.GetHashKeysByPage(
    database: 1,
    hashKey: ctx.Users.FullName, // underlying redis key
    pageNumber: 2,
    pageSize: 25);
```
Uses cursor-like offset logic with `HashScanAsync`.

## Memory Usage / Size
```csharp
long sizeKey = ctx.AppVersion.GetSize();
long sizeUsers = ctx.Users.GetSize();
```
Uses `MEMORY USAGE` – may return 0 if unsupported by server or lacking permission.

## Hash Length Safety Limit
`RedisHashKey<T>` enforces a soft limit (4000 fields). Bulk or single writes failing the limit log an informational message and return false. Adjust in source (`IsLimitExceeded`).

## Bulk Write Chunking
`WriteAsync(IDictionary<string,T>, maxChunkSizeInBytes)` splits large payloads by serialized byte length to avoid over-large single operations.

## Custom Serialization
You may override serialization per key / hash (shown earlier for `Invoices`). Metadata wrapping is preserved.

## Metadata Wrapper
All stored data is nested inside `RedisDataWrapper<T>`:
```json
{
  "Data": { /* your T */ },
  "DateTime": "2025-01-01T10:12:33.456Z",
  "PersianLastUpdate": "1403/10/11 13:42"
}
```
Access with `ReadFull` / `ReadFull(string key)` when you need timestamps.

## Dependency Injection
A generic extension method is provided to register your derived context with DI:
```csharp
using Santel.Redis.TypedKeys;

services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(config));
services.AddLogging();

services.AddRedisDBContext<AppRedisContext>(
    keepDataInMemory: true,
    prefix: "Prod",
    channelName: "Prod");
```
How it works:
- Resolves `IConnectionMultiplexer` and `ILogger<AppRedisContext>` from DI.
- Tries the single-multiplexer constructor `(IConnectionMultiplexer, bool, ILogger, string?, string?)` first.
- Falls back to the dual-multiplexer constructor `(IConnectionMultiplexer, IConnectionMultiplexer, bool, ILogger, string?, string?)` if present.

You can also register the base `RedisDBContextModule` (rarely useful on its own):
```csharp
services.AddRedisDBContext<RedisDBContextModule>(
    keepDataInMemory: true,
    prefix: "Prod",
    channelName: "Prod");
```

Manual registration remains an option if you need custom wiring:
```csharp
services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(config));
services.AddSingleton<AppRedisContext>(sp =>
{
    var mux = sp.GetRequiredService<IConnectionMultiplexer>();
    var logger = sp.GetRequiredService<ILogger<AppRedisContext>>();
    return new AppRedisContext(mux, mux, logger,
        prefix: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        keepDataInMemory: true,
        channelName: "Prod");
});
```

## Error Handling & Logging
All operations catch and log exceptions with contextual key info using the provided `ILogger`.

## Versioning / Roadmap Ideas
Planned / potential improvements:
- Configurable hash size limit.
- Optional compression (e.g., LZ4) layer.
- Structured pub/sub event model.
- Metrics hooks (latency / miss ratio).

## License
MIT

## Disclaimer
Use responsibly in high cardinality scenarios: the in-memory cache is per-process and unbounded except for the hash field count guard.