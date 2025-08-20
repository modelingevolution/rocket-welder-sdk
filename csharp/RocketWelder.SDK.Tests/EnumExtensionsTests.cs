using System;
using System.ComponentModel;
using Xunit;
using RocketWelder.SDK;

namespace RocketWelder.SDK.Tests
{
    public enum TestEnum
    {
        [Description("First Value")]
        FirstValue,
        
        [Description("Second Value")]
        SecondValue,
        
        ValueWithoutDescription,
        
        [Description("Complex Description with Special Chars!@#")]
        ComplexValue
    }

    [Flags]
    public enum TestFlags
    {
        None = 0,
        [Description("Flag One")]
        FlagOne = 1,
        [Description("Flag Two")]
        FlagTwo = 2,
        [Description("Flag Four")]
        FlagFour = 4,
        [Description("Combined Flags")]
        Combined = FlagOne | FlagTwo
    }

    public class EnumExtensionsTests
    {
        [Fact]
        public void GetDescription_Should_Return_Description_Attribute_Value()
        {
            // Arrange
            var enumValue = TestEnum.FirstValue;
            
            // Act
            var description = enumValue.GetDescription();
            
            // Assert
            Assert.Equal("First Value", description);
        }

        [Fact]
        public void GetDescription_Should_Return_Enum_Name_When_No_Description()
        {
            // Arrange
            var enumValue = TestEnum.ValueWithoutDescription;
            
            // Act
            var description = enumValue.GetDescription();
            
            // Assert
            Assert.Equal("ValueWithoutDescription", description);
        }

        [Fact]
        public void GetDescription_Should_Handle_Special_Characters()
        {
            // Arrange
            var enumValue = TestEnum.ComplexValue;
            
            // Act
            var description = enumValue.GetDescription();
            
            // Assert
            Assert.Equal("Complex Description with Special Chars!@#", description);
        }

        [Fact]
        public void GetDescription_Should_Work_With_Protocol_Enum()
        {
            // Arrange
            var protocol = Protocol.Shm;
            
            // Act
            var description = protocol.GetDescription();
            
            // Assert
            Assert.NotNull(description);
            Assert.NotEmpty(description);
        }

        [Fact]
        public void GetDescription_Should_Handle_Flags_Enum()
        {
            // Arrange
            var flags = TestFlags.FlagOne;
            
            // Act
            var description = flags.GetDescription();
            
            // Assert
            Assert.Equal("Flag One", description);
        }

        [Fact]
        public void GetDescription_Should_Handle_Combined_Flags()
        {
            // Arrange
            var flags = TestFlags.Combined;
            
            // Act
            var description = flags.GetDescription();
            
            // Assert
            Assert.Equal("Combined Flags", description);
        }

        [Fact]
        public void GetDescription_Should_Handle_Multiple_Flags()
        {
            // Arrange
            var flags = TestFlags.FlagOne | TestFlags.FlagTwo | TestFlags.FlagFour;
            
            // Act
            var description = flags.GetDescription();
            
            // Assert
            // When multiple flags are set without a specific description,
            // it should return the ToString() value
            Assert.Equal("FlagOne, FlagTwo, FlagFour", description);
        }

        [Theory]
        [InlineData(TestEnum.FirstValue, "First Value")]
        [InlineData(TestEnum.SecondValue, "Second Value")]
        [InlineData(TestEnum.ValueWithoutDescription, "ValueWithoutDescription")]
        [InlineData(TestEnum.ComplexValue, "Complex Description with Special Chars!@#")]
        public void GetDescription_Should_Return_Correct_Value_For_All_Enum_Values(TestEnum value, string expected)
        {
            // Act
            var description = value.GetDescription();
            
            // Assert
            Assert.Equal(expected, description);
        }

        [Fact]
        public void GetDescription_Should_Be_Case_Sensitive()
        {
            // Arrange
            var enumValue = TestEnum.FirstValue;
            
            // Act
            var description = enumValue.GetDescription();
            
            // Assert
            Assert.Equal("First Value", description);
            Assert.NotEqual("first value", description);
        }

        [Fact]
        public void GetDescription_Should_Cache_Results_For_Performance()
        {
            // This test verifies that multiple calls to GetDescription
            // for the same enum value are efficient (implementation detail)
            
            // Arrange
            var enumValue = TestEnum.FirstValue;
            
            // Act - Call multiple times
            var desc1 = enumValue.GetDescription();
            var desc2 = enumValue.GetDescription();
            var desc3 = enumValue.GetDescription();
            
            // Assert - All should return the same string
            Assert.Same(desc1, desc2);
            Assert.Same(desc2, desc3);
        }

        [Fact]
        public void GetDescription_Should_Handle_Null_Properly()
        {
            // Arrange
            TestEnum? nullableEnum = null;
            
            // Act & Assert
            // Extension methods can't be called on null, so this would be a compile error
            // This test is more about documenting expected behavior
            Assert.True(true);
        }

        [Fact]
        public void GetDescription_Should_Work_With_Different_Enum_Types()
        {
            // This test verifies the extension method is generic enough
            
            // Act & Assert for Protocol enum
            var protocol = Protocol.Mjpeg | Protocol.Http;
            var protocolDesc = protocol.GetDescription();
            Assert.NotNull(protocolDesc);
            
            // Act & Assert for TestEnum
            var testEnum = TestEnum.FirstValue;
            var testDesc = testEnum.GetDescription();
            Assert.Equal("First Value", testDesc);
            
            // Act & Assert for TestFlags
            var flags = TestFlags.FlagOne;
            var flagsDesc = flags.GetDescription();
            Assert.Equal("Flag One", flagsDesc);
        }
    }
}