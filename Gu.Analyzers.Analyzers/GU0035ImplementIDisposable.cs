﻿namespace Gu.Analyzers
{
    using System.Collections.Immutable;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class GU0035ImplementIDisposable : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "GU0035";
        private const string Title = "Implement IDisposable.";
        private const string MessageFormat = "Implement IDisposable.";
        private const string Description = "The member is assigned with a created `IDisposable`s within the type. Implement IDisposable and dispose it.";
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
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptor);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(HandleField, SyntaxKind.FieldDeclaration);
            context.RegisterSyntaxNodeAction(HandleProperty, SyntaxKind.PropertyDeclaration);
        }

        private static void HandleField(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            var field = (IFieldSymbol)context.ContainingSymbol;
            if (field.IsStatic)
            {
                return;
            }

            if (Disposable.IsAssignedWithCreatedAndNotCachedOrInjected(field, context.SemanticModel, context.CancellationToken))
            {
                CheckThatIDisposalbeIsImplemented(context);
            }
        }

        private static void HandleProperty(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            var property = (IPropertySymbol)context.ContainingSymbol;
            if (property.IsStatic ||
                property.IsIndexer)
            {
                return;
            }

            var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;
            if (propertyDeclaration.ExpressionBody != null)
            {
                return;
            }

            if (propertyDeclaration.TryGetSetAccessorDeclaration(out AccessorDeclarationSyntax setter) &&
    setter.Body != null)
            {
                // Handle the backing field
                return;
            }

            if (Disposable.IsAssignedWithCreatedAndNotCachedOrInjected(property, context.SemanticModel, context.CancellationToken))
            {
                CheckThatIDisposalbeIsImplemented(context);
            }
        }

        private static void CheckThatIDisposalbeIsImplemented(SyntaxNodeAnalysisContext context)
        {
            var containingType = context.ContainingSymbol.ContainingType;
            if (!Disposable.IsAssignableTo(containingType) ||
                !Disposable.TryGetDisposeMethod(containingType, Search.TopLevel, out IMethodSymbol _))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, context.Node.GetLocation()));
            }
        }
    }
}