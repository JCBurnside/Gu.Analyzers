﻿namespace Gu.Analyzers
{
    using System.Collections.Generic;
    using System.Threading;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal sealed class AssignedValueWalker : CSharpSyntaxWalker
    {
        private static readonly Pool<AssignedValueWalker> Pool = new Pool<AssignedValueWalker>(
            () => new AssignedValueWalker(),
            x =>
            {
                x.assignedValues.Clear();
                x.checkedSymbols.Clear();
                x.visitedMembers.Clear();
                x.currentSymbol = null;
                x.context = null;
                x.semanticModel = null;
                x.cancellationToken = CancellationToken.None;
            });

        private readonly List<Assignment> assignedValues = new List<Assignment>();
        private readonly HashSet<ISymbol> checkedSymbols = new HashSet<ISymbol>();
        private readonly HashSet<SyntaxNode> visitedMembers = new HashSet<SyntaxNode>();

        private ISymbol currentSymbol;
        private SyntaxNode context;
        private SemanticModel semanticModel;
        private CancellationToken cancellationToken;
        private bool isSamplingRetunValues;

        private AssignedValueWalker()
        {
        }

        public IReadOnlyList<Assignment> AssignedValues => this.assignedValues;

        public override void Visit(SyntaxNode node)
        {
            if (this.visitedMembers.AddIfNotNull(node as MemberDeclarationSyntax) == false ||
                this.visitedMembers.AddIfNotNull(node as AccessorDeclarationSyntax) == false ||
                !this.IsBeforeInScope(node))
            {
                return;
            }

            base.Visit(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (node.Initializer != null &&
                SymbolComparer.Equals(this.currentSymbol, this.semanticModel.GetDeclaredSymbolSafe(node, this.cancellationToken)))
            {
                this.assignedValues.Add(new Assignment(this.currentSymbol, node.Initializer.Value));
            }

            base.VisitVariableDeclarator(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.Initializer != null &&
                SymbolComparer.Equals(this.currentSymbol, this.semanticModel.GetDeclaredSymbolSafe(node, this.cancellationToken)))
            {
                this.assignedValues.Add(new Assignment(this.currentSymbol, node.Initializer.Value));
            }

            base.VisitPropertyDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (node.Initializer != null)
            {
                var ctor = this.semanticModel.GetSymbolSafe(node.Initializer, this.cancellationToken);
                if (ctor != null)
                {
                    foreach (var reference in ctor.DeclaringSyntaxReferences)
                    {
                        this.Visit(reference.GetSyntax(this.cancellationToken));
                    }
                }
            }
            else
            {
                var ctor = this.semanticModel.GetDeclaredSymbolSafe(node, this.cancellationToken);
                IMethodSymbol baseCtor;
                if (Constructor.TryGetDefault(ctor?.ContainingType.BaseType, out baseCtor))
                {
                    foreach (var reference in baseCtor.DeclaringSyntaxReferences)
                    {
                        this.Visit(reference.GetSyntax(this.cancellationToken));
                    }
                }
            }

            base.VisitConstructorDeclaration(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            var left = this.semanticModel.GetSymbolSafe(node.Left, this.cancellationToken);
            if (SymbolComparer.Equals(this.currentSymbol, left))
            {
                this.assignedValues.Add(new Assignment(this.currentSymbol, node.Right));
            }
            else if (left is IPropertySymbol)
            {
                this.VisitSetter(left);
            }

            base.VisitAssignmentExpression(node);
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            var operand = this.semanticModel.GetSymbolSafe(node.Operand, this.cancellationToken);
            if (SymbolComparer.Equals(this.currentSymbol, operand))
            {
                this.assignedValues.Add(new Assignment(this.currentSymbol, node));
            }
            else if (operand is IPropertySymbol)
            {
                this.VisitSetter(operand);
            }

            base.VisitPrefixUnaryExpression(node);
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            var operand = this.semanticModel.GetSymbolSafe(node.Operand, this.cancellationToken);
            if (SymbolComparer.Equals(this.currentSymbol, operand))
            {
                this.assignedValues.Add(new Assignment(this.currentSymbol, node));
            }
            else if (operand is IPropertySymbol)
            {
                this.VisitSetter(operand);
            }

            base.VisitPostfixUnaryExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (this.currentSymbol is IFieldSymbol ||
                this.currentSymbol is IPropertySymbol)
            {
                var method = this.semanticModel.GetSymbolSafe(node, this.cancellationToken);
                if (method != null)
                {
                    if (method.ContainingType.Is(this.currentSymbol.ContainingType) ||
                        this.currentSymbol.ContainingType.Is(method.ContainingType))
                    {
                        foreach (var reference in method.DeclaringSyntaxReferences)
                        {
                            this.Visit(reference.GetSyntax(this.cancellationToken));
                        }
                    }
                }
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitArgument(ArgumentSyntax node)
        {
            if (node.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) ||
                node.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                if (invocation != null &&
                    SymbolComparer.Equals(this.currentSymbol, this.semanticModel.GetSymbolSafe(node.Expression, this.cancellationToken)))
                {
                    this.assignedValues.Add(new Assignment(this.currentSymbol, invocation));
                }
            }

            base.VisitArgument(node);
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            if (this.isSamplingRetunValues)
            {
                this.assignedValues.Add(new Assignment(this.currentSymbol, node.Expression));
            }

            base.VisitArrowExpressionClause(node);
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (this.isSamplingRetunValues)
            {
                this.assignedValues.Add(new Assignment(this.currentSymbol, node.Expression));
            }

            base.VisitReturnStatement(node);
        }

        internal static Pool<AssignedValueWalker>.Pooled Create(IPropertySymbol property, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            return CreateCore(property, null, semanticModel, cancellationToken);
        }

        internal static Pool<AssignedValueWalker>.Pooled Create(IFieldSymbol field, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            return CreateCore(field, null, semanticModel, cancellationToken);
        }

        internal static Pool<AssignedValueWalker>.Pooled Create(ExpressionSyntax value, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            var symbol = semanticModel.GetSymbolSafe(value, cancellationToken);
            if (symbol is IFieldSymbol ||
                symbol is IPropertySymbol ||
                symbol is ILocalSymbol ||
                symbol is IParameterSymbol)
            {
                return CreateCore(symbol, value, semanticModel, cancellationToken);
            }

            return Pool.GetOrCreate();
        }

        internal static Pool<AssignedValueWalker>.Pooled Create(ISymbol symbol, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            if (symbol is IFieldSymbol ||
                symbol is IPropertySymbol ||
                symbol is ILocalSymbol ||
                symbol is IParameterSymbol)
            {
                return CreateCore(symbol, null, semanticModel, cancellationToken);
            }

            return Pool.GetOrCreate();
        }

        internal bool HasChecked(ISymbol symbol) => this.checkedSymbols.Contains(symbol);

        internal void AppendAssignmentsFor(ExpressionSyntax assignedValue)
        {
            this.currentSymbol = this.semanticModel.GetSymbolSafe(assignedValue, this.cancellationToken);
            this.Run();
            var parameter = this.currentSymbol as IParameterSymbol;
            if (parameter?.Name == "value")
            {
                this.currentSymbol = (parameter.ContainingSymbol as IMethodSymbol)?.AssociatedSymbol as IPropertySymbol;
                this.Run();
            }
        }

        private static Pool<AssignedValueWalker>.Pooled CreateCore(ISymbol symbol, SyntaxNode context, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            if (symbol == null)
            {
                return Pool.GetOrCreate();
            }

            var pooled = Pool.GetOrCreate();
            pooled.Item.currentSymbol = symbol;
            pooled.Item.context = context;
            pooled.Item.semanticModel = semanticModel;
            pooled.Item.cancellationToken = cancellationToken;
            if (context != null)
            {
                pooled.Item.Run();
            }
            else
            {
                foreach (var reference in symbol.DeclaringSyntaxReferences)
                {
                    pooled.Item.context = symbol is IFieldSymbol || symbol is IPropertySymbol
                                              ? reference.GetSyntax(cancellationToken)
                                                         .FirstAncestor<TypeDeclarationSyntax>()
                                              : reference.GetSyntax(cancellationToken)
                                                         .FirstAncestor<MemberDeclarationSyntax>();
                    pooled.Item.Run();
                }
            }

            return pooled;
        }

        private void Run()
        {
            if (this.currentSymbol == null ||
                !this.checkedSymbols.Add(this.currentSymbol))
            {
                return;
            }

            this.visitedMembers.Clear();
            if (this.currentSymbol is IFieldSymbol ||
                this.currentSymbol is IPropertySymbol)
            {
                foreach (var reference in this.currentSymbol.DeclaringSyntaxReferences)
                {
                    var memberDeclaration = reference.GetSyntax(this.cancellationToken)?.FirstAncestorOrSelf<MemberDeclarationSyntax>();
                    AccessorDeclarationSyntax getter;
                    if ((memberDeclaration as PropertyDeclarationSyntax).TryGetGetAccessorDeclaration(out getter))
                    {
                        this.isSamplingRetunValues = true;
                        this.Visit(getter);
                        this.isSamplingRetunValues = false;
                        this.visitedMembers.Remove(memberDeclaration).IgnoreReturnValue();
                    }

                    if (memberDeclaration != null)
                    {
                        this.Visit(memberDeclaration);
                    }
                }
            }

            var ctor = this.context?.FirstAncestorOrSelf<ConstructorDeclarationSyntax>();
            if (ctor != null)
            {
                this.Visit(ctor);
            }
            else
            {
                var typeDeclaration = this.context?.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                if (typeDeclaration == null)
                {
                    return;
                }

                if (this.currentSymbol is IFieldSymbol ||
                    this.currentSymbol is IPropertySymbol)
                {
                    var type = (INamedTypeSymbol)this.semanticModel.GetDeclaredSymbolSafe(typeDeclaration, this.cancellationToken);
                    foreach (var typeCtor in type.Constructors)
                    {
                        if (Constructor.IsCalledByOther(typeCtor, this.semanticModel, this.cancellationToken))
                        {
                            continue;
                        }

                        if (typeCtor.DeclaringSyntaxReferences.Length == 0)
                        {
                            IMethodSymbol defaultCtor;
                            if (Constructor.TryGetDefault(type.BaseType, out defaultCtor))
                            {
                                foreach (var reference in defaultCtor.DeclaringSyntaxReferences)
                                {
                                    this.Visit(reference.GetSyntax(this.cancellationToken));
                                }
                            }
                        }

                        foreach (var reference in typeCtor.DeclaringSyntaxReferences)
                        {
                            ctor = (ConstructorDeclarationSyntax)reference.GetSyntax(this.cancellationToken);
                            this.Visit(ctor);
                        }
                    }

                    this.Visit(typeDeclaration);
                }
                else
                {
                    var memnber = this.context?.FirstAncestorOrSelf<MemberDeclarationSyntax>();
                    this.Visit(memnber ?? typeDeclaration);
                }
            }
        }

        private void VisitSetter(ISymbol left)
        {
            if (left is IPropertySymbol)
            {
                foreach (var reference in left.DeclaringSyntaxReferences)
                {
                    AccessorDeclarationSyntax setter;
                    if ((reference.GetSyntax(this.cancellationToken) as PropertyDeclarationSyntax).TryGetSetAccessorDeclaration(out setter))
                    {
                        this.Visit(setter);
                    }
                }
            }
        }

        private bool IsBeforeInScope(SyntaxNode node)
        {
            if (this.context == null ||
                node is BlockSyntax ||
                node.FirstAncestorOrSelf<StatementSyntax>() == null)
            {
                return true;
            }

            if (this.currentSymbol is IParameterSymbol ||
                this.currentSymbol is ILocalSymbol)
            {
                return node.IsBeforeInScope(this.context);
            }

            if (
                !this.context.SharesAncestor<ConstructorDeclarationSyntax>(node))
            {
                return true;
            }

            return node.IsBeforeInScope(this.context);
        }
    }
}