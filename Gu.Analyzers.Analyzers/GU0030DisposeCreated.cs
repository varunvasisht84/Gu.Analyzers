﻿namespace Gu.Analyzers
{
    using System.Collections.Immutable;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class GU0030DisposeCreated : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "GU0030";
        private const string Title = "Dispose created.";
        private const string MessageFormat = "Dispose created.";
        private const string Description = "Use using for created `IDisposable` that is not assigned to a field or property.";
        private static readonly string HelpLink = Analyzers.HelpLink.ForId(DiagnosticId);

        private static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
            id: DiagnosticId,
            title: Title,
            messageFormat: MessageFormat,
            category: AnalyzerCategory.Correctness,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: AnalyzerConstants.EnabledByDefault,
            description: Description,
            helpLinkUri: HelpLink);

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Descriptor);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(HandleDeclaration, SyntaxKind.VariableDeclaration);
        }

        private static void HandleDeclaration(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            var variableDeclaration = (VariableDeclarationSyntax)context.Node;
            foreach (var declarator in variableDeclaration.Variables)
            {
                var local = context.SemanticModel.GetDeclaredSymbolSafe(declarator, context.CancellationToken) as ILocalSymbol;
                if (local == null ||
                    !Disposable.IsPotentiallyAssignableTo(local.Type))
                {
                    return;
                }

                var value = declarator.Initializer?.Value;
                if (Disposable.IsCreation(value, context.SemanticModel, context.CancellationToken)
                              .IsEither(Result.Yes, Result.Maybe))
                {
                    if (variableDeclaration.Parent is UsingStatementSyntax ||
                        variableDeclaration.Parent is AnonymousFunctionExpressionSyntax)
                    {
                        return;
                    }

                    var block = declarator.FirstAncestorOrSelf<BlockSyntax>();
                    if (block == null)
                    {
                        return;
                    }

                    if (IsReturned(local, block, context.SemanticModel, context.CancellationToken))
                    {
                        return;
                    }

                    if (IsAssignedToFieldOrProperty(local, block, context.SemanticModel, context.CancellationToken))
                    {
                        return;
                    }

                    if (IsAddedToFieldOrProperty(local, block, context.SemanticModel, context.CancellationToken))
                    {
                        return;
                    }

                    if (IsDisposedAfter(local, value, context.SemanticModel, context.CancellationToken))
                    {
                        return;
                    }

                    context.ReportDiagnostic(Diagnostic.Create(Descriptor, variableDeclaration.GetLocation()));
                }
            }
        }

        private static bool IsReturned(ILocalSymbol symbol, BlockSyntax block, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            using (var pooled = ReturnValueWalker.Create(block, Search.TopLevel, semanticModel, cancellationToken))
            {
                foreach (var value in pooled.Item)
                {
                    var returnedSymbol = semanticModel.GetSymbolSafe(value, cancellationToken);
                    if (SymbolComparer.Equals(symbol, returnedSymbol))
                    {
                        return true;
                    }

                    if (value is ObjectCreationExpressionSyntax objectCreation)
                    {
                        if (objectCreation.ArgumentList != null)
                        {
                            foreach (var argument in objectCreation.ArgumentList.Arguments)
                            {
                                var arg = semanticModel.GetSymbolSafe(argument.Expression, cancellationToken);
                                if (SymbolComparer.Equals(symbol, arg))
                                {
                                    return true;
                                }
                            }
                        }

                        if (objectCreation.Initializer != null)
                        {
                            foreach (var argument in objectCreation.Initializer.Expressions)
                            {
                                var arg = semanticModel.GetSymbolSafe(argument, cancellationToken);
                                if (SymbolComparer.Equals(symbol, arg))
                                {
                                    return true;
                                }
                            }
                        }
                    }

                    if (value is InvocationExpressionSyntax invocation)
                    {
                        if (returnedSymbol == KnownSymbol.RxDisposable.Create &&
                            invocation.ArgumentList != null &&
                            invocation.ArgumentList.Arguments.TryGetSingle(out ArgumentSyntax argument))
                        {
                            var body = (argument.Expression as ParenthesizedLambdaExpressionSyntax)?.Body;
                            using (var pooledInvocations = InvocationWalker.Create(body))
                            {
                                foreach (var candidate in pooledInvocations.Item.Invocations)
                                {
                                    if (Disposable.IsDisposing(candidate, symbol, semanticModel, cancellationToken))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsAssignedToFieldOrProperty(ILocalSymbol symbol, BlockSyntax block, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            AssignmentExpressionSyntax assignment = null;
            if (block?.TryGetAssignment(symbol, semanticModel, cancellationToken, out assignment) == true)
            {
                var left = semanticModel.GetSymbolSafe(assignment.Left, cancellationToken) ??
                           semanticModel.GetSymbolSafe((assignment.Left as ElementAccessExpressionSyntax)?.Expression, cancellationToken);
                return left is IFieldSymbol || left is IPropertySymbol || left is ILocalSymbol || left is IParameterSymbol;
            }

            return false;
        }

        private static bool IsAddedToFieldOrProperty(ILocalSymbol symbol, BlockSyntax block, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            using (var pooledInvocations = InvocationWalker.Create(block))
            {
                foreach (var invocation in pooledInvocations.Item.Invocations)
                {
                    var method = semanticModel.GetSymbolSafe(invocation, cancellationToken) as IMethodSymbol;
                    if (method?.Name == "Add")
                    {
                        using (var pooledIdentifiers = IdentifierNameWalker.Create(invocation.ArgumentList))
                        {
                            foreach (var identifierName in pooledIdentifiers.Item.IdentifierNames)
                            {
                                var argSymbol = semanticModel.GetSymbolSafe(identifierName, cancellationToken);
                                if (symbol.Equals(argSymbol))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsDisposedAfter(ISymbol symbol, ExpressionSyntax assignment, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            using (var pooled = InvocationWalker.Create(assignment.FirstAncestorOrSelf<MemberDeclarationSyntax>()))
            {
                foreach (var invocation in pooled.Item.Invocations)
                {
                    if (!IsAfter(invocation, assignment))
                    {
                        continue;
                    }

                    if (Disposable.IsDisposing(invocation, symbol, semanticModel, cancellationToken))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsAfter(SyntaxNode node, SyntaxNode other)
        {
            var statement = node?.FirstAncestorOrSelf<StatementSyntax>();
            var otherStatement = other?.FirstAncestorOrSelf<StatementSyntax>();
            if (statement == null ||
                otherStatement == null)
            {
                return false;
            }

            if (statement.SpanStart <= otherStatement.SpanStart)
            {
                return false;
            }

            var block = node.FirstAncestor<BlockSyntax>();
            var otherBlock = other.FirstAncestor<BlockSyntax>();

            if (block == null || otherBlock == null)
            {
                return false;
            }

            return ReferenceEquals(block, otherBlock);
        }
    }
}