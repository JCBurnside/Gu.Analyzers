namespace Gu.Analyzers.Test.GU0080TestAttributeCountMismatchTests
{
    using Gu.Roslyn.Asserts;
    using NUnit.Framework;

    internal class HappyPath
    {
        private static readonly TestMethodAnalyzer Analyzer = new TestMethodAnalyzer();

        [TestCase("[Test]")]
        [TestCase("[Test(Author = \"Author\")]")]
        [TestCase("[TestAttribute]")]
        [TestCase("[TestAttribute()]")]
        public void TestAttribute(string attribute)
        {
            var testCode = @"
namespace RoslynSandbox
{
    using NUnit.Framework;

    public class FooTests
    {
        [Test]
        public void Test()
        {
        }
    }
}";
            testCode = testCode.AssertReplace("[Test]", attribute);
            AnalyzerAssert.Valid(Analyzer, testCode);
        }

        [TestCase("[TestCase(1)]")]
        [TestCase("[TestCase(1, Author = \"Author\")]")]
        public void TestCaseAttribute(string attribute)
        {
            var testCode = @"
namespace RoslynSandbox
{
    using NUnit.Framework;

    public class FooTests
    {
        [TestCase(1)]
        public void Test(int i)
        {
        }
    }
}";
            testCode = testCode.AssertReplace("[TestCase(1)]", attribute);
            AnalyzerAssert.Valid(Analyzer, testCode);
        }

        [Test]
        public void TestAndTestCaseAttribute()
        {
            var testCode = @"
namespace RoslynSandbox
{
    using NUnit.Framework;

    public class FooTests
    {
        [Test]
        [TestCase(1)]
        public void Test(int i)
        {
        }
    }
}";
            AnalyzerAssert.Valid(Analyzer, testCode);
        }

        [Test]
        public void TestCaseSourceAttribute()
        {
            var testCode = @"
namespace RoslynSandbox
{
    using NUnit.Framework;

    public class FooTests
    {
        private static readonly int[] TestCases = { 1, 2, 3 };

        [TestCaseSource(nameof(TestCases))]
        public void Test(int value)
        {
        }
    }
}";
            AnalyzerAssert.Valid(Analyzer, testCode);
        }
    }
}