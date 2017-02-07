﻿namespace Gu.Analyzers
{
    using System.Collections.Immutable;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class GU0033DontIgnoreReturnValueOfTypeIDisposable : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "GU0033";

        private const string Title = "Don't ignore returnvalue of type IDisposable.";

        private const string MessageFormat = "Don't ignore returnvalue of type IDisposable.";

        private const string Description = "Don't ignore returnvalue of type IDisposable.";

        private static readonly string HelpLink = Analyzers.HelpLink.ForId(DiagnosticId);

        private static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
            id: DiagnosticId,
            title: Title,
            messageFormat: MessageFormat,
            category: AnalyzerCategory.Correctness,
            defaultSeverity: DiagnosticSeverity.Hidden,
            isEnabledByDefault: AnalyzerConstants.EnabledByDefault,
            description: Description,
            helpLinkUri: HelpLink);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Descriptor);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(HandleCreation, SyntaxKind.ObjectCreationExpression);
            context.RegisterSyntaxNodeAction(HandleInvocation, SyntaxKind.InvocationExpression);
        }

        private static void HandleCreation(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
            if (!Disposable.IsPotentiallyCreated(objectCreation, context.SemanticModel, context.CancellationToken))
            {
                return;
            }

            if (MustBeHandled(objectCreation, context.SemanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, context.Node.GetLocation()));
            }
        }

        private static void HandleInvocation(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation == null)
            {
                return;
            }

            var symbol = (IMethodSymbol)context.SemanticModel.GetSymbolSafe(invocation, context.CancellationToken);
            if (symbol == null ||
                symbol.ReturnsVoid)
            {
                return;
            }

            if (!Disposable.IsPotentiallyCreated(invocation, context.SemanticModel, context.CancellationToken))
            {
                return;
            }

            if (MustBeHandled(invocation, context.SemanticModel, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, context.Node.GetLocation()));
            }
        }

        private static bool MustBeHandled(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (node.Parent is AnonymousFunctionExpressionSyntax ||
                node.Parent is UsingStatementSyntax)
            {
                return false;
            }

            if (node.Parent is StatementSyntax)
            {
                return !(node.Parent is ReturnStatementSyntax);
            }

            var argument = node.Parent as ArgumentSyntax;
            if (argument != null)
            {
                ISymbol member;
                if (TryGetAssignedFieldOrProperty(argument, semanticModel, cancellationToken, out member) &&
                    member != null)
                {
                    if (Disposable.IsMemberDisposed(member, semanticModel, cancellationToken))
                    {
                        return false;
                    }

                    var initializer = argument.FirstAncestorOrSelf<ConstructorInitializerSyntax>();
                    if (initializer != null)
                    {
                        var ctor = semanticModel.GetDeclaredSymbolSafe(initializer.Parent, cancellationToken) as IMethodSymbol;
                        if (ctor != null &&
                            ctor.ContainingType != member.ContainingType)
                        {
                            IMethodSymbol disposeMethod;
                            if (Disposable.TryGetDisposeMethod(ctor.ContainingType, false, out disposeMethod))
                            {
                                return Disposable.IsMemberDisposed(member, disposeMethod, semanticModel, cancellationToken);
                            }
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        private static bool TryGetConstructor(ArgumentSyntax argument, SemanticModel semanticModel, CancellationToken cancellationToken, out IMethodSymbol ctor)
        {
            var objectCreation = argument.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
            if (objectCreation != null)
            {
                ctor = semanticModel.GetSymbolSafe(objectCreation, cancellationToken) as IMethodSymbol;
                return ctor != null;
            }

            var initializer = argument.FirstAncestorOrSelf<ConstructorInitializerSyntax>();
            if (initializer != null)
            {
                ctor = semanticModel.GetSymbolSafe(initializer, cancellationToken);
                return ctor != null;
            }

            ctor = null;
            return false;
        }

        private static bool TryGetAssignedFieldOrProperty(ArgumentSyntax argument, SemanticModel semanticModel, CancellationToken cancellationToken, out ISymbol member)
        {
            IMethodSymbol ctor;
            if (TryGetConstructor(argument, semanticModel, cancellationToken, out ctor))
            {
                return TryGetAssignedFieldOrProperty(argument, ctor, semanticModel, cancellationToken, out member);
            }

            member = null;
            return false;
        }

        private static bool TryGetAssignedFieldOrProperty(ArgumentSyntax argument, IMethodSymbol method, SemanticModel semanticModel, CancellationToken cancellationToken, out ISymbol member)
        {
            member = null;
            if (method == null)
            {
                return false;
            }

            if (method.ContainingType == KnownSymbol.SerialDisposable ||
                method.ContainingType.Is(KnownSymbol.StreamReader))
            {
                return true;
            }

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var methodDeclaration = reference.GetSyntax(cancellationToken) as BaseMethodDeclarationSyntax;
                if (methodDeclaration == null)
                {
                    continue;
                }

                ParameterSyntax paremeter;
                if (!methodDeclaration.TryGetMatchingParameter(argument, out paremeter))
                {
                    continue;
                }

                var parameterSymbol = semanticModel.GetDeclaredSymbolSafe(paremeter, cancellationToken);
                AssignmentExpressionSyntax assignment;
                if (methodDeclaration.Body.TryGetAssignment(parameterSymbol, semanticModel, cancellationToken, out assignment))
                {
                    member = semanticModel.GetSymbolSafe(assignment.Left, cancellationToken);
                    if (member is IFieldSymbol ||
                        member is IPropertySymbol)
                    {
                        return true;
                    }
                }

                var ctor = reference.GetSyntax(cancellationToken) as ConstructorDeclarationSyntax;
                if (ctor?.Initializer != null)
                {
                    foreach (var arg in ctor.Initializer.ArgumentList.Arguments)
                    {
                        var argSymbol = semanticModel.GetSymbolSafe(arg.Expression, cancellationToken);
                        if (parameterSymbol.Equals(argSymbol))
                        {
                            var chained = semanticModel.GetSymbolSafe(ctor.Initializer, cancellationToken);
                            return TryGetAssignedFieldOrProperty(arg, chained, semanticModel, cancellationToken, out member);
                        }
                    }
                }
            }

            return false;
        }
    }
}