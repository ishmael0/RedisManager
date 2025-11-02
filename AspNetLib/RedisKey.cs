using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    /// <summary>
    /// Represents a simple Redis string key abstraction (not hash) that can cache a single serialized value
    /// in-memory to reduce subsequent round trips. Supports optional custom serialization functions and
    /// publish callbacks when values change.
    /// </summary>
    /// <typeparam name="T">Type of the value stored under the key.</typeparam>
    public class RedisKey<T> : RedisCommonProperties<T>, IRedisKey
    {
        private readonly object _locker = new();
        private RedisDataWrapper<T>? _data;
        /// <summary>
        /// Delegate invoked after a successful write to notify subscribers.
        /// </summary>
        public Action Publish { private set; get; }
        /// <summary>
        /// Creates a new <see cref="RedisKey{T}"/>.
        /// </summary>
        /// <param name="dbIndex">Redis database index.</param>
        /// <param name="serialize">Optional custom serializer (value -> string).</param>
        /// <param name="deSerialize">Optional custom deserializer (string -> value).</param>
        public RedisKey(int dbIndex, Func<T, string>? serialize = null, Func<string, T>? deSerialize = null)
        {
            DbIndex = dbIndex;
            Serialize = (d) => JsonConvert.SerializeObject(serialize == null ? new RedisDataWrapper<T>(d) : new RedisDataWrapper<string>(serialize(d)));
            DeSerialize = (str) =>
            {
                if (str == null)
                    return null;    
                if (deSerialize == null)
                    return JsonConvert.DeserializeObject<RedisDataWrapper<T>>(str)!;
                var temp = JsonConvert.DeserializeObject<RedisDataWrapper<string>>(str);
                if (temp != null)
                    return new RedisDataWrapper<T>(deSerialize(temp!.Data))
                    {
                        DateTime = temp.DateTime,
                        PersianLastUpdate = temp.PersianLastUpdate
                    };
                return null;
            };
        }
        public void Init(RedisDBContextModuleConfigs contexConfig, RedisKey fullName)
        {
            ContextConfig = contexConfig;
            FullName = fullName;
            Reader = ContextConfig.Reader.GetDatabase(DbIndex);
            Writer = ContextConfig.Writer.GetDatabase(DbIndex);
        }
        /// <summary>
        /// Returns approximate memory usage (bytes) in Redis for this key using the MEMORY USAGE command.
        /// </summary>
        public long GetSize()
        {
            var keyMemoryUsage = Reader.Execute("MEMORY", "USAGE", FullName);
            return keyMemoryUsage.IsNull ? 0 : Convert.ToInt64(keyMemoryUsage.ToString());
        }


        /// <summary>
        /// Reads the full wrapper (value plus metadata) optionally forcing a fresh Redis fetch.
        /// </summary>
        /// <param name="force">If true bypasses the in-memory cache.</param>
        /// <returns>Wrapper or null.</returns>
        public RedisDataWrapper<T>? ReadFull(bool force = false)
        {
            if (!force && _data != null)
                return _data;
            try
            {
                lock (_locker)
                {
                    var temp = Reader.StringGet(FullName).ToString();
                    if (string.IsNullOrEmpty(temp))
                        return default;
                    var data = DeSerialize(temp);
                    if (data != null)
                    {
                        if (ContextConfig.KeepDataInMemory)
                            _data = data;
                        return data;
                    }
                }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}");
            }
            if (_data != null)
                return _data;
            return default;
        }

        /// <summary>
        /// Reads only the value (not metadata). Optionally forces a Redis fetch.
        /// </summary>
        /// <param name="force">If true forces a re-fetch.</param>
        /// <returns>Value or default.</returns>
        public T? Read(bool force = false)
        {
            var d = ReadFull(force);
            return d == null ? default : d.Data;
        }
        /// <summary>
        /// Asynchronously reads the value optionally forcing a Redis fetch.
        /// </summary>
        /// <param name="force">If true bypasses cache.</param>
        /// <returns>Value or default.</returns>
        public async Task<T?> ReadAsync(bool force = false)
        {
            if (!force && _data != null)
                return _data.Data;
            try
            {
                var temp = (await Reader.StringGetAsync(FullName)).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                lock (_locker)
                    if (data != null)
                    {
                        if (ContextConfig. KeepDataInMemory)
                            _data = data;
                        return data.Data;
                    }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}");
            }
            if (_data != null)
                return _data.Data;
            return default;
        }
        /// <summary>
        /// Writes the value to Redis (overwriting existing) and optionally updates in-memory cache.
        /// Triggers the publish callback upon success.
        /// </summary>
        /// <param name="d">Value to store.</param>
        /// <returns>True if the write succeeds.</returns>
        public bool Write(T d)
        {
            if (d == null) return false;
            try
            {
                var res = Writer.StringSet(FullName, Serialize(d));
                Publish();
                if (ContextConfig.KeepDataInMemory)
                    lock (_locker)
                        _data = new RedisDataWrapper<T>(d);
                return res;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}");
                return false;
            }
        }
        /// <summary>
        /// Asynchronously writes the value to Redis and triggers publish on success.
        /// </summary>
        /// <param name="d">Value to store.</param>
        /// <returns>True if the write succeeds.</returns>
        public async Task<bool> WriteAsync(T d)
        {
            if (d == null) return false;
            try
            {
                var res = await Writer.StringSetAsync(FullName, Serialize(d));
                if (ContextConfig.KeepDataInMemory)
                    lock (_locker)
                        _data = new RedisDataWrapper<T>(d);
                Publish();
                return res;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}");
                return false;
            }
        }
        /// <summary>
        /// Clears the in-memory cached value forcing the next read to hit Redis.
        /// </summary>
        public void InvalidateCache()
        {
            lock (_locker)
                _data = null;
            //Read(true);
        }
        /// <summary>
        /// Indexer to access a hash field on a key that is assumed to be a hash (legacy behavior).
        /// </summary>
        /// <param name="key">Hash field name.</param>
        public RedisValue this[string key]
        {
            get => Reader.HashGet(FullName, key);
        }
    }
}
