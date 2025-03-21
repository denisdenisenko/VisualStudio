using System;
using TestProjWithUnitTests.Models;
using Xunit;

namespace TestProjWithUnitTests.Tests
{
    public class CalculatorTests
    {
        private readonly Calculator _calculator;

        public CalculatorTests()
        {
            _calculator = new Calculator();
        }

        [Fact]
        public void Add_ShouldReturnSum()
        {
            // Arrange
            int a = 5;
            int b = 3;

            // Act
            int result = _calculator.Add(a, b);

            // Assert
            Assert.Equal(8, result);
        }

        [Theory]
        [InlineData(10, 5, 5)]
        [InlineData(20, 5, 15)]
        [InlineData(0, 5, -5)]
        public void Subtract_ShouldReturnDifference(int a, int b, int expected)
        {
            // Act
            int result = _calculator.Subtract(a, b);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(3, 4, 12)]
        [InlineData(0, 5, 0)]
        [InlineData(-2, 3, -6)]
        public void Multiply_ShouldReturnProduct(int a, int b, int expected)
        {
            // Act
            int result = _calculator.Multiply(a, b);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(10, 2, 5.0)]
        [InlineData(7, 2, 3.5)]
        [InlineData(0, 5, 0.0)]
        public void Divide_ShouldReturnQuotient(int a, int b, double expected)
        {
            // Act
            double result = _calculator.Divide(a, b);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Divide_ByZero_ShouldThrowException()
        {
            // Arrange
            int a = 10;
            int b = 0;

            // Act & Assert
            Assert.Throws<DivideByZeroException>(() => _calculator.Divide(a, b));
        }

        [Theory]
        [InlineData(2, true)]
        [InlineData(4, true)]
        [InlineData(5, false)]
        [InlineData(0, true)]
        public void IsEven_ShouldReturnCorrectResult(int number, bool expected)
        {
            // Act
            bool result = _calculator.IsEven(number);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(2, true)]
        [InlineData(3, true)]
        [InlineData(4, false)]
        [InlineData(17, true)]
        [InlineData(20, false)]
        [InlineData(1, false)]
        [InlineData(0, false)]
        public void IsPrime_ShouldReturnCorrectResult(int number, bool expected)
        {
            // Act
            bool result = _calculator.IsPrime(number);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}