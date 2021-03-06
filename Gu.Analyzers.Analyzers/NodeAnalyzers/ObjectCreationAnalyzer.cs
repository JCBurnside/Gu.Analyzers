﻿namespace Gu.Analyzers
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class ObjectCreationAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
            GU0005ExceptionArgumentsPositions.Descriptor,
            GU0013CheckNameInThrow.Descriptor);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(HandleObjectCreation, SyntaxKind.ObjectCreationExpression);
        }

        private static void HandleObjectCreation(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            if (context.Node is ObjectCreationExpressionSyntax objectCreation &&
                objectCreation.ArgumentList is ArgumentListSyntax argumentList &&
                argumentList.Arguments.Count > 0 &&
                context.ContainingSymbol is IMethodSymbol method)
            {
                if (objectCreation.TryGetConstructor(KnownSymbol.ArgumentException, context.SemanticModel, context.CancellationToken, out var ctor) ||
                    objectCreation.TryGetConstructor(KnownSymbol.ArgumentNullException, context.SemanticModel, context.CancellationToken, out ctor) ||
                    objectCreation.TryGetConstructor(KnownSymbol.ArgumentOutOfRangeException, context.SemanticModel, context.CancellationToken, out ctor))
                {
                    if (ctor.Parameters.TryFirst(x => x.Name == "paramName", out var parameterName))
                    {
                        if (TryGetWithParameterName(argumentList, method.Parameters, out var argument) &&
                            argument.NameColon == null &&
                            objectCreation.ArgumentList.Arguments.IndexOf(argument) != ctor.Parameters.IndexOf(parameterName))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(GU0005ExceptionArgumentsPositions.Descriptor, argument.GetLocation()));
                        }
                        else if (objectCreation.TryGetMatchingArgument(parameterName, out argument) &&
                                 objectCreation.Parent is ThrowExpressionSyntax throwExpression &&
                                 throwExpression.Parent is BinaryExpressionSyntax binary &&
                                 binary.IsKind(SyntaxKind.CoalesceExpression) &&
                                 argument.TryGetNameOf(out var name) &&
                                 binary.Left is IdentifierNameSyntax identifierName &&
                                 identifierName.Identifier.ValueText != name)
                        {
                            var properties = ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, string>("Name", identifierName.Identifier.ValueText) });
                            context.ReportDiagnostic(Diagnostic.Create(GU0013CheckNameInThrow.Descriptor, argument.GetLocation(), properties));
                        }
                    }
                }
            }
        }

        private static bool TryGetWithParameterName(ArgumentListSyntax argumentList, ImmutableArray<IParameterSymbol> parameters, out ArgumentSyntax argument)
        {
            argument = null;
            foreach (var arg in argumentList.Arguments)
            {
                if (arg.Expression is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.StringLiteralExpression) &&
                    parameters.TryFirst(x => x.Name == literal.Token.ValueText, out _))
                {
                    if (argument != null)
                    {
                        return false;
                    }

                    argument = arg;
                }

                if (arg.TryGetNameOf(out var name) &&
                    parameters.TryFirst(x => x.Name == name, out _))
                {
                    if (argument != null)
                    {
                        return false;
                    }

                    argument = arg;
                }
            }

            return argument != null;
        }
    }
}
