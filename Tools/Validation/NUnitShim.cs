// Small assertion/reflection harness for offline rule checks, not the Unity or NUnit test runner.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseAttribute : Attribute
    {
        public object[] Arguments { get; }
        public TestCaseAttribute(params object[] arguments) => Arguments = arguments;
    }
    public static class Assert
    {
        public static void IsTrue(bool value, string message = null) { if (!value) throw new Exception(message ?? "Expected true"); }
        public static void IsFalse(bool value, string message = null) => IsTrue(!value, message ?? "Expected false");
        public static void IsNotNull(object value, string message = null) => IsTrue(value != null, message ?? "Expected non-null");
        public static void AreEqual(object expected, object actual, string message = null)
        { if (!Equals(expected, actual)) throw new Exception($"{message} Expected {expected}, got {actual}"); }
        public static void AreEqual(double expected, double actual, double tolerance, string message = null)
        { if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance) throw new Exception($"{message} Expected {expected} +/- {tolerance}, got {actual}"); }
        public static void AreNotEqual(object expected, object actual, string message = null) => IsFalse(Equals(expected, actual), message);
        public static void Less(double actual, double expected, string message = null) => IsTrue(actual < expected, message ?? $"Expected {actual} < {expected}");
        public static void Greater(double actual, double expected, string message = null) => IsTrue(actual > expected, message ?? $"Expected {actual} > {expected}");
    }
    public static class CollectionAssert
    {
        public static void Contains(IEnumerable values, object value) => Assert.IsTrue(values.Cast<object>().Contains(value));
        public static void DoesNotContain(IEnumerable values, object value) => Assert.IsFalse(values.Cast<object>().Contains(value));
        public static void AreEqual(IEnumerable expected, IEnumerable actual) => Assert.IsTrue(expected.Cast<object>().SequenceEqual(actual.Cast<object>()));
    }
    public static class StringAssert
    {
        public static void Contains(string expected, string actual) => Assert.IsTrue(actual?.Contains(expected) == true, $"Missing {expected} in {actual}");
    }
}
namespace UnityEngine.TestTools
{
    public static class LogAssert
    {
        private static readonly Queue<(LogType Type, Regex Pattern)> Expected = new();
        private static readonly List<string> Unexpected = new();
        public static void Begin()
        {
            Expected.Clear(); Unexpected.Clear();
            Debug.Logged = (type, message) =>
            {
                if (Expected.Count > 0 && Expected.Peek().Type == type && Expected.Peek().Pattern.IsMatch(message)) Expected.Dequeue();
                else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Unexpected.Add(message);
            };
        }
        public static void Expect(LogType type, Regex pattern) => Expected.Enqueue((type, pattern));
        public static void NoUnexpectedReceived()
        {
            if (Expected.Count > 0 || Unexpected.Count > 0)
                throw new Exception($"Missing logs: {Expected.Count}; unexpected errors: {string.Join("; ", Unexpected)}");
        }
    }
}
