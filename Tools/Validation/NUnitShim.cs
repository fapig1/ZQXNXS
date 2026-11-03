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
    public abstract class Constraint
    {
        public abstract bool Matches(object actual);
        public abstract string Description { get; }
        public virtual Constraint Within(double tolerance) => this;
    }
    public sealed class PredicateConstraint : Constraint
    {
        private readonly Func<object, bool> _matches;
        private readonly string _description;
        public PredicateConstraint(Func<object, bool> matches, string description)
        { _matches = matches; _description = description; }
        public override bool Matches(object actual) => _matches(actual);
        public override string Description => _description;
    }
    public sealed class NotConstraint : Constraint
    {
        private readonly Constraint _inner;
        public NotConstraint(Constraint inner) => _inner = inner;
        public override bool Matches(object actual) => !_inner.Matches(actual);
        public override string Description => "not " + _inner.Description;
    }
    public sealed class AllConstraint : Constraint
    {
        private readonly Constraint _inner;
        public AllConstraint(Constraint inner) => _inner = inner;
        public override bool Matches(object actual)
            => actual is IEnumerable items && items.Cast<object>().All(_inner.Matches);
        public override string Description => "all " + _inner.Description;
    }
    public sealed class EqualConstraint : Constraint
    {
        private readonly object _expected;
        private double _tolerance = double.NaN;
        public EqualConstraint(object expected) => _expected = expected;
        public override Constraint Within(double tolerance) { _tolerance = tolerance; return this; }
        public override bool Matches(object actual)
        {
            if (!double.IsNaN(_tolerance))
                return TryNumber(actual, out var left) && TryNumber(_expected, out var right) && Math.Abs(left - right) <= _tolerance;
            if (_expected is not string && _expected is IEnumerable expectedItems && actual is IEnumerable actualItems)
            {
                var left = expectedItems.Cast<object>().ToArray();
                var right = actualItems.Cast<object>().ToArray();
                return left.Length == right.Length && left.Zip(right, Equals).All(value => value);
            }
            return Equals(_expected, actual);
        }
        public override string Description => "equal to " + _expected;
        private static bool TryNumber(object value, out double number)
        {
            switch (value)
            {
                case float single: number = single; return true;
                case double @double: number = @double; return true;
                case int integer: number = integer; return true;
                case long l: number = l; return true;
                case decimal m: number = (double)m; return true;
                default: number = 0; return false;
            }
        }
    }
    public sealed class ComparisonConstraint : Constraint
    {
        private readonly IComparable _expected;
        private readonly Func<int, bool> _accepts;
        private readonly string _description;
        public ComparisonConstraint(IComparable expected, Func<int, bool> accepts, string description)
        { _expected = expected; _accepts = accepts; _description = description; }
        public override bool Matches(object actual) => actual is IComparable comparable && _accepts(comparable.CompareTo(_expected));
        public override string Description => _description + " " + _expected;
    }
    public sealed class StringConstraint : Constraint
    {
        private readonly Func<string, bool> _matches;
        private readonly string _description;
        public StringConstraint(Func<string, bool> matches, string description)
        { _matches = matches; _description = description; }
        public override bool Matches(object actual) => actual is string text && _matches(text);
        public override string Description => _description;
    }
    public sealed class NotOperator
    {
        private readonly bool _all;
        public NotOperator(bool all = false) => _all = all;
        public Constraint Null => Wrap(new NotConstraint(Is.Null));
        public Constraint Empty => Wrap(new NotConstraint(Is.Empty));
        private Constraint Wrap(Constraint value) => _all ? new AllConstraint(value) : value;
    }
    public sealed class AllOperator
    {
        public NotOperator Not => new(true);
        public Constraint Null => new AllConstraint(Is.Null);
        public Constraint Empty => new AllConstraint(Is.Empty);
    }
    public static class Is
    {
        public static Constraint True => new PredicateConstraint(value => value is bool flag && flag, "true");
        public static Constraint False => new PredicateConstraint(value => value is bool flag && !flag, "false");
        public static Constraint Null => new PredicateConstraint(value => value == null, "null");
        public static Constraint Empty => new PredicateConstraint(value => value switch
        {
            null => true,
            string text => text.Length == 0,
            IEnumerable items => !items.Cast<object>().Any(),
            _ => false,
        }, "empty");
        public static NotOperator Not => new();
        public static EqualConstraint EqualTo(object expected) => new(expected);
        public static Constraint GreaterThan(object expected) => new ComparisonConstraint((IComparable)expected, value => value > 0, "greater than");
        public static Constraint GreaterThanOrEqualTo(object expected) => new ComparisonConstraint((IComparable)expected, value => value >= 0, "greater than or equal to");
        public static Constraint LessThan(object expected) => new ComparisonConstraint((IComparable)expected, value => value < 0, "less than");
        public static Constraint LessThanOrEqualTo(object expected) => new ComparisonConstraint((IComparable)expected, value => value <= 0, "less than or equal to");
    }
    public static class Has
    {
        public static AllOperator All => new();
    }
    public static class Does
    {
        public static Constraint StartWith(string expected) => new StringConstraint(value => value.StartsWith(expected, StringComparison.Ordinal), "start with " + expected);
        public static Constraint EndWith(string expected) => new StringConstraint(value => value.EndsWith(expected, StringComparison.Ordinal), "end with " + expected);
        public static Constraint Contain(string expected) => new StringConstraint(value => value.Contains(expected), "contain " + expected);
        public static Constraint Match(string pattern) => new StringConstraint(value => Regex.IsMatch(value, pattern), "match " + pattern);
    }
    public static class Assert
    {
        public static void That(object actual, Constraint constraint, string message = null)
        {
            if (!constraint.Matches(actual)) throw new Exception($"{message} Expected {constraint.Description}, got {actual}");
        }
        public static void IsTrue(bool value, string message = null) { if (!value) throw new Exception(message ?? "Expected true"); }
        public static void IsFalse(bool value, string message = null) => IsTrue(!value, message ?? "Expected false");
        public static void IsNull(object value, string message = null) => IsTrue(value == null, message ?? "Expected null");
        public static void IsNotNull(object value, string message = null) => IsTrue(value != null, message ?? "Expected non-null");
        public static void AreEqual(object expected, object actual, string message = null)
        { if (!Equals(expected, actual)) throw new Exception($"{message} Expected {expected}, got {actual}"); }
        public static void AreEqual(double expected, double actual, double tolerance, string message = null)
        { if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance) throw new Exception($"{message} Expected {expected} +/- {tolerance}, got {actual}"); }
        public static void AreNotEqual(object expected, object actual, string message = null) => IsFalse(Equals(expected, actual), message);
        public static void AreSame(object expected, object actual, string message = null) => IsTrue(ReferenceEquals(expected, actual), message ?? "Expected same reference");
        public static void Less(double actual, double expected, string message = null) => IsTrue(actual < expected, message ?? $"Expected {actual} < {expected}");
        public static void Greater(double actual, double expected, string message = null) => IsTrue(actual > expected, message ?? $"Expected {actual} > {expected}");
        public static void GreaterOrEqual(double actual, double expected, string message = null) => IsTrue(actual >= expected, message ?? $"Expected {actual} >= {expected}");
        public static void Fail(string message = null) => throw new Exception(message ?? "Assert.Fail");
        public static void DoesNotThrow(Action action, string message = null)
        { try { action(); } catch (Exception exception) { throw new Exception($"{message} Unexpected exception: {exception}"); } }
    }
    public static class Assume
    {
        public static void That(object actual, Constraint constraint, string message = null)
        {
            if (!constraint.Matches(actual)) throw new Exception($"{message} Assumption failed: expected {constraint.Description}, got {actual}");
        }
    }
    public static class CollectionAssert
    {
        public static void IsEmpty(IEnumerable values, string message = null)
        {
            if (values.Cast<object>().Any()) throw new Exception(message ?? "Expected an empty collection");
        }
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
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnityTestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnitySetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnityTearDownAttribute : Attribute { }
    // PlayMode transitions are recorded only; offline checks never enter play mode.
    public sealed class EnterPlayMode { }
    public sealed class ExitPlayMode { }
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
