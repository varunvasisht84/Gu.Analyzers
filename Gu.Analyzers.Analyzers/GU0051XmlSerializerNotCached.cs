namespace Gu.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal class GU0051XmlSerializerNotCached : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "GU0051";
        private const string Title = "Cache the XmlSerializer.";
        private const string MessageFormat = "The serializer is not cached.";
        private const string Description = "This constructor loads assemblies in non-GC memory, which may cause memory leaks.";
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
            context.RegisterSyntaxNodeAction(this.HandleObjectCreation, SyntaxKind.ObjectCreationExpression);
        }

        private bool IsLeakyConstructor(SyntaxNodeAnalysisContext context)
        {
            var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
            var ctor = (IMethodSymbol)context.SemanticModel.GetSymbolSafe(objectCreation, context.CancellationToken);
            var parameters = ctor.Parameters;
            if (parameters.Length == 1 && parameters[0].Type == KnownSymbol.Type)
            {
                return false;
            }

            if (parameters.Length == 2 && parameters[0].Type == KnownSymbol.Type && parameters[1].Type == KnownSymbol.String)
            {
                return false;
            }

            return true;
        }

        private void HandleObjectCreation(SyntaxNodeAnalysisContext context)
        {
            if (context.IsExcludedFromAnalysis())
            {
                return;
            }

            var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
            if (objectCreation.IsSameType(KnownSymbol.XmlSerializer, context))
            {
                if (!this.IsLeakyConstructor(context))
                {
                    return;
                }

                var fieldDeclaration = objectCreation.FirstAncestor<VariableDeclaratorSyntax>();
                if (fieldDeclaration != null)
                {
                    var fieldDeclarationSymbol = context.SemanticModel.SemanticModelFor(fieldDeclaration)?.GetDeclaredSymbol(fieldDeclaration, context.CancellationToken) as IFieldSymbol;
                    if (fieldDeclarationSymbol != null &&
                       fieldDeclarationSymbol.IsReadOnly &&
                       fieldDeclarationSymbol.IsStatic)
                    {
                        return;
                    }
                }

                var variableDeclaration = objectCreation.FirstAncestor<VariableDeclarationSyntax>();
                if (variableDeclaration != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Descriptor, variableDeclaration.GetLocation()));
                    return;
                }

                context.ReportDiagnostic(Diagnostic.Create(Descriptor, objectCreation.GetLocation()));
            }
        }
    }
}