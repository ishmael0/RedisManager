using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Modules.RedisModule
{

    /// <summary>
    /// Provides a reflection-based bootstrap context that initializes Redis key/hash key properties
    /// on derived types, wiring publish/subscribe callbacks and optional in-memory caching behavior.
    /// </summary>
    public class RedisDBContextModule
    {
        /// <summary>
        /// Writer connection multiplexer.
        /// </summary>
        public IConnectionMultiplexer ConnectionMultiplexerWrite { set; get; }
        /// <summary>
        /// Reader connection multiplexer.
        /// </summary>
        public IConnectionMultiplexer ConnectionMultiplexerRead { set; get; }
        /// <summary>
        /// Indicates if keys should keep data cached locally.
        /// </summary>
        public bool KeepDataInMemory { get; }
        /// <summary>
        /// Logger for diagnostics.
        /// </summary>
        public ILogger Logger { get; }
        /// <summary>
        /// Redis subscriber used for publish notifications.
        /// </summary>
        public ISubscriber Sub { get; }
        /// <summary>
        /// Root channel used for publish events.
        /// </summary>
        public RedisChannel Channel { get; }

        /// <summary>
        /// Constructs the context and initializes all declared <see cref="RedisHashKey{T}"/> and <see cref="RedisKey{T}"/> properties via reflection.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="connectionMultiplexerWrite">Write connection.</param>
        /// <param name="connectionMultiplexerRead">Read connection.</param>
        /// <param name="prefix">Environment/namespace prefix for key names.</param>
        /// <param name="keepDataInMemory">Enable in-memory caching of values.</param>
        /// <param name="usePushNotification">Whether to enable publish notifications (currently always used).</param>
        public RedisDBContextModule(ILogger logger, IConnectionMultiplexer connectionMultiplexerWrite,
            IConnectionMultiplexer connectionMultiplexerRead, bool keepDataInMemory,
            bool usePushNotification = true, string? prefix = null)
        {
            Logger = logger;
            ConnectionMultiplexerRead = connectionMultiplexerRead;
            ConnectionMultiplexerWrite = connectionMultiplexerWrite;
            KeepDataInMemory = keepDataInMemory;
            Sub = ConnectionMultiplexerRead.GetSubscriber();
            Channel = new RedisChannel(prefix, RedisChannel.PatternMode.Literal);
            GetType().GetProperties().Where(c =>
                    c.PropertyType.IsGenericType && c.PropertyType.GetGenericTypeDefinition() == typeof(RedisHashKey<>))
                .Select(c => new { GType = c, Type = c.PropertyType.GetGenericArguments()[0], c.Name })
                .ToList().ForEach(c =>
                {
                    var item = c.GType.GetValue(this) as IRedisCommonHashKeyMethods;
                    if (item == null)
                    {

                        item =
                            Activator.CreateInstance(typeof(RedisHashKey<>).MakeGenericType(c.Type), 1, null, null) as
                                IRedisCommonHashKeyMethods;
                        c.GType.SetValue(this, item);
                    }
                    var ch = new RedisChannel(prefix, RedisChannel.PatternMode.Literal);
                    item!.Init(Logger, connectionMultiplexerWrite, connectionMultiplexerRead,
                        () => Sub?.Publish(ch, $"{c.Name}|all"),
                        (key) => Sub?.Publish(ch, $"{c.Name}|{key}"),
                        new RedisKey($"{prefix}_{c.Name}"), keepDataInMemory);
                });


            GetType().GetProperties().Where(c =>
                    c.PropertyType.IsGenericType && c.PropertyType.GetGenericTypeDefinition() == typeof(RedisKey<>))
                .Select(c => new { GType = c, Type = c.PropertyType.GetGenericArguments()[0], c.Name })
                .ToList().ForEach(c =>
                {
                    var item = c.GType.GetValue(this) as IRedisCommonKeyMethods;
                    if (item == null)
                    {

                        item =
                            Activator.CreateInstance(typeof(RedisKey<>).MakeGenericType(c.Type), 1, null, null) as
                                IRedisCommonKeyMethods;
                        c.GType.SetValue(this, item);
                    }
                    var ch = new RedisChannel(prefix, RedisChannel.PatternMode.Literal);
                    item!.Init(Logger, connectionMultiplexerWrite, connectionMultiplexerRead, () => Sub?.Publish(ch, c.Name),
                        new RedisKey($"{prefix}_{c.Name}"), keepDataInMemory);
                });
        }

        /// <summary>
        /// Returns the number of keys present in a given Redis logical database.
        /// </summary>
        /// <param name="database">Database index.</param>
        /// <returns>Total key count as reported by DBSIZE.</returns>
        public async Task<long> GetDbSize(int database)
        {
            var db = ConnectionMultiplexerRead.GetDatabase(database);
            var keyCount = await db.ExecuteAsync("DBSIZE");
            return Convert.ToInt32(keyCount.ToString());
        }

        /// <summary>
        /// Retrieves a page of field names for a hash key using HashScan (cursor based) semantics.
        /// </summary>
        /// <param name="database">Database index.</param>
        /// <param name="hashKey">Hash key name.</param>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Number of fields to return.</param>
        /// <returns>Tuple of field list and total hash length.</returns>
        public async Task<(List<string>?, long)> GetHashKeysByPage(int database, string hashKey, int pageNumber = 1, int pageSize = 10)
        {
            var db = ConnectionMultiplexerRead.GetDatabase(database);
            long cursor = (pageNumber - 1) * pageSize;
            var keysRetrieved = 0;
            var keys = new List<string>();
            var hashLength = await db.HashLengthAsync(hashKey);
            await foreach (var entry in db.HashScanAsync(hashKey, cursor: cursor, pageSize: pageSize))
            {
                keys.Add(entry.Name!);
                keysRetrieved++;
                if (keysRetrieved >= pageSize)
                    break;
            }

            return (keys, hashLength);
        }
        /// <summary>
        /// Reads and returns the string value for a simple key.
        /// </summary>
        /// <param name="database">Database index.</param>
        /// <param name="key">Key name.</param>
        /// <returns>Raw string value or null.</returns>
        public async Task<string?> GetValues(int database, string key)
        {
            var db = ConnectionMultiplexerRead.GetDatabase(database);
            var hashEntries = await db.StringGetAsync(key);

            return hashEntries;

        }
    }
}

