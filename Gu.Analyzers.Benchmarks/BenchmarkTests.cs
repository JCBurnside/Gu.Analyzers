﻿// ReSharper disable RedundantNameQualifier
namespace Gu.Analyzers.Benchmarks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Gu.Analyzers.Benchmarks.Benchmarks;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    internal class BenchmarkTests
    {
        private static IReadOnlyList<DiagnosticAnalyzer> AllAnalyzers { get; } = typeof(KnownSymbol).Assembly
                                                                                                    .GetTypes()
                                                                                                    .Where(typeof(DiagnosticAnalyzer).IsAssignableFrom)
                                                                                                    .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t))
                                                                                                    .ToArray();

        private static IReadOnlyList<Gu.Roslyn.Asserts.Benchmark> AllBenchmarks { get; } = AllAnalyzers
            .Select(x => Gu.Roslyn.Asserts.Benchmark.Create(Code.AnalyzersProject, x))
            .ToArray();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            foreach (var benchmark in AllBenchmarks)
            {
                benchmark.Run();
            }
        }

        [TestCaseSource(nameof(AllBenchmarks))]
        public void Run(Gu.Roslyn.Asserts.Benchmark benchmark)
        {
            benchmark.Run();
        }
    }
}