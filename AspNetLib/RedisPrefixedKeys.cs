using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Santel.Redis.TypedKeys
{
    /// <summary>
    /// Manages a group of Redis string keys under a common prefix. Each logical field is stored as an individual
    /// Redis string key with the composed format: "FullName:field".
    /// Provides optional in-memory caching per field and synchronous/asynchronous CRUD helpers.
    /// </summary>
    /// <typeparam name="T">Value type stored per composed key.</typeparam>
    public class RedisPrefixedKeys<T> : RedisCommonProperties<T>, IRedisCommonPrefixKeyMethods
    {
        private readonly ConcurrentDictionary<string, RedisDataWrapper<T>> _data = new();
        private Action PublishAll;
        private Action<string> Publish;
        public RedisPrefixedKeys(int dbIndex, Func<T, string>? serialize = null, Func<string, T>? deSerialize = null)
        {
            DbIndex = dbIndex;
            Serialize = (d) => JsonConvert.SerializeObject(serialize == null ? new RedisDataWrapper<T>(d) : new RedisDataWrapper<string>(serialize(d)));
            DeSerialize = (str) =>
            {
                if (deSerialize == null)
                    return JsonConvert.DeserializeObject<RedisDataWrapper<T>>(str)!;
                var temp = JsonConvert.DeserializeObject<RedisDataWrapper<string>>(str);
                if (temp != null && !string.IsNullOrEmpty(temp.Data))
                    return new RedisDataWrapper<T>(deSerialize(temp!.Data))
                    {
                        DateTime = temp.DateTime,
                        PersianLastUpdate = temp.PersianLastUpdate
                    };
#pragma warning disable CS8603 // Possible null reference return.
                return null;
#pragma warning restore CS8603 // Possible null reference return.
            };
        }

        /// <summary>
        /// Initializes connections, logging and configuration for the prefixed keys manager.
        /// </summary>
        public void Init(ILogger logger,
            IConnectionMultiplexer? cnWriter,
            IConnectionMultiplexer cnReader,
            Action publishAll,
            Action<string> publish,
            RedisKey fullName,
            bool keepDataInMemory)
        {
            Publish = publish;
            PublishAll = publishAll;
            Logger = logger;
            DbReader = cnReader.GetDatabase(DbIndex);
            if (cnWriter != null)
                DbWriter = cnWriter.GetDatabase(DbIndex);
            FullName = fullName;
            KeepDataInMemory = keepDataInMemory;
            _cnReader = cnReader;
        }

        private RedisKey Compose(string key) => (RedisKey)$"{FullName}:{key}";
        /// <summary>
        /// Read full wrapper for a specific field.
        /// </summary>
        public RedisDataWrapper<T>? ReadFull(string key, bool force = false)
        {
            if (!force && _data.TryGetValue(key, out var cached))
                return cached;
            try
            {
                var temp = DbReader.StringGet(Compose(key)).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                if (data != null)
                {
                    if (KeepDataInMemory)
                        _data[key] = data;
                    return data;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in reading {(string)FullName}:{key}");
            }
            return default;
        }

        /// <summary>
        /// Read value for a specific field.
        /// </summary>
        public T? Read(string key, bool force = false)
        {
            var d = ReadFull(key, force);
            return d == null ? default : d.Data;
        }

        /// <summary>
        /// Read multiple fields in one roundtrip using MGET-like StringGet with multiple keys.
        /// </summary>
        public Dictionary<string, T>? Read(IEnumerable<string> keys, bool force = false)
        {
            var result = new Dictionary<string, T>();
            try
            {
                var toFetch = force ? keys.ToArray() : keys.Where(k => !_data.ContainsKey(k)).ToArray();

                if (toFetch.Length > 0)
                {
                    var redisKeys = toFetch.Select(Compose).ToArray();
                    var values = DbReader.StringGet(redisKeys);
                    for (var i = 0; i < toFetch.Length; i++)
                    {
                        if (!values[i].IsNullOrEmpty)
                        {
                            var d = DeSerialize(values[i].ToString());
                            if (d != null)
                            {
                                if (KeepDataInMemory)
                                    _data[toFetch[i]] = d;
                                result[toFetch[i]] = d.Data;
                            }
                        }
                    }
                }

                if (KeepDataInMemory)
                {
                    foreach (var k in keys)
                        if (_data.TryGetValue(k, out var w) && !result.ContainsKey(k))
                            result[k] = w.Data;
                }

                return result;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in reading {(string)FullName}:<many>");
                return default;
            }
        }

        public async Task<T?> ReadAsync(string key, bool force = false)
        {
            if (!force && _data.TryGetValue(key, out var cached))
                return cached.Data;
            try
            {
                var temp = (await DbReader.StringGetAsync(Compose(key))).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                if (data != null)
                {
                    if (KeepDataInMemory)
                        _data[key] = data;
                    return data.Data;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in reading {(string)FullName}:{key}");
            }
            return default;
        }

        /// <summary>
        /// Write a value for a specific field to the composed key.
        /// </summary>
        public bool Write(string key, T d)
        {
            if (string.IsNullOrEmpty(key) || d == null)
                return false;
            try
            {
                var res = DbWriter.StringSet(Compose(key), Serialize(d));
                if (KeepDataInMemory)
                    _data[key] = new RedisDataWrapper<T>(d);
                Publish(key);
                return res;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in Writing {(string)FullName}:{key}");
                return false;
            }
        }

        public async Task<bool> WriteAsync(string key, T d)
        {
            if (string.IsNullOrEmpty(key) || d == null)
                return false;
            try
            {
                var res = await DbWriter.StringSetAsync(Compose(key), Serialize(d));
                if (KeepDataInMemory)
                    _data[key] = new RedisDataWrapper<T>(d);
                Publish(key);
                return res;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in Writing {(string)FullName}:{key}");
                return false;
            }
        }

        /// <summary>
        /// Bulk write. Performs pipelined StringSet on each composed key.
        /// </summary>
        public async Task<bool> WriteAsync(IDictionary<string, T> data, bool forceToPublish = false)
        {
            if (data == null || data.Count == 0) return false;
            try
            {
                var tasks = new List<Task<bool>>(data.Count);
                foreach (var kv in data)
                {
                    var val = Serialize(kv.Value);
                    tasks.Add(DbWriter.StringSetAsync(Compose(kv.Key), val));
                    if (KeepDataInMemory)
                        _data[kv.Key] = new RedisDataWrapper<T>(kv.Value);
                }
                await Task.WhenAll(tasks);
                if (forceToPublish)
                    PublishAll();
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in Writing {(string)FullName}:<many>");
                return false;
            }
        }

        /// <summary>
        /// Remove one composed key.
        /// </summary>
        public async Task<bool> RemoveAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            await DbWriter.KeyDeleteAsync(Compose(key));
            _data.TryRemove(key, out _);
            return true;
        }

        /// <summary>
        /// Remove multiple composed keys.
        /// </summary>
        public async Task<bool> RemoveAsync(IEnumerable<string> keys)
        {
            var arr = keys?.ToArray() ?? Array.Empty<string>();
            if (arr.Length == 0) return false;
            var redisKeys = arr.Select(Compose).ToArray();
            await DbWriter.KeyDeleteAsync(redisKeys);
            foreach (var k in arr)
                _data.TryRemove(k, out _);
            return true;
        }

        /// <summary>
        /// Force next read to fetch from Redis for a field.
        /// </summary>
        public void ForceToReFetch(string key)
        {
            _data.TryRemove(key, out _);
        }

        /// <summary>
        /// Clear cache for all fields.
        /// </summary>
        public void ForceToReFetchAll()
        {
            _data.Clear();
        }

        public void DoPublishAll() => PublishAll();
    }
}
