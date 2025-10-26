using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Santel.Redis.TypedKeys
{
    
    public class RedisPrefixedKeys<T> : RedisCommonProperties<T>, IRedisHashKey
    {
        private readonly ConcurrentDictionary<string, RedisDataWrapper<T>> _data = new();
        public RedisPrefixedKeys(int dbIndex, Func<T, string>? serialize = null, Func<string, T>? deSerialize = null)
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
                if (temp != null && !string.IsNullOrEmpty(temp.Data))
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
            Writer = ContextConfig.Reader.GetDatabase(DbIndex);
        }
        private RedisKey Compose(string key) => (RedisKey)$"{FullName}:{key}";
        
        public RedisDataWrapper<T>? ReadFull(string key, bool force = false)
        {
            if (!force && _data.TryGetValue(key, out var cached))
                return cached;
            try
            {
                var temp = Reader.StringGet(Compose(key)).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                if (data != null)
                {
                    if (ContextConfig.KeepDataInMemory)
                        _data[key] = data;
                    return data;
                }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}:{key}");
            }
            return default;
        }

        public T? Read(string key, bool force = false)
        {
            var d = ReadFull(key, force);
            return d == null ? default : d.Data;
        }
        public Dictionary<string, T>? Read(IEnumerable<string> keys, bool force = false)
        {
            var result = new Dictionary<string, T>();
            try
            {
                var toFetch = force ? keys.ToArray() : keys.Where(k => !_data.ContainsKey(k)).ToArray();

                if (toFetch.Length > 0)
                {
                    var redisKeys = toFetch.Select(Compose).ToArray();
                    var values = Reader.StringGet(redisKeys);
                    for (var i = 0; i < toFetch.Length; i++)
                    {
                        if (!values[i].IsNullOrEmpty)
                        {
                            var d = DeSerialize(values[i].ToString());
                            if (d != null)
                            {
                                if (ContextConfig.KeepDataInMemory)
                                    _data[toFetch[i]] = d;
                                result[toFetch[i]] = d.Data;
                            }
                        }
                    }
                }

                if (ContextConfig.KeepDataInMemory)
                {
                    foreach (var k in keys)
                        if (_data.TryGetValue(k, out var w) && !result.ContainsKey(k))
                            result[k] = w.Data;
                }

                return result;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}:<many>");
                return default;
            }
        }
        public async Task<T?> ReadAsync(string key, bool force = false)
        {
            if (!force && _data.TryGetValue(key, out var cached))
                return cached.Data;
            try
            {
                var temp = (await Reader.StringGetAsync(Compose(key))).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                if (data != null)
                {
                    if (ContextConfig.KeepDataInMemory)
                        _data[key] = data;
                    return data.Data;
                }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}:{key}");
            }
            return default;
        }
        public bool Write(string key, T d)
        {
            if (string.IsNullOrEmpty(key) || d == null)
                return false;
            try
            {
                var res = Writer.StringSet(Compose(key), Serialize(d));
                if (ContextConfig.KeepDataInMemory)
                    _data[key] = new RedisDataWrapper<T>(d);
                ContextConfig.PublishByKey(this,key);
                return res;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}:{key}");
                return false;
            }
        }
        public async Task<bool> WriteAsync(string key, T d)
        {
            if (string.IsNullOrEmpty(key) || d == null)
                return false;
            try
            {
                var res = await Writer.StringSetAsync(Compose(key), Serialize(d));
                if (ContextConfig.KeepDataInMemory)
                    _data[key] = new RedisDataWrapper<T>(d);
                ContextConfig.PublishByKey(this, key);
                return res;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}:{key}");
                return false;
            }
        }
        public async Task<bool> WriteAsync(IDictionary<string, T> data, bool forceToPublish = false)
        {
            if (data == null || data.Count == 0) return false;
            try
            {
                var tasks = new List<Task<bool>>(data.Count);
                foreach (var kv in data)
                {
                    var val = Serialize(kv.Value);
                    tasks.Add(Writer.StringSetAsync(Compose(kv.Key), val));
                    if (ContextConfig.KeepDataInMemory)
                        _data[kv.Key] = new RedisDataWrapper<T>(kv.Value);
                }
                await Task.WhenAll(tasks);
                if (forceToPublish)
              ContextConfig.Publish(this);
                return true;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}:<many>");
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
            await Writer.KeyDeleteAsync(Compose(key));
            _data.TryRemove(key, out _);
            return true;
        }
        public async Task<bool> RemoveAsync(IEnumerable<string> keys)
        {
            var arr = keys?.ToArray() ?? Array.Empty<string>();
            if (arr.Length == 0) return false;
            var redisKeys = arr.Select(Compose).ToArray();
            await Writer.KeyDeleteAsync(redisKeys);
            foreach (var k in arr)
                _data.TryRemove(k, out _);
            return true;
        }
        public void ForceToReFetch(string key)
        {
            if (_data != null && _data.ContainsKey(key))
                _data.TryRemove(key, out _);
        }
        public void ForceToReFetch()
        {
            _data.Clear();
        }
    }
}
