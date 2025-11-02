# Santel.Redis.TypedKeys (فارسی ساده)

کلیدهای Redis به‌صورت تایپی برای .NET 9. هدفش اینه کار با key/hash راحت‌تر بشه: تعریف ساده، cache داخل حافظه، و pub/sub سبک. همه‌چی روی StackExchange.Redis پیاده‌سازی شده.

- .NET: 9
- Redis client: StackExchange.Redis 2.x
- Package: Santel.Redis.TypedKeys

[English README](./README.md)

## چی داره برات؟
- `RedisKey<T>` و `RedisHashKey<T>` برای کار با key و hash به‌صورت تایپی
- کلیدهای رشته‌ای با پیشوند ثابت به‌صورت جداگانه: `RedisPrefixedKeys<T>` (فرمت ذخیره: `FullName:field`)
- یه context مرکزی (`RedisDBContextModule`) که توش همهٔ keyها رو تعریف می‌کنی
- cache اختیاری برای key و fieldها (حافظهٔ داخل برنامه)
- pub/sub سبک برای invalidation بین چند پروسه
- امکان serialization سفارشی برای هر key
- naming قابل‌سفارشی‌سازی از طریق `nameGeneratorStrategy`
- **جدید**: عملیات‌های تکه‌ای (chunked) برای دیتاست‌های بزرگ (`ReadInChunks`, `WriteInChunks`, `RemoveInChunks`)
- **جدید**: ردیابی مصرف حافظه با متد `GetSize()` برای همه‌ی انواع key
- **جدید**: متدهای پیشرفته‌تر برای پاک‌سازی cache (تک، چندتایی، کامل)
- ابزارهای آماده: paging برای hash، گرفتن DB size، bulk write با chunk کردن، و چند محدودیت نرم برای ایمنی

## نصب
```
dotnet add package Santel.Redis.TypedKeys
```

## پیش‌نیاز
- .NET 9
- یه سرور Redis که بالا باشه

---

## شروع سریع

1) اول context خودت رو بساز و keyها رو تعریف کن:
```csharp
using Newtonsoft.Json;
using Santel.Redis.TypedKeys;
using StackExchange.Redis;

public class AppRedisContext : RedisDBContextModule
{
    public RedisKey<string> AppVersion { get; set; } = new(0);
    public RedisHashKey<UserProfile> Users { get; set; } = new(1);
    public RedisHashKey<Invoice> Invoices { get; set; } = new(2,
        serialize: inv => JsonConvert.SerializeObject(inv, Formatting.None),
        deSerialize: s => JsonConvert.DeserializeObject<Invoice>(s)!);

    // DB 3: کلیدهای رشته‌ای با پیشوند (FullName:field)
    public RedisPrefixedKeys<UserProfile> UserById { get; set; } = new(3);

    public AppRedisContext(IConnectionMultiplexer writer,
                           IConnectionMultiplexer reader,
                           ILogger<AppRedisContext> logger,
                           Func<string, string>? nameGeneratorStrategy = null,
                           bool keepDataInMemory = true,
                           string? channelName = null)
        : base(writer, reader, keepDataInMemory, logger, nameGeneratorStrategy, channelName)
    {
        // Init یک‌خطی برای prefixed (بدون بلاک جداگانه)
        UserById.Init(
            logger,
            writer,
            reader,
            () => Sub?.Publish(Channel, $"{nameof(UserById)}|all"),
            field => Sub?.Publish(Channel, $"{nameof(UserById)}|{field}"),
            new RedisKey(nameGeneratorStrategy?.Invoke(nameof(UserById)) ?? nameof(UserById)),
            keepDataInMemory);
    }

    public AppRedisContext(IConnectionMultiplexer mux,
                           ILogger<AppRedisContext> logger,
                           Func<string, string>? nameGeneratorStrategy = null,
                           bool keepDataInMemory = true,
                           string? channelName = null)
        : base(mux, keepDataInMemory, logger, nameGeneratorStrategy, channelName)
    {
        UserById.Init(
            logger,
            mux,
            mux,
            () => Sub?.Publish(Channel, $"{nameof(UserById)}|all"),
            field => Sub?.Publish(Channel, $"{nameof(UserById)}|{field}"),
            new RedisKey(nameGeneratorStrategy?.Invoke(nameof(UserById)) ?? nameof(UserById)),
            keepDataInMemory);
    }
}

public record UserProfile(int Id, string Name)
{
    public UserProfile() : this(0, string.Empty) { }
}
public record Invoice(string Id, decimal Amount);
```

2) DI
```csharp
using Microsoft.Extensions.DependencyInjection;
using Santel.Redis.TypedKeys;
using StackExchange.Redis;

var services = new ServiceCollection();
services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddLogging();

services.AddRedisDBContext<AppRedisContext>(
    keepDataInMemory: true,
    nameGeneratorStrategy: name => $"Prod_{name}",
    channelName: "Prod");
```

3) استفاده
```csharp
var sp = services.BuildServiceProvider();
var ctx = sp.GetRequiredService<AppRedisContext>();

ctx.AppVersion.Write("1.5.0");
string? version = ctx.AppVersion.Read();

ctx.Users.Write("42", new UserProfile(42, "Alice", "alice@example.com"));
var alice = ctx.Users.Read("42");

// دسترسی از طریق indexer
var bob = ctx.Users["42"];

// Prefixed: ذخیره به شکل "UserById:42"
await ctx.UserById.WriteAsync("42", new UserProfile(42, "Alice", "alice@example.com"));
var u = await ctx.UserById.ReadAsync("42");
await ctx.UserById.RemoveAsync("42");

// نوشتن چندتایی
await ctx.Users.WriteAsync(new Dictionary<string, UserProfile>
{
    ["1"] = new(1, "Bob", "bob@example.com"),
    ["2"] = new(2, "Carol", "carol@example.com")
});

// خواندن چندتایی
var batch = ctx.Users.Read(new[] { "1", "2" });

// عملیات تکه‌ای برای دیتاست بزرگ (10000 رکورد)
var manyUsers = new Dictionary<string, UserProfile>();
for (int i = 0; i < 10000; i++)
    manyUsers[$"{i}"] = new UserProfile(i, $"User{i}", $"user{i}@example.com");

await ctx.Users.WriteInChunksAsync(manyUsers, chunkSize: 500);
Console.WriteLine("10000 کاربر در تکه‌های 500 تایی نوشته شد");

// بررسی مصرف حافظه
var size = ctx.Users.GetSize();
Console.WriteLine($"حجم Users در Redis: {size} بایت");
```

---

## نام‌گذاری key و Pub/Sub
- نام‌گذاری: پیش‌فرض برابر با `PropertyName` هست.
- اگه `nameGeneratorStrategy` بدی، بهش `PropertyName` پاس می‌شه و اسم نهایی key رو برمی‌گردونه.
  - مثال‌ها:
    - محیطی: `name => $"Prod_{name}"`
    - kebab-case: `name => Regex.Replace(name, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant()`
    - tenant: `name => $"{tenantId}:{name}"`
- channel برای publish با `channelName` مشخص میشه.
  - `RedisKey<T>`: `KeyName`
  - `RedisHashKey<T>` (field): `HashName|{field}`
  - `RedisHashKey<T>` (publish-all): `HashName|all`
  - `RedisPrefixedKeys<T>` هم از همین الگو تبعیت می‌کند.

نمونه subscribe:
```csharp
var sub = readerMux.GetSubscriber();
await sub.SubscribeAsync("Prod", (ch, msg) =>
{
    var text = (string)msg;
    if (text.EndsWith("|all"))
    {
        // کل cache همان نام (hash یا prefixed) را خالی کن
    }
    else if (text.Contains('|'))
    {
        var parts = text.Split('|'); // parts[0] = name، parts[1] = field
        // cache همان field را خالی کن
    }
    else
    {
        // key ساده تغییر کرد؛ cacheش را خالی کن
    }
});
```

نکته: publish با write multiplexer انجام میشه؛ subscribe می‌تونه با read multiplexer باشه.

---

## Cache و invalidate
- با `keepDataInMemory` روشن/خاموشش کن.
- `RedisKey<T>`: آخرین مقدار (داخل `RedisDataWrapper<T>`) cache میشه
- `RedisHashKey<T>`: هر field جداگانه cache میشه
- `RedisPrefixedKeys<T>`: هر field جداگانه cache میشه

متدهای invalidate:
```csharp
// RedisKey<T>
ctx.AppVersion.InvalidateCache();          // پاک‌سازی cache برای key

// RedisHashKey<T>
ctx.Users.InvalidateCache("42");           // پاک‌سازی cache یک field
ctx.Users.InvalidateCache(new[] {"1","2"}); // پاک‌سازی چند field
ctx.Users.InvalidateCache();                // پاک‌سازی کل cache

// RedisPrefixedKeys<T>
ctx.UserById.InvalidateCache("42");
ctx.UserById.InvalidateCache(new[] {"1","2"});
ctx.UserById.InvalidateCache();

// متدهای قدیمی (هنوز پشتیبانی می‌شن)
ctx.AppVersion.ForceToReFetch();
ctx.Users.ForceToReFetch("42");
ctx.Users.ForceToReFetchAll();
```

---

## API Cheatsheet

RedisKey<T>
- تعریف: `public RedisKey<T> SomeKey { get; set; } = new(dbIndex);`
- نوشتن: `Write(T value)` / `Task WriteAsync(T value)`
- خوندن: `T? Read(bool force = false)` / `Task<T?> ReadAsync(bool force = false)`
- خوندن wrapper کامل: `RedisDataWrapper<T>? ReadFull()`
- وجود: `bool Exists()`
- حذف: `bool Remove()` / `Task<bool> RemoveAsync()`
- **حجم حافظه**: `long GetSize()` - برمی‌گردونه حجم مصرفی به بایت
- cache: `InvalidateCache()` / `ForceToReFetch()`

RedisHashKey<T>
- تعریف: `public RedisHashKey<T> SomeHash { get; set; } = new(dbIndex, serialize?, deSerialize?);`
- نوشتن field: `Write(string field, T value)` / `Task<bool> WriteAsync(string field, T value)`
- نوشتن bulk: `Write(IDictionary<string,T> data)` / `Task<bool> WriteAsync(IDictionary<string,T> data)`
- **نوشتن تکه‌ای**: `WriteInChunks(IDictionary<string,T> data, int chunkSize = 1000)` / `Task<bool> WriteInChunksAsync(...)`
- خوندن field: `T? Read(string field, bool force = false)` / `Task<T?> ReadAsync(string field, bool force = false)`
- خوندن چند field: `Dictionary<string,T>? Read(IEnumerable<string> fields, bool force = false)` / نسخه async
- **خوندن تکه‌ای**: `ReadInChunks(IEnumerable<string> keys, int chunkSize = 1000, bool force = false)` / نسخه async
- **گرفتن همه keyها**: `RedisValue[] GetAllKeys()` / `Task<RedisValue[]> GetAllKeysAsync()`
- حذف: `Remove(string key)` / `RemoveAsync(string key)` / حذف چند field
- **حذف تکه‌ای**: `RemoveInChunks(IEnumerable<string> keys, int chunkSize = 1000)` / نسخه async
- حذف کل hash: `Task<bool> RemoveAsync()`
- **حجم حافظه**: `long GetSize()` - برمی‌گردونه حجم hash به بایت
- **cache**: `InvalidateCache(string key)` / `InvalidateCache(IEnumerable<string> keys)` / `InvalidateCache()`
- **Indexer**: `T? this[string key]` - خوندن با سینتکس indexer

RedisPrefixedKeys<T>
- تعریف: `public RedisPrefixedKeys<T> SomeGroup { get; set; } = new(dbIndex);`
- نوشتن: `Write(string field, T value)` / `Task<bool> WriteAsync(string field, T value)`
- نوشتن bulk: `Write(IDictionary<string,T> data)` / `Task<bool> WriteAsync(IDictionary<string,T> data)`
- **نوشتن تکه‌ای**: `WriteInChunks(IDictionary<string,T> data, int chunkSize = 1000)` / نسخه async
- خوندن: `T? Read(string field, bool force = false)` / `Task<T?> ReadAsync(string field, bool force = false)`
- خوندن چند field: `Dictionary<string,T>? Read(IEnumerable<string> fields, bool force = false)` / نسخه async
- **خوندن تکه‌ای**: `ReadInChunks(IEnumerable<string> keys, int chunkSize = 1000, bool force = false)` / نسخه async
- حذف: `Remove(string key)` / `RemoveAsync(string key)` / چندتایی
- **حذف تکه‌ای**: `RemoveInChunks(IEnumerable<string> keys, int chunkSize = 1000)` / نسخه async
- **حجم حافظه**: `long GetSize()` - مجموع حجم همه keyهای prefixed به بایت (از SCAN استفاده می‌کنه)
- **cache**: `InvalidateCache(string key)` / `InvalidateCache(IEnumerable<string> keys)` / `InvalidateCache()`

ابزارهای context
- `Task<long> GetDbSize(int database)`
- `Task<(List<string>? Keys, long Total)> GetHashKeysByPage(int database, string hashKey, int pageNumber = 1, int pageSize = 10)`
- `Task<string?> GetValues(int database, string key)`

---

## عملیات تکه‌ای (Chunked Operations) - جدید
برای کار با دیتاست‌های بزرگ، از متدهای تکه‌ای استفاده کن تا Redis بلاک نشه و timeout نگیری:

### نوشتن به شکل تکه‌ای
```csharp
var manyUsers = new Dictionary<string, UserProfile>();
for (int i = 0; i < 10000; i++)
    manyUsers[$"{i}"] = new UserProfile(i, $"User{i}", $"user{i}@example.com");

// Hash: نوشتن در تکه‌های 500 تایی
await ctx.Users.WriteInChunksAsync(manyUsers, chunkSize: 500);

// Prefixed keys: نوشتن در تکه‌های 500 تایی
await ctx.UserSettings.WriteInChunksAsync(manyUsers, chunkSize: 500);
```

### خوندن به شکل تکه‌ای
```csharp
var userIds = Enumerable.Range(0, 10000).Select(i => i.ToString()).ToList();

// خوندن 10000 کاربر در تکه‌های 500 تایی
var users = await ctx.Users.ReadInChunksAsync(userIds, chunkSize: 500);
Console.WriteLine($"{users?.Count} کاربر لود شد");
```

### حذف به شکل تکه‌ای
```csharp
var idsToRemove = Enumerable.Range(0, 10000).Select(i => i.ToString());

// حذف در تکه‌های 500 تایی
await ctx.Users.RemoveInChunksAsync(idsToRemove, chunkSize: 500);
```

**مزایا:**
- Redis بلاک نمیشه روی عملیات‌های بزرگ
- فشار کمتری روی حافظه
- timeout نمی‌گیره
- برای production و دیتاست‌های هزاران آیتمی امنه

---

## ردیابی مصرف حافظه (Memory Usage) - جدید
حجم مصرفی Redis رو برای مانیتورینگ و بهینه‌سازی ردیابی کن:

```csharp
// حجم یک key ساده
var versionSize = ctx.AppVersion.GetSize();
Console.WriteLine($"AppVersion: {versionSize} بایت");

// حجم کل یک hash
var usersSize = ctx.Users.GetSize();
Console.WriteLine($"Users hash: {usersSize} بایت");

// حجم کل همه keyهای prefixed (از SCAN استفاده می‌کنه - برای production امنه)
var settingsSize = ctx.UserSettings.GetSize();
Console.WriteLine($"همه تنظیمات کاربران: {settingsSize} بایت");

// مانیتور کردن همه keyها
Console.WriteLine("خلاصه مصرف حافظه:");
Console.WriteLine($"  AppVersion: {ctx.AppVersion.GetSize()} بایت");
Console.WriteLine($"  Users: {ctx.Users.GetSize()} بایت");
Console.WriteLine($"  UserSettings: {ctx.UserSettings.GetSize()} بایت");
```

**نکته‌ها:**
- از دستور `MEMORY USAGE` Redis استفاده می‌کنه
- اگر دستور پشتیبانی نشه یا غیرفعال باشه، `0` برمی‌گردونه
- برای `RedisPrefixedKeys`، همه keyهای مچ شده رو با SCAN (نه KEYS) اسکن می‌کنه - برای production امنه
- برای مانیتورینگ، برنامه‌ریزی ظرفیت، و بهینه‌سازی هزینه مفیده

---

## Paging مثال
```csharp
var (fields, total) = await ctx.GetHashKeysByPage(
    database: 1,
    hashKey: ctx.Users.FullName,
    pageNumber: 2,
    pageSize: 25);
```

---

## Bulk write chunking
```csharp
await ctx.Invoices.WriteAsync(
    items: bigDictionary,
    forceToPublish: false,
    maxChunkSizeInBytes: 256 * 1024);
```

---

## Serialization سفارشی
```csharp
public RedisHashKey<Invoice> Invoices { get; set; } = new(2,
    serialize: inv => JsonSerializer.Serialize(inv),
    deSerialize: s => JsonSerializer.Deserialize<Invoice>(s)!);
```

---

## DI
```csharp
services.AddRedisDBContext<AppRedisContext>(
    keepDataInMemory: true,
    nameGeneratorStrategy: name => $"Prod_{name}",
    channelName: "Prod");
```
Constructorها به این ترتیبه:
1) `(IConnectionMultiplexer mux, bool keepDataInMemory, ILogger logger, Func<string,string>? nameGeneratorStrategy, string? channelName)`
2) `(IConnectionMultiplexer write, IConnectionMultiplexer read, bool keepDataInMemory, ILogger logger, Func<string,string>? nameGeneratorStrategy, string? channelName)`

---

## نکته‌ها
- برای read سنگین، read multiplexer جدا (روی replica) بذار.
- `channelName` رو برای هر env/tenant ثابت نگه دار.
- بعد از پیام pub/sub، با `InvalidateCache()` کش رو تازه کن.
- Async برای مسیرهای شلوغ.
- **از عملیات تکه‌ای** (`ReadInChunks`, `WriteInChunks`, `RemoveInChunks`) برای دیتاست‌های بیش از 1000 آیتم استفاده کن.
- **مصرف حافظه رو مانیتور کن** با `GetSize()` برای برنامه‌ریزی ظرفیت و بهینه‌سازی هزینه.
- `chunkSize` مناسب تنظیم کن بر اساس حجم دیتا (پیش‌فرض 1000 برای اکثر موارد خوبه).
- برای پارامتر `force`: از `true` استفاده کن تا cache رو bypass کنی و همیشه از Redis بخونی.

---

## رفع اشکال
- پیام pub/sub نمی‌رسه؟ ببین `channelName` ست شده و publish از write connection انجام میشه.
- دیتا قدیمیه؟ `keepDataInMemory` و invalidate شدن cacheها رو چک کن.
- روی bulk write timeout می‌گیری؟ اندازهٔ `maxChunkSizeInBytes` رو کمتر کن.
- DB size صفره؟ بعضی ارائه‌دهنده‌ها بعضی دستورات (مثل `DBSIZE`) رو می‌بندن.

---

## نسخه
- Target framework: .NET 9
- Redis client: StackExchange.Redis 2.7.x

---

## لایسنس
MIT

## مشارکت
Issue و PR همیشه خوشحال‌مون می‌کنه.
