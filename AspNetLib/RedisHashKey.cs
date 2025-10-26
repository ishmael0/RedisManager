using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Santel.Redis.TypedKeys
{
    /// <summary>
    /// Represents a Redis Hash key wrapper that supports optional in-memory caching of deserialized values
    /// and provides synchronous and asynchronous CRUD operations with batching and size limiting.
    /// </summary>
    /// <typeparam name="T">Type of the value stored per hash field.</typeparam>
    public class RedisHashKey<T> : RedisCommonProperties<T>, IRedisCommonHashKeyMethods
    {
        private readonly ConcurrentDictionary<string, RedisDataWrapper<T>> _data;
        private Action PublishAll;
        private Action<string> Publish;

        /// <summary>
        /// Initializes a new instance of <see cref="RedisHashKey{T}"/>.
        /// </summary>
        /// <param name="dbIndex">Target Redis database index.</param>
        /// <param name="serialize">Optional custom value serializer (value -&gt; string). When provided the payload is wrapped.</param>
        /// <param name="deSerialize">Optional custom value deserializer (string -&gt; value).</param>
        public RedisHashKey(int dbIndex, Func<T, string>? serialize = null, Func<string, T>? deSerialize = null)
        {
            DbIndex = dbIndex;
            _data = new ConcurrentDictionary<string, RedisDataWrapper<T>>();
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
        /// Gets approximate Redis memory usage for this hash key (in bytes) using the MEMORY USAGE command.
        /// </summary>
        /// <returns>Total memory (bytes) or 0 if key does not exist.</returns>
        public long GetSize()
        {
            var keyMemoryUsage = DbReader.Execute("MEMORY", "USAGE", FullName);
            return keyMemoryUsage.IsNull ? 0 : Convert.ToInt64(keyMemoryUsage.ToString());
        }
        /// <summary>
        /// Initializes connections, logging and configuration for the hash key.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="cnWriter">Optional writer connection multiplexer.</param>
        /// <param name="cnReader">Reader connection multiplexer (required).</param>
        /// <param name="publishAll">Callback invoked when a full publish notification is required.</param>
        /// <param name="publish">Callback invoked when a single field changes.</param>
        /// <param name="fullName">Full Redis key name.</param>
        /// <param name="keepDataInMemory">If true values are cached in memory after first read.</param>
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
            if (cnWriter != null)
                DbWriter = cnWriter.GetDatabase(DbIndex);
            DbReader = cnReader.GetDatabase(DbIndex);
            FullName = fullName;
            KeepDataInMemory = keepDataInMemory;

        }
        /// <summary>
        /// Returns all field names in the hash (no paging).
        /// </summary>
        public RedisValue[] GetAllKeys()
        {
            var x = DbReader.HashKeys(FullName);
            return x;
        }
        /// <summary>
        /// Asynchronously returns all field names in the hash.
        /// </summary>
        public async Task<RedisValue[]> GetAllKeysAsync()
        {
            return await DbReader.HashKeysAsync(FullName);
        }
        /// <summary>
        /// Sets a special meta field ("____last") with a timestamp value.
        /// </summary>
        /// <param name="s">Timestamp to store.</param>
        public void SetLast(DateTime s)
        {
            DbWriter.HashSet(FullName, "____last", s.ToString());
        }
        /// <summary>
        /// Reads a group of keys optionally in chunks. When caching is enabled only missing keys trigger Redis calls.
        /// </summary>
        /// <param name="keys">Field names to fetch.</param>
        /// <param name="force">If true forces re-fetch even if cached.</param>
        /// <param name="chunkSize">Number of keys per HashGet batch.</param>
        /// <returns>Dictionary of key/value pairs or null on failure.</returns>
        public Dictionary<string, T>? Read(IEnumerable<string> keys, bool force = false, int chunkSize = 10)
        {
            try
            {
                var result = new Dictionary<string, T>();
                List<string[]> query;
                if (force)
                    query = keys.Chunk(chunkSize).ToList();
                else
                    query = keys.Where(c => !_data.ContainsKey(c)).Chunk(chunkSize).ToList();

                query.ForEach(e =>
                {
                    var tempArray = DbReader.HashGet(FullName, e.Select(key => (RedisValue)key).ToArray());
                    for (var i = 0; i < tempArray.Length; i++)
                        if (!string.IsNullOrEmpty(tempArray[i]))
                        {
                            var d = DeSerialize(tempArray[i]!);
                            if (d != null)
                            {
                                if (KeepDataInMemory)
                                    _data.TryAdd(e[i], d);
                                else
                                    result[e[i]] = d.Data;
                            }
                        }
                });
                if (KeepDataInMemory)
                    return _data.Where(kv => keys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value.Data);
                else return result;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in reading {FullName}");
            }

            return default;
        }
        /// <summary>
        /// Reads a single field value.
        /// </summary>
        /// <param name="key">Field name.</param>
        /// <param name="force">If true ignores cached entry.</param>
        /// <returns>The value or default if not found.</returns>
        public T? Read(string key, bool force = false)
        {
            var d = ReadFull(key, force);
            if (d == null) return default;
            return d.Data;
        }
        /// <summary>
        /// Reads a single field returning the wrapper metadata (timestamp etc.).
        /// </summary>
        /// <param name="key">Field name.</param>
        /// <param name="force">If true forces a Redis read even if cached.</param>
        /// <returns>Wrapper with data and metadata or null.</returns>
        public RedisDataWrapper<T>? ReadFull(string key, bool force = false)
        {
            if (!force && _data.ContainsKey(key))
                return _data[key];
            try
            {
                var temp = DbReader.HashGet(FullName, key).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                if (data != null)
                {
                    if (KeepDataInMemory)
                        _data.TryAdd(key, data);
                    return data;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in reading {FullName}:{key}");
            }
            return default;
        }
        /// <summary>
        /// Asynchronously reads a single field value.
        /// </summary>
        /// <param name="key">Field name.</param>
        /// <param name="force">If true re-fetches from Redis.</param>
        /// <returns>Value or default.</returns>
        public async Task<T?> ReadAsync(string key, bool force = false)
        {
            if (!force && _data.ContainsKey(key))
                return _data[key].Data;
            try
            {
                var temp = (await DbReader.HashGetAsync(FullName, key)).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                if (data != null)
                {
                    if (KeepDataInMemory)
                        _data.TryAdd(key, data);
                    return data.Data;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in reading {FullName}:{key}");
            }
            return default;
        }
        /// <summary>
        /// Asynchronously reads a batch of fields (with optional caching and chunking).
        /// Always returns a dictionary (possibly empty) unless a fatal error occurs.
        /// </summary>
        /// <param name="keys">List of field names.</param>
        /// <param name="force">If true re-reads all keys from Redis.</param>
        /// <param name="chunkSize">Max fields per batch HashGet.</param>
        /// <returns>Dictionary with available values.</returns>
        public async Task<Dictionary<string, T>?> ReadAsync(List<string> keys, bool force = false, int chunkSize = 5)
        {
            var result = new Dictionary<string, T>();
            try
            {
                List<string[]> query;

                if (force)
                    query = keys.Chunk(chunkSize).ToList();
                else
                {
                    query = keys.Where(c => !_data.ContainsKey(c)).Chunk(chunkSize).ToList();
                    foreach (var cached in keys.Where(c => _data.ContainsKey(c)))
                        result[cached] = _data[cached].Data;
                }

                foreach (var item in query)
                    try
                    {
                        var tempArray = await DbReader.HashGetAsync(FullName, item.Select(key => (RedisValue)key).ToArray());
                        for (var i = 0; i < tempArray.Length; i++)
                            if (!string.IsNullOrEmpty(tempArray[i]))
                            {
                                var d = DeSerialize(tempArray[i]!);
                                if (d != null)
                                {
                                    if (KeepDataInMemory)
                                        _data[item[i]] = d;
                                    result[item[i]] = d.Data; // Always populate result for consistency
                                }
                            }
                    }
                    catch (Exception e)
                    {
                        Logger.LogError(e, $"In RedisManager, in reading {FullName}", e);
                    }
                if (KeepDataInMemory)
                {
                    // Ensure all requested keys are present if cached
                    foreach (var k in keys)
                        if (_data.TryGetValue(k, out var wrapper) && !result.ContainsKey(k))
                            result[k] = wrapper.Data;
                }
                return result;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in reading {FullName}");
            }

            return result;
        }
        /// <summary>
        /// Removes a single field from the hash asynchronously.
        /// </summary>
        /// <param name="key">Field name.</param>
        /// <returns>True if request queued; false on invalid input.</returns>
        public async Task<bool> RemoveAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            await DbWriter.HashDeleteAsync(FullName, key);
            return true;
        }
        /// <summary>
        /// Removes multiple fields in one operation.
        /// </summary>
        /// <param name="keys">Field names.</param>
        public async Task<bool> RemoveAsync(RedisValue[] keys)
        {
            await DbWriter.HashDeleteAsync(FullName, keys);
            return true;
        }
        /// <summary>
        /// Deletes the entire hash key.
        /// </summary>
        public async Task<bool> RemoveAsync()
        {
            await DbWriter.KeyDeleteAsync(FullName);
            return true;
        }
        /// <summary>
        /// Determines whether adding a number of additional entries would exceed the configured limit (4000 items).
        /// </summary>
        /// <param name="additional">Expected number of new fields.</param>
        /// <returns>True if limit would be exceeded.</returns>
        private bool IsLimitExceeded(int additional = 0)
        {
            try
            {
                var len = (int)DbWriter.HashLength(FullName);
                return len + additional > 4000;
            }
            catch
            {
                // Fallback to previous expensive approach only on failure
                try
                {
                    var len = DbWriter.HashKeys(FullName).Length;
                    return len + additional > 4000;
                }
                catch { return true; }
            }
        }
        /// <summary>
        /// Writes or overwrites a single field value.
        /// </summary>
        /// <param name="key">Field name.</param>
        /// <param name="d">Value.</param>
        /// <returns>True if field was set.</returns>
        public bool Write(string key, T d)
        {
            if (string.IsNullOrEmpty(key) || d == null) return false;
            try
            {
                if (IsLimitExceeded())
                {
                    Logger.LogInformation($"in {nameof(Write)} method of RedisManager | for item {FullName}, key is larger than 4000 records!");
                    return false;
                }
                var res = DbWriter.HashSet(FullName, key, Serialize(d));
                Publish(key);
                return res;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in Writing {FullName}:{key}");
                return false;
            }

        }
        /// <summary>
        /// Bulk writes entries (no chunking). Fails if new total would exceed the limit.
        /// </summary>
        /// <param name="data">Entries to write.</param>
        /// <param name="forceToPublish">If true triggers a publish-all callback.</param>
        public bool Write(IDictionary<string, T> data, bool forceToPublish = false)
        {
            if (data == null || data.Count == 0)
                return false;
            try
            {
                if (IsLimitExceeded(data.Count))
                {
                    Logger.LogInformation($"in {nameof(Write)} method of RedisManager | for item {FullName}, key is larger than 4000 records!");
                    return false;
                }
                DbWriter.HashSet(FullName, data.Select(c => new HashEntry(c.Key, Serialize(c.Value))).ToArray());
                if (forceToPublish)
                    PublishAll();
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in Writing {FullName}");
                return false;
            }
        }

        /// <summary>
        /// Invokes the publish-all callback manually.
        /// </summary>
        public void DoPublishAll()
        {
            PublishAll();
        }
        /// <summary>
        /// Asynchronously writes a single field value.
        /// </summary>
        /// <param name="key">Field name.</param>
        /// <param name="d">Value.</param>
        /// <returns>True if set successfully.</returns>
        public async Task<bool> WriteAsync(string key, T d)
        {
            if (string.IsNullOrEmpty(key) || d == null) return false;
            try
            {
                if (IsLimitExceeded())
                {
                    Logger.LogInformation($"in {nameof(WriteAsync)} method of RedisManager | for item {FullName}, key is larger than 4000 records!");
                    return false;
                }
                var res = await DbWriter.HashSetAsync(FullName, key, Serialize(d));
                Publish(key);
                return res;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in Writing {FullName}:{key}");
                return false;
            }
        }
        /// <summary>
        /// Asynchronously bulk writes entries with size-based chunking.
        /// </summary>
        /// <param name="data">Entries to write.</param>
        /// <param name="forceToPublish">If true triggers publish-all after completion.</param>
        /// <param name="maxChunkSizeInBytes">Maximum serialized byte-length per chunk.</param>
        /// <returns>True if write operations succeed.</returns>
        public async Task<bool> WriteAsync(IDictionary<string, T> data, bool forceToPublish = false, int maxChunkSizeInBytes = 500000)
        {
            if (data == null || data.Count == 0)
                return false;
            try
            {
                if (IsLimitExceeded(data.Count))
                {
                    Logger.LogInformation($"in {nameof(WriteAsync)} method of RedisManager | for item {FullName}, key is larger than 4000 records!");
                    return false;
                }

                var chunks = ChunkDataBySize(data, maxChunkSizeInBytes);

                foreach (var chunk in chunks)
                    await DbWriter.HashSetAsync(FullName, chunk.ToArray());
                if (forceToPublish)
                    PublishAll();
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"In RedisManager, in Writing {FullName}");
                return false;
            }
        }
        /// <summary>
        /// Splits large bulk data into bite-sized chunks based on serialized length.
        /// </summary>
        /// <param name="data">Source data entries.</param>
        /// <param name="maxChunkSizeInBytes">Max bytes per chunk.</param>
        private IEnumerable<List<HashEntry>> ChunkDataBySize(ICollection<KeyValuePair<string, T>> data, int maxChunkSizeInBytes)
        {
            var currentChunk = new List<HashEntry>();
            var currentChunkSize = 0;

            foreach (var entry in data)
            {
                var ser = Serialize(entry.Value);
                if (currentChunkSize + ser.Length > maxChunkSizeInBytes)
                {
                    yield return currentChunk;
                    currentChunk.Clear();
                    currentChunkSize = 0;
                }
                currentChunk.Add(new HashEntry(entry.Key, ser));
                currentChunkSize += ser.Length;
            }

            if (currentChunk.Count > 0)
                yield return currentChunk;
        }
        /// <summary>
        /// Removes a specific cached entry (if caching is enabled) so next read forces a Redis fetch.
        /// </summary>
        /// <param name="key">Field name.</param>
        public void ForceToReFetch(string key)
        {
            if (_data != null && _data.ContainsKey(key))
                _data.TryRemove(key, out _);
        }
        /// <summary>
        /// Clears the entire in-memory cache for this hash.
        /// </summary>
        public void ForceToReFetchAll()
        {
            _data.Clear();
        }
        /// <summary>
        /// Indexer shortcut to read a value (cached or from Redis).
        /// </summary>
        /// <param name="key">Field name.</param>
        public T? this[string key]
        {
            get => Read(key);
        }

        /// <summary>
        /// Concurrency test helper that triggers cache clearing while multiple reads occur.
        /// </summary>
        /// <param name="v">Hash key instance.</param>
        public static async Task TestConcurrency_IN_ForceToReFetchAll(RedisHashKey<int> v)
        {
            await v.WriteAsync(Enumerable.Range(1, 10000).ToDictionary(c => c.ToString(), c => c));
            var tasks = new List<Task>();
            var keys = v.GetAllKeys().Select(c => (string)c!).ToList();
            tasks.Add(Task.Run(() => v.ForceToReFetchAll()));
            foreach (var key in keys)
                tasks.Add(Task.Run(() => v.ReadAsync(key)));
            await Task.WhenAll(tasks);
        }
    }
}
