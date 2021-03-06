namespace Gu.Analyzers
{
    using System.Collections.Immutable;
    using System.Composition;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Editing;

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CreateAndAssignFieldCodeFixProvider))]
    [Shared]
    internal class CreateAndAssignFieldCodeFixProvider : CodeFixProvider
    {
        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(
            GU0030DisposeCreated.DiagnosticId,
            GU0033DontIgnoreReturnValueOfTypeIDisposable.DiagnosticId);

        /// <inheritdoc/>
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var syntaxRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                                          .ConfigureAwait(false);

            foreach (var diagnostic in context.Diagnostics)
            {
                var token = syntaxRoot.FindToken(diagnostic.Location.SourceSpan.Start);
                if (string.IsNullOrEmpty(token.ValueText) ||
                    token.IsMissing)
                {
                    continue;
                }

                var node = syntaxRoot.FindNode(diagnostic.Location.SourceSpan);
                if (diagnostic.Id == GU0030DisposeCreated.DiagnosticId)
                {
                    var statement = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
                    if (statement?.FirstAncestor<ConstructorDeclarationSyntax>() != null &&
                        statement.Declaration.Variables.Count == 1 &&
                        statement.Declaration.Variables[0].Initializer != null)
                    {
                        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
                                                                  .ConfigureAwait(false);
                        var type = semanticModel.GetSymbolInfo(statement.Declaration.Type).Symbol as ITypeSymbol;
                        if (type != null)
                        {
                            context.RegisterCodeFix(
                                CodeAction.Create(
                                    "Create and assign field.",
                                    _ => ApplyAddUsingFixAsync(context, statement, type),
                                    nameof(CreateAndAssignFieldCodeFixProvider)),
                                diagnostic);
                        }
                    }
                }

                if (diagnostic.Id == GU0033DontIgnoreReturnValueOfTypeIDisposable.DiagnosticId)
                {
                    var statement = node.FirstAncestorOrSelf<ExpressionStatementSyntax>();
                    if (statement?.FirstAncestor<ConstructorDeclarationSyntax>() != null)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Create and assign field.",
                                _ => ApplyAddUsingFixAsync(context, statement),
                                nameof(CreateAndAssignFieldCodeFixProvider)),
                            diagnostic);
                    }
                }
            }
        }

        private static async Task<Document> ApplyAddUsingFixAsync(CodeFixContext context, LocalDeclarationStatementSyntax statement, ITypeSymbol type)
        {
            var editor = await DocumentEditor.CreateAsync(context.Document).ConfigureAwait(false);
            var usesUnderscoreNames = editor.SemanticModel.SyntaxTree.GetRoot().UsesUnderscoreNames(editor.SemanticModel, CancellationToken.None);
            var identifier = statement.Declaration.Variables[0].Identifier;
            var name = usesUnderscoreNames
                ? "_" + identifier.ValueText
                : identifier.ValueText;
            var containingType = statement.FirstAncestor<TypeDeclarationSyntax>();
            var declaredSymbol = editor.SemanticModel.GetDeclaredSymbol(containingType);
            while (declaredSymbol.MemberNames.Contains(name))
            {
                name += "_";
            }

            var newField = (FieldDeclarationSyntax)editor.Generator.FieldDeclaration(
                name,
                accessibility: Accessibility.Private,
                modifiers: DeclarationModifiers.ReadOnly,
                type: SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            var members = containingType.Members;
            if (members.TryGetFirst(x => x is FieldDeclarationSyntax, out MemberDeclarationSyntax field))
            {
                editor.InsertBefore(field, new[] { newField });
            }
            else if (members.TryGetFirst(out field))
            {
                editor.InsertBefore(field, new[] { newField });
            }
            else
            {
                editor.AddMember(containingType, newField);
            }

            var fieldAccess = usesUnderscoreNames
                ? SyntaxFactory.IdentifierName(name)
                : SyntaxFactory.ParseExpression($"this.{name}");
            editor.ReplaceNode(
                statement,
                SyntaxFactory.ExpressionStatement(
                    (ExpressionSyntax)editor.Generator.AssignmentStatement(
                        fieldAccess,
                        statement.Declaration.Variables[0].Initializer.Value))
                             .WithLeadingTrivia(statement.GetLeadingTrivia())
                             .WithTrailingTrivia(statement.GetTrailingTrivia()));
            return editor.GetChangedDocument();
        }

        private static async Task<Document> ApplyAddUsingFixAsync(CodeFixContext context, ExpressionStatementSyntax statement)
        {
            var editor = await DocumentEditor.CreateAsync(context.Document).ConfigureAwait(false);
            var usesUnderscoreNames = editor.SemanticModel.SyntaxTree.GetRoot().UsesUnderscoreNames(editor.SemanticModel, CancellationToken.None);
            var name = usesUnderscoreNames
                ? "_disposable"
                : "disposable";
            var containingType = statement.FirstAncestor<TypeDeclarationSyntax>();
            var declaredSymbol = editor.SemanticModel.GetDeclaredSymbol(containingType);
            while (declaredSymbol.MemberNames.Contains(name))
            {
                name += "_";
            }

            var newField = (FieldDeclarationSyntax)editor.Generator.FieldDeclaration(
                name,
                accessibility: Accessibility.Private,
                modifiers: DeclarationModifiers.ReadOnly,
                type: SyntaxFactory.ParseTypeName("IDisposable"));
            var members = containingType.Members;
            if (members.TryGetFirst(x => x is FieldDeclarationSyntax, out MemberDeclarationSyntax field))
            {
                editor.InsertBefore(field, new[] { newField });
            }
            else if (members.TryGetFirst(out field))
            {
                editor.InsertBefore(field, new[] { newField });
            }
            else
            {
                editor.AddMember(containingType, newField);
            }

            var fieldAccess = usesUnderscoreNames
                ? SyntaxFactory.IdentifierName(name)
                : SyntaxFactory.ParseExpression($"this.{name}");
            editor.ReplaceNode(
                statement,
                SyntaxFactory.ExpressionStatement(
                    (ExpressionSyntax)editor.Generator.AssignmentStatement(fieldAccess, statement.Expression))
                                                      .WithLeadingTrivia(SyntaxFactory.ElasticMarker)
                                                      .WithTrailingTrivia(SyntaxFactory.ElasticMarker));
            return editor.GetChangedDocument();
        }
    }
}