using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ARPet.Validation
{
    public sealed class RuleResult
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public string Error { get; set; }
    }

    public static class RuleRunner
    {
        public static RuleResult[] Run(Assembly assembly)
        {
            var results = new List<RuleResult>();
            foreach (var type in assembly.GetTypes().OrderBy(type => type.FullName))
            foreach (var method in type.GetMethods().OrderBy(method => method.Name))
            {
                var cases = method.GetCustomAttributes<TestCaseAttribute>().Select(value => value.Arguments).ToList();
                if (cases.Count == 0 && method.IsDefined(typeof(TestAttribute), false)) cases.Add(Array.Empty<object>());
                foreach (var args in cases)
                {
                    var result = new RuleResult { Name = type.Name + "." + method.Name + "(" + string.Join(",", args.Select(value => value?.ToString())) + ")", Passed = true };
                    object fixture = null;
                    LogAssert.Begin();
                    try
                    {
                        fixture = Activator.CreateInstance(type);
                        foreach (var setup in type.GetMethods().Where(value => value.IsDefined(typeof(SetUpAttribute), false))) setup.Invoke(fixture, null);
                        method.Invoke(fixture, args);
                        LogAssert.NoUnexpectedReceived();
                    }
                    catch (Exception e)
                    {
                        result.Passed = false;
                        result.Error = e.GetBaseException().ToString();
                    }
                    finally
                    {
                        if (fixture != null)
                        {
                            try
                            {
                                foreach (var teardown in type.GetMethods().Where(value => value.IsDefined(typeof(TearDownAttribute), false))) teardown.Invoke(fixture, null);
                            }
                            catch (Exception e) { result.Passed = false; result.Error += "\nTearDown: " + e.GetBaseException(); }
                        }
                    }
                    results.Add(result);
                }
            }
            return results.ToArray();
        }
    }
}
