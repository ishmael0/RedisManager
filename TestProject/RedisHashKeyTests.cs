using Moq;
using StackExchange.Redis;
using Santel.Redis.TypedKeys;
using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class RedisHashKeyTests
    {
        private Mock<IConnectionMultiplexer> _mockConnection;
        private Mock<IDatabase> _mockDatabase;
        private Mock<ILogger> _mockLogger;
        private RedisDBContextModuleConfigs _contextConfig;

        public RedisHashKeyTests()
        {
            _mockConnection = new Mock<IConnectionMultiplexer>();
            _mockDatabase = new Mock<IDatabase>();
            _mockLogger = new Mock<ILogger>();

            _mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
             .Returns(_mockDatabase.Object);

            _contextConfig = new RedisDBContextModuleConfigs
            {
                Reader = _mockConnection.Object,
                Writer = _mockConnection.Object,
                Logger = _mockLogger.Object,
                KeepDataInMemory = true
            };
        }

        [Fact]
        public void Constructor_ShouldInitializeWithDefaultSerialization()
        {
            // Arrange & Act
            var redisHashKey = new RedisHashKey<string>(0);

            // Assert
            Assert.Equal(0, redisHashKey.DbIndex);
            Assert.NotNull(redisHashKey.Serialize);
            Assert.NotNull(redisHashKey.DeSerialize);
        }

        [Fact]
        public void Write_SingleKey_ShouldReturnTrue_WhenSuccessful()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            _mockDatabase.Setup(x => x.HashSet(
            It.IsAny<StackExchange.Redis.RedisKey>(),
           It.IsAny<RedisValue>(),
        It.IsAny<RedisValue>(),
          It.IsAny<When>(),
                  It.IsAny<CommandFlags>()))
            .Returns(true);

            // Act
            var result = redisHashKey.Write("field1", "value1");

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.HashSet(
              It.IsAny<StackExchange.Redis.RedisKey>(),
           It.IsAny<RedisValue>(),
               It.IsAny<RedisValue>(),
          It.IsAny<When>(),
        It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Write_SingleKey_ShouldReturnFalse_WhenKeyIsNull()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            // Act
            var result = redisHashKey.Write(null!, "value1");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Write_SingleKey_ShouldReturnFalse_WhenDataIsNull()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            // Act
            var result = redisHashKey.Write("field1", null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Write_Dictionary_ShouldReturnTrue_WhenSuccessful()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var data = new Dictionary<string, string>
        {
    { "field1", "value1" },
     { "field2", "value2" }
      };

            _mockDatabase.Setup(x => x.HashSet(
            It.IsAny<StackExchange.Redis.RedisKey>(),
     It.IsAny<HashEntry[]>(),
  It.IsAny<CommandFlags>()));

            // Act
            var result = redisHashKey.Write(data);

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.HashSet(
     It.IsAny<StackExchange.Redis.RedisKey>(),
    It.IsAny<HashEntry[]>(),
      It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Write_Dictionary_ShouldReturnFalse_WhenDataIsEmpty()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var data = new Dictionary<string, string>();

            // Act
            var result = redisHashKey.Write(data);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Read_SingleKey_ShouldReturnDefault_WhenFieldDoesNotExist()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            _mockDatabase.Setup(x => x.HashGet(
            It.IsAny<StackExchange.Redis.RedisKey>(),
           It.IsAny<RedisValue>(),
          It.IsAny<CommandFlags>()))
                  .Returns(RedisValue.Null);

            // Act
            var result = redisHashKey.Read("field1");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Read_MultipleKeys_ShouldReturnDictionary()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var keys = new[] { "field1", "field2" };

            _mockDatabase.Setup(x => x.HashGet(
          It.IsAny<StackExchange.Redis.RedisKey>(),
      It.IsAny<RedisValue[]>(),
     It.IsAny<CommandFlags>()))
                .Returns(new[] { RedisValue.Null, RedisValue.Null });

            // Act
            var result = redisHashKey.Read(keys);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task ReadAsync_SingleKey_ShouldReturnDefault_WhenFieldDoesNotExist()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            _mockDatabase.Setup(x => x.HashGetAsync(
                 It.IsAny<StackExchange.Redis.RedisKey>(),
              It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            // Act
            var result = await redisHashKey.ReadAsync("field1");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task WriteAsync_SingleKey_ShouldReturnTrue_WhenSuccessful()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            _mockDatabase.Setup(x => x.HashSetAsync(
        It.IsAny<StackExchange.Redis.RedisKey>(),
    It.IsAny<RedisValue>(),
        It.IsAny<RedisValue>(),
     It.IsAny<When>(),
    It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);

            // Act
            var result = await redisHashKey.WriteAsync("field1", "value1");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Remove_SingleKey_ShouldReturnTrue_WhenSuccessful()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            _mockDatabase.Setup(x => x.HashDelete(
                     It.IsAny<StackExchange.Redis.RedisKey>(),
             It.IsAny<RedisValue>(),
           It.IsAny<CommandFlags>()))
                .Returns(true);

            // Act
            var result = redisHashKey.Remove("field1");

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.HashDelete(
          It.IsAny<StackExchange.Redis.RedisKey>(),
                 It.IsAny<RedisValue>(),
          It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Remove_MultipleKeys_ShouldReturnTrue_WhenSuccessful()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var keys = new[] { "field1", "field2" };

            _mockDatabase.Setup(x => x.HashDelete(
                  It.IsAny<StackExchange.Redis.RedisKey>(),
                  It.IsAny<RedisValue[]>(),
          It.IsAny<CommandFlags>()))
                .Returns(2);

            // Act
            var result = redisHashKey.Remove(keys);

            // Assert
            Assert.True(result);
        }


        [Fact]
        public async Task RemoveAsync_SingleKey_ShouldReturnTrue()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            _mockDatabase.Setup(x => x.HashDeleteAsync(
          It.IsAny<StackExchange.Redis.RedisKey>(),
                It.IsAny<RedisValue>(),
     It.IsAny<CommandFlags>()))
                  .ReturnsAsync(true);

            // Act
            var result = await redisHashKey.RemoveAsync("field1");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void InvalidateCache_SingleKey_ShouldClearCachedValue()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            _mockDatabase.Setup(x => x.HashSet(
     It.IsAny<StackExchange.Redis.RedisKey>(),
     It.IsAny<RedisValue>(),
      It.IsAny<RedisValue>(),
    It.IsAny<When>(),
                  It.IsAny<CommandFlags>()))
                  .Returns(true);

            _mockDatabase.Setup(x => x.HashGet(
                   It.IsAny<StackExchange.Redis.RedisKey>(),
             It.IsAny<RedisValue>(),
        It.IsAny<CommandFlags>()))
       .Returns(RedisValue.Null);

            redisHashKey.Write("field1", "value1");

            // Act
            redisHashKey.InvalidateCache("field1");
            var result = redisHashKey.Read("field1");

            // Assert
            _mockDatabase.Verify(x => x.HashGet(
        It.IsAny<StackExchange.Redis.RedisKey>(),
    It.IsAny<RedisValue>(),
     It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void InvalidateCache_AllKeys_ShouldClearAllCachedValues()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            // Act
            redisHashKey.InvalidateCache();

            // Assert - No exception should be thrown
            Assert.True(true);
        }

        [Fact]
        public void GetAllKeys_ShouldReturnKeys()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var expectedKeys = new RedisValue[] { "field1", "field2" };
            _mockDatabase.Setup(x => x.HashKeys(
           It.IsAny<StackExchange.Redis.RedisKey>(),
          It.IsAny<CommandFlags>()))
                .Returns(expectedKeys);

            // Act
            var result = redisHashKey.GetAllKeys();

            // Assert
            Assert.Equal(expectedKeys, result);
        }

        [Fact]
        public async Task GetAllKeysAsync_ShouldReturnKeys()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var expectedKeys = new RedisValue[] { "field1", "field2" };
            _mockDatabase.Setup(x => x.HashKeysAsync(
                It.IsAny<StackExchange.Redis.RedisKey>(),
            It.IsAny<CommandFlags>()))
                 .ReturnsAsync(expectedKeys);

            // Act
            var result = await redisHashKey.GetAllKeysAsync();

            // Assert
            Assert.Equal(expectedKeys, result);
        }

        [Fact]
        public void ReadInChunks_ShouldProcessInChunks()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var keys = Enumerable.Range(1, 2500).Select(i => $"field{i}");

            _mockDatabase.Setup(x => x.HashGet(
      It.IsAny<StackExchange.Redis.RedisKey>(),
           It.IsAny<RedisValue[]>(),
         It.IsAny<CommandFlags>()))
                     .Returns((StackExchange.Redis.RedisKey key, RedisValue[] fields, CommandFlags flags) =>
       fields.Select(_ => RedisValue.Null).ToArray());

            // Act
            var result = redisHashKey.ReadInChunks(keys, chunkSize: 1000);

            // Assert
            Assert.NotNull(result);
            // Should make 3 calls (1000 + 1000 + 500)
            _mockDatabase.Verify(x => x.HashGet(
     It.IsAny<StackExchange.Redis.RedisKey>(),
  It.IsAny<RedisValue[]>(),
         It.IsAny<CommandFlags>()), Times.Exactly(3));
        }

        [Fact]
        public void ReadInChunks_ShouldThrow_WhenChunkSizeIsZero()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var keys = new[] { "field1", "field2" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => redisHashKey.ReadInChunks(keys, chunkSize: 0));
        }

        [Fact]
        public void WriteInChunks_ShouldProcessInChunks()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var data = Enumerable.Range(1, 2500)
            .ToDictionary(i => $"field{i}", i => $"value{i}");

            _mockDatabase.Setup(x => x.HashSet(
            It.IsAny<StackExchange.Redis.RedisKey>(),
          It.IsAny<HashEntry[]>(),
        It.IsAny<CommandFlags>()));

            // Act
            var result = redisHashKey.WriteInChunks(data, chunkSize: 1000);

            // Assert
            Assert.True(result);
            // Should make 3 calls (1000 + 1000 + 500)
            _mockDatabase.Verify(x => x.HashSet(
         It.IsAny<StackExchange.Redis.RedisKey>(),
  It.IsAny<HashEntry[]>(),
           It.IsAny<CommandFlags>()), Times.Exactly(3));
        }

        [Fact]
        public void RemoveInChunks_ShouldProcessInChunks()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            var keys = Enumerable.Range(1, 2500).Select(i => $"field{i}");

            _mockDatabase.Setup(x => x.HashDelete(
                 It.IsAny<StackExchange.Redis.RedisKey>(),
               It.IsAny<RedisValue[]>(),
          It.IsAny<CommandFlags>()))
                .Returns(1000);

            // Act
            var result = redisHashKey.RemoveInChunks(keys, chunkSize: 1000);

            // Assert
            Assert.True(result);
            // Should make 3 calls (1000 + 1000 + 500)
            _mockDatabase.Verify(x => x.HashDelete(
                It.IsAny<StackExchange.Redis.RedisKey>(),
          It.IsAny<RedisValue[]>(),
           It.IsAny<CommandFlags>()), Times.Exactly(3));
        }

        [Fact]
        public void Indexer_ShouldCallRead()
        {
            // Arrange
            var redisHashKey = new RedisHashKey<string>(0);
            redisHashKey.Init(_contextConfig, "test:hash");

            _mockDatabase.Setup(x => x.HashGet(
                        It.IsAny<StackExchange.Redis.RedisKey>(),
                        It.IsAny<RedisValue>(),
                  It.IsAny<CommandFlags>()))
          .Returns(RedisValue.Null);

            // Act
            var result = redisHashKey["field1"];

            // Assert
            _mockDatabase.Verify(x => x.HashGet(
  It.IsAny<StackExchange.Redis.RedisKey>(),
     It.IsAny<RedisValue>(),
      It.IsAny<CommandFlags>()), Times.Once);
        }
    }
}
