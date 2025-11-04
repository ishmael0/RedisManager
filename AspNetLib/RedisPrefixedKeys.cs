using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Threading.Tasks;

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
            Writer = ContextConfig.Writer.GetDatabase(DbIndex);
        }

        public long GetSize()
        {
            try
            {
                long totalSize = 0;
                var server = ContextConfig.Reader.GetEndPoints().FirstOrDefault();
                if (server == null) return 0;

                var redisServer = ContextConfig.Reader.GetServer(server);
                var pattern = $"{FullName}:*";

                // Use SCAN (via pageSize) instead of KEYS to avoid blocking Redis
                // Iterates through all prefixed keys and sums their memory usage
                foreach (var key in redisServer.Keys(DbIndex, pattern: pattern, pageSize: 250))
                {
                    var keyMemoryUsage = Reader.Execute("MEMORY", "USAGE", key);
                    if (!keyMemoryUsage.IsNull)
                    {
                        var s = keyMemoryUsage.ToString();
                        if (long.TryParse(s, out var size))
                            totalSize += size;
                    }
                }

                return totalSize;
            }
            catch (RedisServerException ex)
            {
                // MEMORY USAGE may be disabled on some Redis providers. Log and return 0.
                ContextConfig.Logger?.LogWarning(ex, $"MEMORY USAGE not supported or failed for {FullName}");
                return 0;
            }
            catch (Exception ex)
            {
                ContextConfig.Logger?.LogError(ex, $"Error getting size for {FullName}");
                return 0;
            }
        }

        private RedisKey Compose(string key) => (RedisKey)$"{FullName}:{key}";


        public T? Read(string key, bool force = false)
        {
            if (!force && _data.TryGetValue(key, out var cached))
                return cached.Data;
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
                    return data.Data;
                }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}:{key}");
            }
            return default;
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

        public Dictionary<string, T>? ReadInChunks(IEnumerable<string> keys, int chunkSize = 1000, bool force = false)
        {
            if (chunkSize <= 0)
                throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

            var result = new Dictionary<string, T>();
            try
            {
                foreach (var chunk in keys.Chunk(chunkSize))
                {
                    var chunkResult = Read(chunk, force);

                    if (chunkResult != null)
                    {
                        foreach (var kv in chunkResult)
                        {
                            result[kv.Key] = kv.Value;
                        }
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading chunks {FullName}:<many>");
                return default;
            }
        }
        public async Task<Dictionary<string, T>?> ReadInChunksAsync(IEnumerable<string> keys, int chunkSize = 1000, bool force = false)
        {
            if (chunkSize <= 0)
                throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

            var result = new Dictionary<string, T>();
            try
            {
                foreach (var chunk in keys.Chunk(chunkSize))
                {
                    var chunkResult = await ReadAsync(chunk, force);
                    if (chunkResult != null)
                    {
                        foreach (var kv in chunkResult)
                        {
                            result[kv.Key] = kv.Value;
                        }
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading chunks {FullName}:<many>");
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
        public async Task<Dictionary<string, T>?> ReadAsync(IEnumerable<string> keys, bool force = false)
        {
            var result = new Dictionary<string, T>();
            try
            {
                var toFetch = force ? keys.ToArray() : keys.Where(k => !_data.ContainsKey(k)).ToArray();

                if (toFetch.Length > 0)
                {
                    var redisKeys = toFetch.Select(Compose).ToArray();
                    var values = await Reader.StringGetAsync(redisKeys);
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

        public bool Write(string key, T d, TimeSpan? expiry = null)
        {
            if (string.IsNullOrEmpty(key) || d == null)
                return false;
            try
            {
                var res = Writer.StringSet(Compose(key), Serialize(d), expiry);
                if (ContextConfig.KeepDataInMemory)
                    _data[key] = new RedisDataWrapper<T>(d);
                ContextConfig.Publish(this, key);
                return res;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}:{key}");
                return false;
            }
        }
        public bool Write(IDictionary<string, T> data, TimeSpan? expiry = null)
        {
            if (data == null || data.Count == 0) return false;
            try
            {
                foreach (var kv in data)
                {
                    var val = Serialize(kv.Value);
                    Writer.StringSet(Compose(kv.Key), val, expiry);
                    if (ContextConfig.KeepDataInMemory)
                        _data[kv.Key] = new RedisDataWrapper<T>(kv.Value);
                }
                ContextConfig.Publish(this, data.Keys);
                return true;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}:<many>");
                return false;
            }
        }

        public bool WriteInChunks(IDictionary<string, T> data, int chunkSize = 1000, TimeSpan? expiry = null)
        {
            if (data == null || data.Count == 0) return false;
            if (chunkSize <= 0)
                throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

            try
            {
                foreach (var chunk in data.Chunk(chunkSize))
                {
                    var chunkDict = chunk.ToDictionary(kv => kv.Key, kv => kv.Value);
                    var success = Write(chunkDict, expiry);
                    if (!success)
                        return false;
                }
                return true;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in writing chunks {FullName}:<many>");
                return false;
            }
        }
        public async Task<bool> WriteInChunksAsync(IDictionary<string, T> data, int chunkSize = 1000, TimeSpan? expiry = null)
        {
            if (data == null || data.Count == 0) return false;
            if (chunkSize <= 0)
                throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

            try
            {
                foreach (var chunk in data.Chunk(chunkSize))
                {
                    var chunkDict = chunk.ToDictionary(kv => kv.Key, kv => kv.Value);
                    var success = await WriteAsync(chunkDict, expiry);
                    if (!success)
                        return false;
                }
                return true;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in writing chunks {FullName}:<many>");
                return false;
            }
        }

        public async Task<bool> WriteAsync(string key, T d, TimeSpan? expiry = null)
        {
            if (string.IsNullOrEmpty(key) || d == null)
                return false;
            try
            {
                var res = await Writer.StringSetAsync(Compose(key), Serialize(d), expiry);
                if (ContextConfig.KeepDataInMemory)
                    _data[key] = new RedisDataWrapper<T>(d);
                ContextConfig.Publish(this, key);
                return res;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}:{key}");
                return false;
            }
        }
        public async Task<bool> WriteAsync(IDictionary<string, T> data, TimeSpan? expiry = null)
        {
            if (data == null || data.Count == 0) return false;
            try
            {
                var tasks = new List<Task<bool>>(data.Count);
                foreach (var kv in data)
                {
                    var val = Serialize(kv.Value);
                    tasks.Add(Writer.StringSetAsync(Compose(kv.Key), val, expiry));
                    if (ContextConfig.KeepDataInMemory)
                        _data[kv.Key] = new RedisDataWrapper<T>(kv.Value);
                }
                await Task.WhenAll(tasks);
                ContextConfig.Publish(this, data.Keys);
                return true;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}:<many>");
                return false;
            }
        }

        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            Writer.KeyDelete(Compose(key));
            _data.TryRemove(key, out _);
            return true;
        }
        public bool Remove(IEnumerable<string> keys)
        {
            var keyArray = keys?.ToArray();
            if (keyArray == null || keyArray.Length == 0) return false;
            var redisKeys = keyArray.Select(Compose).ToArray();
            Writer.KeyDelete(redisKeys);
            foreach (var k in keyArray)
                _data.TryRemove(k, out _);
            return true;
        }


        public bool RemoveInChunks(IEnumerable<string> keys, int chunkSize = 1000)
        {
            if (keys == null) return false;
            if (chunkSize <= 0)
                throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

            try
            {
                foreach (var chunk in keys.Chunk(chunkSize))
                {
                    var success = Remove(chunk);
                    if (!success)
                        return false;
                }
                return true;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in removing chunks {FullName}:<many>");
                return false;
            }
        }

        public async Task<bool> RemoveInChunksAsync(IEnumerable<string> keys, int chunkSize = 1000)
        {
            if (keys == null) return false;
            if (chunkSize <= 0)
                throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

            try
            {
                foreach (var chunk in keys.Chunk(chunkSize))
                {
                    var success = await RemoveAsync(chunk);
                    if (!success)
                        return false;
                }
                return true;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in removing chunks {FullName}:<many>");
                return false;
            }
        }

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
            var keyArray = keys?.ToArray();
            if (keyArray == null || keyArray.Length == 0) return false;
            var redisKeys = keyArray.Select(Compose).ToArray();
            await Writer.KeyDeleteAsync(redisKeys);
            foreach (var k in keyArray)
                _data.TryRemove(k, out _);
            return true;
        }
        public void InvalidateCache(string key)
        {
            if (_data != null && _data.ContainsKey(key))
                _data.TryRemove(key, out _);
        }
        public void InvalidateCache(IEnumerable<string> keys)
        {
            if (_data != null)
            {
                foreach (var key in keys)
                {
                    _data.TryRemove(key, out _);
                }
            }
        }
        public void InvalidateCache()
        {
            _data.Clear();
        }
    }
}
