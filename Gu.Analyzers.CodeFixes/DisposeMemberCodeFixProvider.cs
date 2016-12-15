﻿namespace Gu.Analyzers
{
    using System;
    using System.Collections.Immutable;
    using System.Composition;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DisposeMemberCodeFixProvider))]
    [Shared]
    internal class DisposeMemberCodeFixProvider : CodeFixProvider
    {
        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(GU0031DisposeMember.DiagnosticId);

        /// <inheritdoc/>
        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        /// <inheritdoc/>
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var syntaxRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                                          .ConfigureAwait(false);

            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
                                             .ConfigureAwait(false);

            foreach (var diagnostic in context.Diagnostics)
            {
                var token = syntaxRoot.FindToken(diagnostic.Location.SourceSpan.Start);
                if (string.IsNullOrEmpty(token.ValueText) ||
                    token.IsMissing)
                {
                    continue;
                }

                var member = (MemberDeclarationSyntax)syntaxRoot.FindNode(diagnostic.Location.SourceSpan);
                ISymbol memberSymbol;
                if (!TryGetMemberSymbol(member, semanticModel, context.CancellationToken, out memberSymbol))
                {
                    continue;
                }

                IMethodSymbol disposeMethodSymbol;
                MethodDeclarationSyntax disposeMethodDeclaration;
                if (Disposable.TryGetDisposeMethod(memberSymbol.ContainingType, out disposeMethodSymbol))
                {
                    if (disposeMethodSymbol.DeclaredAccessibility == Accessibility.Public &&
                        disposeMethodSymbol.Parameters.Length == 0 &&
                        disposeMethodSymbol.TryGetSingleDeclaration(context.CancellationToken, out disposeMethodDeclaration))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Dispose member.",
                                _ => ApplyDisposeMemberPublicFixAsync(context, syntaxRoot, disposeMethodDeclaration, memberSymbol),
                                nameof(DisposeMemberCodeFixProvider)),
                            diagnostic);
                        continue;
                    }

                    if (disposeMethodSymbol.Parameters.Length == 1 &&
                        disposeMethodSymbol.TryGetSingleDeclaration(context.CancellationToken, out disposeMethodDeclaration))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Dispose member.",
                                _ => ApplyDisposeMemberProtectedFixAsync(context, syntaxRoot, disposeMethodDeclaration, memberSymbol),
                                nameof(DisposeMemberCodeFixProvider)),
                            diagnostic);
                    }
                }
            }
        }

        private static Task<Document> ApplyDisposeMemberPublicFixAsync(
            CodeFixContext context,
            SyntaxNode syntaxRoot,
            MethodDeclarationSyntax disposeMethod,
            ISymbol member)
        {
            var newDisposeStatement = CreateDisposeStatement(member);
            var statements = CreateStatements(disposeMethod, newDisposeStatement);
            if (disposeMethod.Body != null)
            {
                var updatedBody = disposeMethod.Body.WithStatements(statements);
                return Task.FromResult(context.Document.WithSyntaxRoot(syntaxRoot.ReplaceNode(disposeMethod.Body, updatedBody)));
            }

            if (disposeMethod.ExpressionBody != null)
            {
                var newMethod = disposeMethod.WithBody(SyntaxFactory.Block(statements))
                                             .WithExpressionBody(null)
                                             .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
                return Task.FromResult(context.Document.WithSyntaxRoot(syntaxRoot.ReplaceNode(disposeMethod, newMethod)));
            }

            return Task.FromResult(context.Document);
        }

        private static Task<Document> ApplyDisposeMemberProtectedFixAsync(
            CodeFixContext context,
            SyntaxNode syntaxRoot,
            MethodDeclarationSyntax disposeMethod,
            ISymbol member)
        {
            var newDisposeStatement = CreateDisposeStatement(member);
            if (disposeMethod.Body != null)
            {
                foreach (var statement in disposeMethod.Body.Statements)
                {
                    var ifStatement = statement as IfStatementSyntax;
                    if (ifStatement == null)
                    {
                        continue;
                    }

                    if ((ifStatement.Condition as IdentifierNameSyntax)?.Identifier.ValueText == "disposing")
                    {
                        var block = ifStatement.Statement as BlockSyntax;
                        if (block != null)
                        {
                            var statements = block.Statements.Add(newDisposeStatement);
                            var newBlock = block.WithStatements(statements);
                            return Task.FromResult(context.Document.WithSyntaxRoot(syntaxRoot.ReplaceNode(block, newBlock)));
                        }
                    }
                }
            }

            return Task.FromResult(context.Document);
        }

        private static StatementSyntax CreateDisposeStatement(ISymbol member)
        {
            var prefix = member.Name[0] == '_' ? string.Empty : "this.";
            if (!Disposable.IsAssignableTo(MemberType(member)))
            {
                return SyntaxFactory.ParseStatement($"({prefix}{member.Name} as IDisposable)?.Dispose();")
                             .WithLeadingTrivia(SyntaxFactory.ElasticMarker)
                             .WithTrailingTrivia(SyntaxFactory.ElasticMarker);
            }

            if (IsReadOnly(member))
            {
                return SyntaxFactory.ParseStatement($"{prefix}{member.Name}.Dispose();")
                                              .WithLeadingTrivia(SyntaxFactory.ElasticMarker)
                                              .WithTrailingTrivia(SyntaxFactory.ElasticMarker);
            }

            return SyntaxFactory.ParseStatement($"{prefix}{member.Name}?.Dispose();")
                                .WithLeadingTrivia(SyntaxFactory.ElasticMarker)
                                .WithTrailingTrivia(SyntaxFactory.ElasticMarker);
        }

        private static SyntaxList<StatementSyntax> CreateStatements(MethodDeclarationSyntax method, StatementSyntax newStatement)
        {
            if (method.ExpressionBody != null)
            {
                return SyntaxFactory.List(new[] { SyntaxFactory.ExpressionStatement(method.ExpressionBody.Expression), newStatement });
            }

            return method.Body.Statements.Add(newStatement);
        }

        private static bool IsReadOnly(ISymbol member)
        {
            var isReadOnly = (member as IFieldSymbol)?.IsReadOnly ?? (member as IPropertySymbol)?.IsReadOnly;
            if (isReadOnly == null)
            {
                throw new InvalidOperationException($"Could not figure out if member: {member} is readonly.");
            }

            return isReadOnly.Value;
        }

        private static ITypeSymbol MemberType(ISymbol member) => (member as IFieldSymbol)?.Type ?? (member as IPropertySymbol)?.Type;

        private static bool TryGetMemberSymbol(MemberDeclarationSyntax member, SemanticModel semanticModel, CancellationToken cancellationToken, out ISymbol symbol)
        {
            var field = member as FieldDeclarationSyntax;
            VariableDeclaratorSyntax fieldDeclarator;
            if (field != null && field.Declaration.Variables.TryGetSingle(out fieldDeclarator))
            {
                symbol = semanticModel.GetDeclaredSymbolSafe(fieldDeclarator, cancellationToken);
                return symbol != null;
            }

            var property = member as PropertyDeclarationSyntax;
            if (property != null)
            {
                symbol = semanticModel.GetDeclaredSymbolSafe(property, cancellationToken);
                return symbol != null;
            }

            symbol = null;
            return false;
        }
    }
}