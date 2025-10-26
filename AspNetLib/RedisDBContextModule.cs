using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{

    /// <summary>
    /// Provides a reflection-based bootstrap context that initializes Redis key/hash key properties
    /// on derived types, wiring optional publish/subscribe callbacks and optional in-memory caching behavior.
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
        public bool KeepDataInMemory { get; set; }
        /// <summary>
        /// Logger for diagnostics.
        /// </summary>
        private ILogger Logger { get; set; }
        /// <summary>
        /// Redis subscriber used for publish notifications.
        /// </summary>
        public ISubscriber Sub { get; set; }
        /// <summary>
        /// Root channel used for publish events (only meaningful when a non-empty channel name is provided).
        /// </summary>
        public RedisChannel Channel { get; set; }

        /// <summary>
        /// Constructs the context and initializes all declared <see cref="RedisHashKey{T}"/> and <see cref="RedisKey{T}"/> properties via reflection.
        /// </summary>
        /// <param name="connectionMultiplexerWrite">Write connection.</param>
        /// <param name="connectionMultiplexerRead">Read connection.</param>
        /// <param name="keepDataInMemory">Enable in-memory caching of values.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="nameGeneratorStrategy"></param>
        /// <param name="channelName">Pub/Sub channel name. If null/empty no publish operations are performed.</param>
        public RedisDBContextModule(IConnectionMultiplexer connectionMultiplexerWrite,
            IConnectionMultiplexer connectionMultiplexerRead,
            bool keepDataInMemory,
            ILogger logger,
            Func<string, string>? nameGeneratorStrategy = null,
            string? channelName = null)
        {
            Init(connectionMultiplexerWrite, connectionMultiplexerRead, keepDataInMemory, logger, nameGeneratorStrategy, channelName);
        }
        public RedisDBContextModule(IConnectionMultiplexer connectionMultiplexer,
            bool keepDataInMemory,
            ILogger logger,
            Func<string, string>? nameGeneratorStrategy = null,
            string? channelName = null)
        {
            Init(connectionMultiplexer, connectionMultiplexer, keepDataInMemory, logger, nameGeneratorStrategy, channelName);
        }

        private void Init(
            IConnectionMultiplexer connectionMultiplexerWrite,
            IConnectionMultiplexer connectionMultiplexerRead,
            bool keepDataInMemory,
            ILogger logger,
            Func<string, string>? nameGeneratorStrategy = null,
            string? channelName = null
            )
        {
            Logger = logger;
            ConnectionMultiplexerRead = connectionMultiplexerRead;
            ConnectionMultiplexerWrite = connectionMultiplexerWrite;
            KeepDataInMemory = keepDataInMemory;
            Sub = ConnectionMultiplexerWrite.GetSubscriber();
            var hasChannel = !string.IsNullOrWhiteSpace(channelName);
            Channel = hasChannel ? new RedisChannel(channelName, RedisChannel.PatternMode.Literal) : default;

            // Single pass over properties to initialize keys
            var type = GetType();
            foreach (var prop in type.GetProperties())
            {
                var pType = prop.PropertyType;
                if (!pType.IsGenericType)
                    continue;

                var genDef = pType.GetGenericTypeDefinition();
                var tArg = pType.GetGenericArguments()[0];
                var fullKeyName = nameGeneratorStrategy != null ? nameGeneratorStrategy(prop.Name) : prop.Name;

                if (genDef == typeof(RedisHashKey<>))
                {
                    var item = prop.GetValue(this) as IRedisCommonHashKeyMethods
                               ?? Activator.CreateInstance(typeof(RedisHashKey<>).MakeGenericType(tArg), 1, null, null) as IRedisCommonHashKeyMethods;
                    if (item == null) continue;
                    prop.SetValue(this, item);

                    Action publishAll = hasChannel
                        ? () => { Sub?.Publish(Channel, $"{prop.Name}|all"); }
                        : () => { };
                    Action<string> publishField = hasChannel
                        ? key => { Sub?.Publish(Channel, $"{prop.Name}|{key}"); }
                        : _ => { };

                    item.Init(Logger, connectionMultiplexerWrite, connectionMultiplexerRead,
                        publishAll,
                        publishField,
                        new RedisKey(fullKeyName), keepDataInMemory);
                }
                else if (genDef == typeof(RedisKey<>))
                {
                    var item = prop.GetValue(this) as IRedisCommonKeyMethods
                               ?? Activator.CreateInstance(typeof(RedisKey<>).MakeGenericType(tArg), 1, null, null) as IRedisCommonKeyMethods;
                    if (item == null) continue;
                    prop.SetValue(this, item);

                    Action publish = hasChannel
                        ? () => { Sub?.Publish(Channel, prop.Name); }
                        : () => { };

                    item.Init(Logger, connectionMultiplexerWrite, connectionMultiplexerRead, publish,
                        new RedisKey(fullKeyName), keepDataInMemory);
                }
            }
        }


        /// <summary>
        /// Returns the number of keys present in a given Redis logical database.
        /// </summary>
        public async Task<long> GetDbSize(int database)
        {
            var db = ConnectionMultiplexerRead.GetDatabase(database);
            var keyCount = await db.ExecuteAsync("DBSIZE");
            return Convert.ToInt32(keyCount.ToString());
        }

        /// <summary>
        /// Retrieves a page of field names for a hash key using HashScan (cursor based) semantics.
        /// </summary>
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
        public async Task<string?> GetValues(int database, string key)
        {
            var db = ConnectionMultiplexerRead.GetDatabase(database);
            var hashEntries = await db.StringGetAsync(key);
            return hashEntries;
        }
    }
}

