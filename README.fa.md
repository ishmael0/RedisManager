# Santel.Redis.TypedKeys (فارسی ساده)

کلیدهای Redis به‌صورت تایپی برای .NET 9. هدفش اینه کار با key/hash راحت‌تر بشه: تعریف ساده، cache داخل حافظه، و pub/sub سبک. همه‌چی روی StackExchange.Redis پیاده‌سازی شده.

- .NET: 9
- Redis client: StackExchange.Redis 2.x
- Package: Santel.Redis.TypedKeys

[English README](./README.md)

## چی داره برات؟
- `RedisKey<T>` و `RedisHashKey<T>` برای کار با key و hash به‌صورت تایپی
- یه context مرکزی (`RedisDBContextModule`) که توش همهٔ keyها رو تعریف می‌کنی
- cache اختیاری برای key و fieldها (حافظهٔ داخل برنامه)
- pub/sub سبک برای invalidation بین چند پروسه
- امکان serialization سفارشی برای هر key
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

1) اول context خودت رو بساز (از `RedisDBContextModule` ارث‌بری کن) و keyها رو تعریف کن:
```csharp
using Newtonsoft.Json;
using Santel.Redis.TypedKeys;
using StackExchange.Redis;

public class AppRedisContext : RedisDBContextModule
{
    // DB 0: یه key ساده (string)
    public RedisKey<string> AppVersion { get; set; } = new(0);

    // DB 1: user profileها داخل یه hash (field = userId)
    public RedisHashKey<UserProfile> Users { get; set; } = new(1);

    // DB 2: invoiceها با serialization سفارشی
    public RedisHashKey<Invoice> Invoices { get; set; } = new(2,
        serialize: inv => JsonConvert.SerializeObject(inv, Formatting.None),
        deSerialize: s => JsonConvert.DeserializeObject<Invoice>(s)!);

    // اگه reader و writer جدا داری (مثلاً replica)، از این سازنده استفاده کن
    public AppRedisContext(IConnectionMultiplexer writer,
                           IConnectionMultiplexer reader,
                           ILogger<AppRedisContext> logger,
                           string? prefix,
                           bool keepDataInMemory = true,
                           string? channelName = null)
        : base(writer, reader, keepDataInMemory, logger, prefix, channelName) { }

    // اگه یه multiplexer داری، از این استفاده کن (read = write)
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

2) بعدش توی DI ثبتش کن
```csharp
using Microsoft.Extensions.DependencyInjection;
using Santel.Redis.TypedKeys;
using StackExchange.Redis;

var services = new ServiceCollection();
services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect("localhost:6379"));
services.AddLogging();

// ثبت context به‌صورت جنریک. اول constructor تک‌multiplexer رو امتحان می‌کنه، بعد دوتایی.
services.AddRedisDBContext<AppRedisContext>(
    keepDataInMemory: true,
    prefix: "Prod",
    channelName: "Prod");
```

3) استفاده
```csharp
var sp = services.BuildServiceProvider();
var ctx = sp.GetRequiredService<AppRedisContext>();

// key ساده
ctx.AppVersion.Write("1.5.0");
string? version = ctx.AppVersion.Read();

// hash: یه field
ctx.Users.Write("42", new UserProfile(42, "Alice"));
var alice = ctx.Users.Read("42");

// hash: نوشتن چندتا مورد با هم + publish برای invalidation بقیه پروسه‌ها
await ctx.Users.WriteAsync(new Dictionary<string, UserProfile>
{
    ["1"] = new(1, "Bob"),
    ["2"] = new(2, "Carol")
}, forceToPublish: true); // اگه channelName ست شده باشه، "Users|all" منتشر میشه

// hash: خوندن چندتا field با هم
var batch = ctx.Users.Read(new[] { "1", "2" });
```

---

## نام‌گذاری key و Pub/Sub
- نام‌گذاری key: اگه `prefix` خالی باشه => `PropertyName`؛ وگرنه => `${prefix}_${PropertyName}`
- publish روی چه channelی؟ همونی که `channelName` میدی
  - برای `RedisKey<T>`: خود اسم key publish میشه
  - برای `RedisHashKey<T>` (یه field): `HashName|{field}`
  - برای publish-all: `HashName|all`

نمونهٔ subscribe:
```csharp
var sub = readerMux.GetSubscriber();
await sub.SubscribeAsync("Prod", (ch, msg) =>
{
    var text = (string)msg;
    if (text.EndsWith("|all"))
    {
        // اینجا کل cache مربوط به اون hash رو خالی کن
    }
    else if (text.Contains('|'))
    {
        var parts = text.Split('|'); // parts[0] = اسم hash، parts[1] = field
        // اینجا cache همون field رو خالی کن
    }
    else
    {
        // key ساده تغییر کرده؛ cacheش رو خالی کن
    }
});
```

نکته: publish با write multiplexer انجام میشه؛ subscribe می‌تونه با read multiplexer باشه.

---

## Cache و invalidate
با `keepDataInMemory` روشن یا خاموشش کن.
- `RedisKey<T>`: آخرین مقدار (داخل `RedisDataWrapper<T>`) cache میشه
- `RedisHashKey<T>`: هر field جدا جدا cache میشه

ابزارهای invalidate:
```csharp
ctx.AppVersion.ForceToReFetch();        // cache key ساده رو خالی کن
ctx.Users.ForceToReFetch("42");        // cache یه field رو خالی کن
ctx.Users.ForceToReFetchAll();          // cache همهٔ fieldهای این hash رو خالی کن
ctx.Users.DoPublishAll();               // "Users|all" رو publish کن
```

---

## API Cheatsheet (خلاصه کاربردی)

RedisKey<T>
- تعریف داخل context: `public RedisKey<T> SomeKey { get; set; } = new(dbIndex);`
- نوشتن: `Write(T value)` / `Task WriteAsync(T value)`
- خوندن: `T? Read()` / `Task<T?> ReadAsync()`
- خوندن کامل با زمان‌ها: `RedisDataWrapper<T>? ReadFull()`
- چک وجود: `bool Exists()`
- حذف: `bool Remove()` / `Task<bool> RemoveAsync()`
- مدیریت cache: `ForceToReFetch()`

RedisHashKey<T>
- تعریف: `public RedisHashKey<T> SomeHash { get; set; } = new(dbIndex, serialize?, deSerialize?);`
- نوشتن یه field: `Write(string field, T value)` / `Task WriteAsync(string field, T value)`
- نوشتن bulk: `Task<bool> WriteAsync(IDictionary<string,T> items, bool forceToPublish = false, int maxChunkSizeInBytes = 1024*128)`
- خوندن یه field: `T? Read(string field)` / `Task<T?> ReadAsync(string field)`
- خوندن چندتا field: `IDictionary<string,T?> Read(IEnumerable<string> fields)`
- حذف field: `Task<bool> RemoveAsync(string field)` / حذف چندتا field
- حذف کل hash: `Task<bool> RemoveAsync()`
- مدیریت cache: `ForceToReFetch(string field)` / `ForceToReFetchAll()`
- publish-all: `DoPublishAll()`

کمک‌های context
- `Task<long> GetDbSize(int database)`
- `Task<(List<string>? Keys, long Total)> GetHashKeysByPage(int database, string hashKey, int pageNumber = 1, int pageSize = 10)`
- `Task<string?> GetValues(int database, string key)` (خواندن raw value از یه key ساده)

---

## Paging مثال (fieldهای hash)
```csharp
var (fields, total) = await ctx.GetHashKeysByPage(
    database: 1,
    hashKey: ctx.Users.FullName,
    pageNumber: 2,
    pageSize: 25);
```

---

## Bulk write chunking
وقتی دیکشنری بزرگ می‌نویسی، با `maxChunkSizeInBytes` می‌تونی تکه‌تکه‌ش کنی تا عملیات سنگین نشه:
```csharp
await ctx.Invoices.WriteAsync(
    items: bigDictionary,
    forceToPublish: false,
    maxChunkSizeInBytes: 256 * 1024);
```
این کار احتمال timeout رو کم می‌کنه.

---

## Custom serialization
برای هر key می‌تونی serializer خودت رو بدی. دیتا همیشه داخل `RedisDataWrapper<T>` ذخیره میشه تا timestamp هم داشته باشی.
```csharp
public RedisHashKey<Invoice> Invoices { get; set; } = new(2,
    serialize: inv => JsonSerializer.Serialize(inv),
    deSerialize: s => JsonSerializer.Deserialize<Invoice>(s)!);
```

---

## DI (ثبت در Dependency Injection)
اکستنشن جنریک داریم:
```csharp
services.AddRedisDBContext<AppRedisContext>(
    keepDataInMemory: true,
    prefix: "Prod",          // میشه prefix اسم key: Prod_{Property}
    channelName: "Prod");    // pub/sub channel (اگه خالی باشه publish غیرفعال میشه)
```
سازنده‌ها رو به این ترتیب امتحان می‌کنه:
1) `(IConnectionMultiplexer mux, bool keepDataInMemory, ILogger logger, string? prefix, string? channelName)`
2) `(IConnectionMultiplexer write, IConnectionMultiplexer read, bool keepDataInMemory, ILogger logger, string? prefix, string? channelName)`

اگه wiring خاصی می‌خوای، می‌تونی دستی هم ثبت کنی.

---

## نکته‌ها و Best Practices
- برای read زیاد، یه read multiplexer که به replica وصله خیلی کمک می‌کنه.
- `channelName` رو برای هر محیط/tenant ثابت نگه دار تا تداخل نشه.
- بعد از دریافت پیام pub/sub، با `ForceToReFetch(All)` کش‌ها رو تازه کن.
- توی مسیرهای شلوغ از async استفاده کن.
- برای bulk writeهای خیلی بزرگ، `maxChunkSizeInBytes` منطقی بذار.

---

## عیب‌یابی (Troubleshooting)
- پیام pub/sub نمی‌رسه؟ ببین `channelName` ست شده و publish از write connection انجام میشه.
- دیتا قدیمیه؟ `keepDataInMemory` و invalidate شدن cacheها رو چک کن.
- روی bulk write timeout می‌گیری؟ اندازهٔ `maxChunkSizeInBytes` رو کمتر کن.
- DB size صفره؟ ممکنه بعضی سرویس‌ها دستوراتی مثل `DBSIZE` رو بسته باشن.

---

## نسخه
- Target framework: .NET 9
- Redis client: StackExchange.Redis 2.7.x

---

## لایسنس
MIT

## مشارکت
Issue و PR همیشه خوشحال‌مون می‌کنه.
