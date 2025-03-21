using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using testProj2;

namespace testProj2.Tests
{
    [TestClass]
    public class CalculatorTests
    {
        private Calculator _calculator;

        [TestInitialize]
        public void Setup()
        {
            // Arrange: Initialize the calculator before each test
            _calculator = new Calculator();
        }

        [TestMethod]
        public void Add_TwoPositiveNumbers_ReturnsCorrectSum()
        {
            // Act
            int result = _calculator.Add(5, 3);

            // Assert
            Assert.AreEqual(8, result);
        }

        [TestMethod]
        public void Add_NegativeAndPositiveNumbers_ReturnsCorrectSum()
        {
            // Act
            int result = _calculator.Add(-5, 8);

            // Assert
            Assert.AreEqual(3, result);
        }

        [TestMethod]
        public void Subtract_TwoPositiveNumbers_ReturnsCorrectDifference()
        {
            // Act
            int result = _calculator.Subtract(10, 4);

            // Assert
            Assert.AreEqual(6, result);
        }

        [TestMethod]
        public void Multiply_TwoPositiveNumbers_ReturnsCorrectProduct()
        {
            // Act
            int result = _calculator.Multiply(5, 3);

            // Assert
            Assert.AreEqual(15, result);
        }

        [TestMethod]
        public void Divide_TwoPositiveNumbers_ReturnsCorrectQuotient()
        {
            // Act
            double result = _calculator.Divide(10, 2);

            // Assert
            Assert.AreEqual(5.0, result);
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void Divide_DenominatorIsZero_ThrowsDivideByZeroException()
        {
            // Act - should throw exception
            _calculator.Divide(10, 0);

            // Assert is handled by ExpectedException attribute
        }
    }
}