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

    // توجه: برای ذخیره به شکل "FullName:field" می‌تونی از `RedisPrefixedKeys<T>` استفاده کنی (بخش پایین)

    // reader/write جدا
    public AppRedisContext(IConnectionMultiplexer writer,
                           IConnectionMultiplexer reader,
                           ILogger<AppRedisContext> logger,
                           Func<string, string>? nameGeneratorStrategy = null,
                           bool keepDataInMemory = true,
                           string? channelName = null)
        : base(writer, reader, keepDataInMemory, logger, nameGeneratorStrategy, channelName) { }

    // تک multiplexer
    public AppRedisContext(IConnectionMultiplexer mux,
                           ILogger<AppRedisContext> logger,
                           Func<string, string>? nameGeneratorStrategy = null,
                           bool keepDataInMemory = true,
                           string? channelName = null)
        : base(mux, keepDataInMemory, logger, nameGeneratorStrategy, channelName) { }
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

ctx.Users.Write("42", new UserProfile(42, "Alice"));
var alice = ctx.Users.Read("42");

await ctx.Users.WriteAsync(new Dictionary<string, UserProfile>
{
    ["1"] = new(1, "Bob"),
    ["2"] = new(2, "Carol")
}, forceToPublish: true); // اگه channelName ست شده باشه، "Users|all" منتشر میشه

var batch = ctx.Users.Read(new[] { "1", "2" });
```

---

## کلید رشته‌ای با پیشوند: FullName:field
`RedisPrefixedKeys<T>` هر field را به‌صورت یک کلید مستقل ذخیره می‌کند با الگوی `"FullName:field"`.
- مناسب زمانی که به‌جای hash، کلیدهای مستقل string می‌خوای.
- کش درون‌حافظه‌ای برای هر field، publish برای هر field و publish-all پشتیبانی می‌شود.
- نکته: ابزار لیست‌کردن کلیدها/fieldها به‌صورت پیش‌فرض ارائه نشده. در صورت نیاز خودت الگوپردازی یا ردیابی نام fieldها را در برنامه پیاده‌سازی کن.

راه‌اندازی (init دستی داخل context):
```csharp
public class AppRedisContext : RedisDBContextModule
{
    public RedisPrefixedKeys<UserProfile> UserById { get; set; } = new(3);

    public AppRedisContext(IConnectionMultiplexer writer,
                           IConnectionMultiplexer reader,
                           ILogger<AppRedisContext> logger,
                           Func<string, string>? nameGeneratorStrategy = null,
                           bool keepDataInMemory = true,
                           string? channelName = null)
        : base(writer, reader, keepDataInMemory, logger, nameGeneratorStrategy, channelName)
    {
        // نام و publish (هم‌الگوی hash)
        var name = nameGeneratorStrategy?.Invoke(nameof(UserById)) ?? nameof(UserById);
        Action publishAll = string.IsNullOrWhiteSpace(channelName)
            ? () => { }
            : () => Sub?.Publish(Channel, $"{nameof(UserById)}|all");
        Action<string> publish = string.IsNullOrWhiteSpace(channelName)
            ? _ => { }
            : field => Sub?.Publish(Channel, $"{nameof(UserById)}|{field}");

        UserById.Init(logger, writer, reader, publishAll, publish, new RedisKey(name), keepDataInMemory);
    }
}

// استفاده
await ctx.UserById.WriteAsync("42", new UserProfile(42, "Alice"));
var u = await ctx.UserById.ReadAsync("42");
await ctx.UserById.RemoveAsync("42");
```

Payloadهای Pub/Sub (مثل hash):
- برای هر field: `KeyName|{field}`
- برای همه: `KeyName|all`

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

ابزار invalidate:
```csharp
ctx.AppVersion.ForceToReFetch();        // کش key ساده را خالی کن
ctx.Users.ForceToReFetch("42");        // یک field از hash
ctx.Users.ForceToReFetchAll();          // همهٔ fieldهای hash
ctx.Users.DoPublishAll();               // "Users|all" را publish کن
// برای prefixed
ctx.UserById.ForceToReFetch("42");
ctx.UserById.ForceToReFetchAll();
ctx.UserById.DoPublishAll();
```

---

## API Cheatsheet

RedisKey<T>
- تعریف: `public RedisKey<T> SomeKey { get; set; } = new(dbIndex);`
- نوشتن: `Write(T value)` / `Task WriteAsync(T value)`
- خوندن: `T? Read()` / `Task<T?> ReadAsync()`
- خوندن wrapper کامل: `RedisDataWrapper<T>? ReadFull()`
- وجود: `bool Exists()`
- حذف: `bool Remove()` / `Task<bool> RemoveAsync()`
- cache: `ForceToReFetch()`

RedisHashKey<T>
- تعریف: `public RedisHashKey<T> SomeHash { get; set; } = new(dbIndex, serialize?, deSerialize?);`
- نوشتن field: `Write(string field, T value)` / `Task WriteAsync(string field, T value)`
- نوشتن bulk: `Task<bool> WriteAsync(IDictionary<string,T> items, bool forceToPublish = false, int maxChunkSizeInBytes = 1024*128)`
- خوندن field: `T? Read(string field)` / `Task<T?> ReadAsync(string field)`
- خوندن چند field: `IDictionary<string,T> Read(IEnumerable<string> fields)`
- حذف: `Task<bool> RemoveAsync(string field)` / حذف چند field
- حذف کل hash: `Task<bool> RemoveAsync()`
- cache: `ForceToReFetch(string field)` / `ForceToReFetchAll()`
- publish-all: `DoPublishAll()`

RedisPrefixedKeys<T>
- تعریف: `public RedisPrefixedKeys<T> SomeGroup { get; set; } = new(dbIndex);` (init دستی)
- نوشتن: `Write(string field, T value)` / `Task WriteAsync(string field, T value)`
- نوشتن bulk: `Task<bool> WriteAsync(IDictionary<string,T> items, bool forceToPublish = false)`
- خوندن: `T? Read(string field)` / `Task<T?> ReadAsync(string field)`
- خوندن چند field: `IDictionary<string,T> Read(IEnumerable<string> fields)`
- حذف: `Task<bool> RemoveAsync(string field)` / چندتایی
- cache: `ForceToReFetch(string field)` / `ForceToReFetchAll()`
- publish-all: `DoPublishAll()`

ابزارهای context
- `Task<long> GetDbSize(int database)`
- `Task<(List<string>? Keys, long Total)> GetHashKeysByPage(int database, string hashKey, int pageNumber = 1, int pageSize = 10)`
- `Task<string?> GetValues(int database, string key)`

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

نکته: `RedisPrefixedKeys<T>` فعلاً به‌صورت دستی init می‌شود (مثال بالا).

---

## نکته‌ها
- برای read سنگین، read multiplexer جدا (روی replica) بذار.
- `channelName` رو برای هر env/tenant ثابت نگه دار.
- بعد از پیام pub/sub، با `ForceToReFetch(All)` کش رو تازه کن.
- Async برای مسیرهای شلوغ.
- برای bulk بزرگ، `maxChunkSizeInBytes` معقول تنظیم کن.

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
