﻿namespace Gu.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class GU0011DontIgnoreReturnValue : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "GU0011";
        private const string Title = "Don't ignore returnvalue.";
        private const string MessageFormat = "Don't ignore returnvalue.";
        private const string Description = "Don't ignore returnvalue.";
        private static readonly string HelpLink = Analyzers.HelpLink.ForId(DiagnosticId);

        private static readonly DiagnosticDescriptor Descriptor = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            AnalyzerCategory.Correctness,
            DiagnosticSeverity.Hidden,
            AnalyzerConstants.EnabledByDefault,
            Description,
            HelpLink);

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
            var objectCreation = (ObjectCreationExpressionSyntax)context.Node;

            if (IsIgnored(objectCreation))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, context.Node.GetLocation()));
            }
        }

        private static void HandleInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation == null || CanIgnore(invocation))
            {
                return;
            }

            if (IsIgnored(invocation))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, context.Node.GetLocation()));
            }
        }

        private static bool CanIgnore(InvocationExpressionSyntax invocation)
        {
            // Returning self should be allowed like StringBuilder.Write()
            return false;
        }

        private static bool IsIgnored(SyntaxNode node)
        {
            if (node.Parent is StatementSyntax)
            {
                return !(node.Parent is ReturnStatementSyntax);
            }

            var argument = node.FirstAncestorOrSelf<ArgumentSyntax>();
            if (argument != null)
            {
                var objectCreation = argument.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
                if ((objectCreation?.Type as IdentifierNameSyntax)?.Identifier.ValueText.Contains("Reader") == true)
                {
                    return false;
                }

                return true;
            }

            return false;
        }
    }
}