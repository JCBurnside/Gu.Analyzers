// ReSharper disable RedundantNameQualifier
// ReSharper disable InconsistentNaming
namespace Gu.Analyzers.Benchmarks.Benchmarks
{
    internal class GU0006UseNameofBenchmarks
    {
        private static readonly Gu.Roslyn.Asserts.Benchmark Benchmark = Gu.Roslyn.Asserts.Benchmark.Create(Code.AnalyzersProject, new Gu.Analyzers.GU0006UseNameof());

        [BenchmarkDotNet.Attributes.Benchmark]
        public void RunOnGuAnalyzersProject()
        {
            Benchmark.Run();
        }
    }
}
