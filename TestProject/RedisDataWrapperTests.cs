using Santel.Redis.TypedKeys;

namespace TestProject
{
    public class RedisDataWrapperTests
    {
        [Fact]
        public void Constructor_ShouldThrowArgumentNullException_WhenDataIsNull()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new RedisDataWrapper<string>(null!));
        }

        [Fact]
        public void Constructor_ShouldInitializeData()
        {
            // Arrange
            var testData = "test value";

            // Act
            var wrapper = new RedisDataWrapper<string>(testData);

            // Assert
            Assert.Equal(testData, wrapper.Data);
        }

        [Fact]
        public void Constructor_ShouldSetDateTime()
        {
            // Arrange
            var beforeTime = DateTime.UtcNow;

            // Act
            var wrapper = new RedisDataWrapper<string>("test");
            var afterTime = DateTime.UtcNow;

            // Assert
            Assert.True(wrapper.DateTime >= beforeTime && wrapper.DateTime <= afterTime);
        }

        [Fact]
        public void Constructor_ShouldSetPersianLastUpdate()
        {
            // Arrange & Act
            var wrapper = new RedisDataWrapper<string>("test");

            // Assert
            Assert.NotNull(wrapper.PersianLastUpdate);
            Assert.NotEmpty(wrapper.PersianLastUpdate);
            // Persian date format: yyyy/MM/dd - HH:mm
            Assert.Matches(@"\d{4}/\d{2}/\d{2} - \d{2}:\d{2}", wrapper.PersianLastUpdate);
        }

        [Fact]
        public void Constructor_ShouldWorkWithDifferentTypes()
        {
            // Arrange & Act
            var intWrapper = new RedisDataWrapper<int>(42);
            var stringWrapper = new RedisDataWrapper<string>("test");
            var objectWrapper = new RedisDataWrapper<object>(new object());

            // Assert
            Assert.Equal(42, intWrapper.Data);
            Assert.Equal("test", stringWrapper.Data);
            Assert.NotNull(objectWrapper.Data);
        }

        [Fact]
        public void Constructor_ShouldWorkWithComplexTypes()
        {
            // Arrange
            var complexData = new TestClass { Id = 1, Name = "Test" };

            // Act
            var wrapper = new RedisDataWrapper<TestClass>(complexData);

            // Assert
            Assert.Equal(complexData, wrapper.Data);
            Assert.Equal(1, wrapper.Data.Id);
            Assert.Equal("Test", wrapper.Data.Name);
        }

        [Fact]
        public void Data_ShouldBeSettable()
        {
            // Arrange
            var wrapper = new RedisDataWrapper<string>("initial");
            var newData = "updated";

            // Act
            wrapper.Data = newData;

            // Assert
            Assert.Equal(newData, wrapper.Data);
        }

        [Fact]
        public void DateTime_ShouldBeSettable()
        {
            // Arrange
            var wrapper = new RedisDataWrapper<string>("test");
            var newDateTime = DateTime.UtcNow.AddDays(-1);

            // Act
            wrapper.DateTime = newDateTime;

            // Assert
            Assert.Equal(newDateTime, wrapper.DateTime);
        }

        [Fact]
        public void PersianLastUpdate_ShouldBeSettable()
        {
            // Arrange
            var wrapper = new RedisDataWrapper<string>("test");
            var newPersianDate = "1403/01/01 - 12:00";

            // Act
            wrapper.PersianLastUpdate = newPersianDate;

            // Assert
            Assert.Equal(newPersianDate, wrapper.PersianLastUpdate);
        }

        [Fact]
        public void Constructor_ShouldHandleListType()
        {
            // Arrange
            var list = new List<int> { 1, 2, 3 };

            // Act
            var wrapper = new RedisDataWrapper<List<int>>(list);

            // Assert
            Assert.Equal(list, wrapper.Data);
            Assert.Equal(3, wrapper.Data.Count);
        }

        [Fact]
        public void Constructor_ShouldHandleDictionaryType()
        {
            // Arrange
            var dict = new Dictionary<string, int> { { "one", 1 }, { "two", 2 } };

            // Act
            var wrapper = new RedisDataWrapper<Dictionary<string, int>>(dict);

            // Assert
            Assert.Equal(dict, wrapper.Data);
            Assert.Equal(2, wrapper.Data.Count);
        }

        private class TestClass
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}
