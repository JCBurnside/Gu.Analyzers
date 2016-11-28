﻿namespace Gu.Analyzers.Test.GU0020SortPropertiesTests
{
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    internal class CodeFix : CodeFixVerifier<GU0020SortProperties, SortPropertiesCodeFixProvider>
    {
        [Test]
        public async Task WhenMutableBeforeGetOnlyFirst()
        {
            var testCode = @"
    public class Foo
    {
        public Foo(int a, int b, int c, int d)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
        }

        public int A { get; set; }

        public int B { get; }

        public int C { get; }

        public int D { get; }
    }";
            var expected1 = this.CSharpDiagnostic()
                               .WithLocation("Foo.cs", 12, 9)
                               .WithMessage("Sort properties.");
            var expected2 = this.CSharpDiagnostic()
                                .WithLocation("Foo.cs", 14, 9)
                                .WithMessage("Sort properties.");
            var expected3 = this.CSharpDiagnostic()
                                .WithLocation("Foo.cs", 16, 9)
                                .WithMessage("Sort properties.");
            var expected4 = this.CSharpDiagnostic()
                                .WithLocation("Foo.cs", 18, 9)
                                .WithMessage("Sort properties.");
            await this.VerifyCSharpDiagnosticAsync(testCode, new[] { expected1, expected2, expected3, expected4 }, CancellationToken.None).ConfigureAwait(false);

            var fixedCode = @"
    public class Foo
    {
        public Foo(int a, int b, int c, int d)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
        }

        public int B { get; }

        public int C { get; }

        public int D { get; }

        public int A { get; set; }
    }";
            await this.VerifyCSharpFixAsync(testCode, fixedCode).ConfigureAwait(false);
        }

        [Test]
        public async Task WhenMutableBeforeGetOnlyLast()
        {
            var testCode = @"
    public class Foo
    {
        public Foo(int a, int b, int c, int d)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
        }

        public int A { get; }

        public int B { get; }

        public int C { get; set; }

        public int D { get; }
    }";
            var expected1 = this.CSharpDiagnostic()
                               .WithLocation("Foo.cs", 16, 9)
                               .WithMessage("Sort properties.");
            var expected2 = this.CSharpDiagnostic()
                                .WithLocation("Foo.cs", 18, 9)
                                .WithMessage("Sort properties.");
            await this.VerifyCSharpDiagnosticAsync(testCode, new[] { expected1, expected2 }, CancellationToken.None).ConfigureAwait(false);

            var fixedCode = @"
    public class Foo
    {
        public Foo(int a, int b, int c, int d)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
        }

        public int A { get; }

        public int B { get; }

        public int D { get; }

        public int C { get; set; }
    }";
            await this.VerifyCSharpFixAsync(testCode, fixedCode).ConfigureAwait(false);
        }

        [Test]
        public async Task WhenMutableBeforeGetOnlyWithComments()
        {
            var testCode = @"
    public class Foo
    {
        public Foo(int a, int b, int c, int d)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
        }

        public int A { get; }

        public int B { get; }

        /// <summary>
        /// C
        /// </summary>
        public int C { get; set; }

        /// <summary>
        /// D
        /// </summary>
        public int D { get; }
    }";
            var expected1 = this.CSharpDiagnostic()
                               .WithLocation("Foo.cs", 19, 9)
                               .WithMessage("Sort properties.");
            var expected2 = this.CSharpDiagnostic()
                                .WithLocation("Foo.cs", 24, 9)
                                .WithMessage("Sort properties.");
            await this.VerifyCSharpDiagnosticAsync(testCode, new[] { expected1, expected2 }, CancellationToken.None).ConfigureAwait(false);

            var fixedCode = @"
    public class Foo
    {
        public Foo(int a, int b, int c, int d)
        {
            this.A = a;
            this.B = b;
            this.C = c;
            this.D = d;
        }

        public int A { get; }

        public int B { get; }

        /// <summary>
        /// D
        /// </summary>
        public int D { get; }

        /// <summary>
        /// C
        /// </summary>
        public int C { get; set; }
    }";
            await this.VerifyCSharpFixAsync(testCode, fixedCode).ConfigureAwait(false);
        }

        [Test]
        public async Task ExpressionBodyBeforeGetOnly()
        {
            var testCode = @"
    public class Foo
    {
        public Foo(int b)
        {
            this.B = b;
        }

        public int A => B;

        public int B { get; }
    }";
            var expected1 = this.CSharpDiagnostic()
                               .WithLocation("Foo.cs", 9, 9)
                               .WithMessage("Sort properties.");
            var expected2 = this.CSharpDiagnostic()
                                .WithLocation("Foo.cs", 11, 9)
                                .WithMessage("Sort properties.");
            await this.VerifyCSharpDiagnosticAsync(testCode, new[] { expected1, expected2 }, CancellationToken.None).ConfigureAwait(false);

            var fixedCode = @"
    public class Foo
    {
        public Foo(int b)
        {
            this.B = b;
        }

        public int B { get; }

        public int A => B;
    }";
            await this.VerifyCSharpFixAsync(testCode, fixedCode).ConfigureAwait(false);
        }

        [Test]
        public async Task PublicSetBeforePrivateSetFirst()
        {
            var testCode = @"
    public class Foo
    {
        public Foo(int a, int b)
        {
            this.A = a;
            this.B = b;
        }

        public int A { get; set; }

        public int B { get; private set; }
    }";
            var expected1 = this.CSharpDiagnostic()
                                .WithLocation("Foo.cs", 10, 9)
                                .WithMessage("Sort properties.");
            var expected2 = this.CSharpDiagnostic()
                                .WithLocation("Foo.cs", 12, 9)
                                .WithMessage("Sort properties.");

            await this.VerifyCSharpDiagnosticAsync(testCode, new[] { expected1, expected2 }, CancellationToken.None).ConfigureAwait(false);

            var fixedCode = @"
    public class Foo
    {
        public Foo(int a, int b)
        {
            this.A = a;
            this.B = b;
        }

        public int B { get; private set; }

        public int A { get; set; }
    }";
            await this.VerifyCSharpFixAsync(testCode, fixedCode).ConfigureAwait(false);
        }
    }
}