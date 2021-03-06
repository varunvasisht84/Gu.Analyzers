﻿namespace Gu.Analyzers
{
    using System.Collections.Immutable;
    using System.Composition;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Editing;

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ImplementIDisposableCodeFixProvider))]
    [Shared]
    internal class ImplementIDisposableCodeFixProvider : CodeFixProvider
    {
        private static readonly ParameterSyntax DisposingParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("disposing")).WithType(SyntaxFactory.ParseTypeName("bool"));

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(
            GU0035ImplementIDisposable.DiagnosticId,
            "CS0535");

        /// <inheritdoc/>
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var syntaxRoot = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                                          .ConfigureAwait(false) as CompilationUnitSyntax;
            if (syntaxRoot == null)
            {
                return;
            }

            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
                                             .ConfigureAwait(false);

            foreach (var diagnostic in context.Diagnostics)
            {
                if (!IsSupportedDiagnostic(diagnostic))
                {
                    continue;
                }

                var token = syntaxRoot.FindToken(diagnostic.Location.SourceSpan.Start);
                if (string.IsNullOrEmpty(token.ValueText) ||
                    token.IsMissing)
                {
                    continue;
                }

                var typeDeclaration = syntaxRoot.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>();
                if (typeDeclaration == null)
                {
                    continue;
                }

                var type = semanticModel.GetDeclaredSymbolSafe(typeDeclaration, context.CancellationToken);

                if (Disposable.IsAssignableTo(type) &&
                    Disposable.BaseTypeHasVirtualDisposeMethod(type))
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "override Dispose(bool)",
                            cancellationToken =>
                                ApplyOverrideDisposeFixAsync(
                                    context,
                                    semanticModel,
                                    cancellationToken,
                                    syntaxRoot,
                                    typeDeclaration),
                            nameof(ImplementIDisposableCodeFixProvider)),
                        diagnostic);
                    continue;
                }

                if (type.IsSealed)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Implement IDisposable.",
                            cancellationToken =>
                                ApplyImplementIDisposableSealedFixAsync(
                                    context,
                                    semanticModel,
                                    cancellationToken,
                                    syntaxRoot,
                                    typeDeclaration),
                            nameof(ImplementIDisposableCodeFixProvider) + "Sealed"),
                        diagnostic);
                    continue;
                }

                if (type.IsAbstract)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Implement IDisposable with virtual dispose method.",
                            cancellationToken =>
                                ApplyImplementIDisposableVirtualFixAsync(
                                    context,
                                    semanticModel,
                                    cancellationToken,
                                     syntaxRoot,
                                    typeDeclaration),
                            nameof(ImplementIDisposableCodeFixProvider) + "Virtual"),
                        diagnostic);
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Implement IDisposable and make class sealed.",
                        cancellationToken =>
                            ApplyImplementIDisposableSealedFixAsync(
                                context,
                                semanticModel,
                                cancellationToken,
                                 syntaxRoot,
                                typeDeclaration),
                        nameof(ImplementIDisposableCodeFixProvider) + "Sealed"),
                    diagnostic);

                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Implement IDisposable with virtual dispose method.",
                        cancellationToken =>
                            ApplyImplementIDisposableVirtualFixAsync(
                                context,
                                semanticModel,
                                cancellationToken,
                                 syntaxRoot,
                                typeDeclaration),
                        nameof(ImplementIDisposableCodeFixProvider) + "Virtual"),
                    diagnostic);
            }
        }

        private static bool IsSupportedDiagnostic(Diagnostic diagnostic)
        {
            if (diagnostic.Id == GU0035ImplementIDisposable.DiagnosticId)
            {
                return true;
            }

            if (diagnostic.Id == "CS0535")
            {
                return diagnostic.GetMessage(CultureInfo.InvariantCulture)
                                 .EndsWith("does not implement interface member 'IDisposable.Dispose()'");
            }

            return false;
        }

        private static Task<Document> ApplyOverrideDisposeFixAsync(CodeFixContext context, SemanticModel semanticModel, CancellationToken cancellationToken, SyntaxNode syntaxRoot, TypeDeclarationSyntax typeDeclaration)
        {
            var type = (ITypeSymbol)semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
            var syntaxGenerator = SyntaxGenerator.GetGenerator(context.Document);
            TypeDeclarationSyntax updated = typeDeclaration;
            if (!type.TryGetMethod("Dispose", out IMethodSymbol _))
            {
                var usesUnderscoreNames = typeDeclaration.UsesUnderscoreNames(semanticModel, cancellationToken);
                updated = updated.WithDisposedField(type, syntaxGenerator, usesUnderscoreNames);

                var disposeMethod = syntaxGenerator.MethodDeclaration(
                    name: "Dispose",
                    accessibility: Accessibility.Protected,
                    modifiers: DeclarationModifiers.Override,
                    parameters: new[] { DisposingParameter },
                    statements: new[]
                    {
                        syntaxGenerator.IfDisposedReturn(usesUnderscoreNames),
                        syntaxGenerator.SetDisposedTrue(usesUnderscoreNames),
                        syntaxGenerator.IfDisposing(),
                        SyntaxFactory.ParseStatement("base.Dispose(disposing);")
                    });

                if (updated.Members.TryGetLast(
                       x => (x as MethodDeclarationSyntax)?.Modifiers.Any(SyntaxKind.ProtectedKeyword) == true,
                       out SyntaxNode method))
                {
                    updated = updated.InsertNodesAfter(method, new[] { disposeMethod });
                }
                else
                {
                    updated = (TypeDeclarationSyntax)syntaxGenerator.AddMembers(updated, disposeMethod);
                }
            }

            return Task.FromResult(context.Document.WithSyntaxRoot(syntaxRoot.ReplaceNode(typeDeclaration, updated)));
        }

        private static Task<Document> ApplyImplementIDisposableVirtualFixAsync(CodeFixContext context, SemanticModel semanticModel, CancellationToken cancellationToken, CompilationUnitSyntax syntaxRoot, TypeDeclarationSyntax typeDeclaration)
        {
            var type = (ITypeSymbol)semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
            var syntaxGenerator = SyntaxGenerator.GetGenerator(context.Document);
            TypeDeclarationSyntax updated = typeDeclaration;

            if (!type.TryGetMethod("Dispose", out IMethodSymbol _))
            {
                var usesUnderscoreNames = typeDeclaration.UsesUnderscoreNames(semanticModel, cancellationToken);
                updated = updated.WithDisposedField(type, syntaxGenerator, usesUnderscoreNames);

                var disposeMethod = syntaxGenerator.MethodDeclaration(
                    "Dispose",
                    accessibility: Accessibility.Public,
                    statements: new[] { SyntaxFactory.ParseStatement(usesUnderscoreNames ? "Dispose(true);" : "this.Dispose(true);") });

                if (updated.Members.TryGetLast(
                       x => (x as MethodDeclarationSyntax)?.Modifiers.Any(SyntaxKind.PublicKeyword) == true,
                       out MemberDeclarationSyntax method))
                {
                    updated = updated.InsertNodesAfter(method, new[] { disposeMethod });
                }
                else if (updated.Members.TryGetFirst(x => x.IsKind(SyntaxKind.MethodDeclaration), out method))
                {
                    updated = updated.InsertNodesBefore(method, new[] { disposeMethod });
                }
                else
                {
                    updated = (TypeDeclarationSyntax)syntaxGenerator.AddMembers(updated, disposeMethod);
                }

                var virtualDisposeMethod = syntaxGenerator.MethodDeclaration(
                    name: "Dispose",
                    accessibility: Accessibility.Protected,
                    modifiers: DeclarationModifiers.Virtual,
                    parameters: new[] { DisposingParameter },
                    statements:
                    new[]
                        {
                            syntaxGenerator.IfDisposedReturn(usesUnderscoreNames),
                            syntaxGenerator.SetDisposedTrue(usesUnderscoreNames),
                            syntaxGenerator.IfDisposing(),
                        });

                if (updated.Members.TryGetLast(
                                       x => (x as MethodDeclarationSyntax)?.Modifiers.Any(SyntaxKind.PublicKeyword) == true,
                                       out method))
                {
                    updated = updated.InsertNodesAfter(method, new[] { virtualDisposeMethod });
                }
                else if (updated.Members.TryGetFirst(x => x.IsKind(SyntaxKind.MethodDeclaration), out method))
                {
                    updated = updated.InsertNodesBefore(method, new[] { virtualDisposeMethod });
                }
                else
                {
                    updated = (TypeDeclarationSyntax)syntaxGenerator.AddMembers(updated, virtualDisposeMethod);
                }

                updated = updated.WithThrowIfDisposed(type, syntaxGenerator, usesUnderscoreNames);
            }

            updated = updated.WithIDisposableInterface(syntaxGenerator, type);
            var newRoot = syntaxRoot.ReplaceNode(typeDeclaration, updated);
            newRoot = newRoot.WithUsingSystem();

            return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
        }

        private static Task<Document> ApplyImplementIDisposableSealedFixAsync(CodeFixContext context, SemanticModel semanticModel, CancellationToken cancellationToken, CompilationUnitSyntax syntaxRoot, TypeDeclarationSyntax typeDeclaration)
        {
            var type = (ITypeSymbol)semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
            var syntaxGenerator = SyntaxGenerator.GetGenerator(context.Document);
            TypeDeclarationSyntax updated = typeDeclaration;

            if (!type.TryGetMethod("Dispose", out IMethodSymbol _))
            {
                var usesUnderscoreNames = typeDeclaration.UsesUnderscoreNames(semanticModel, cancellationToken);
                updated = updated.WithDisposedField(type, syntaxGenerator, usesUnderscoreNames);

                var disposeMethod = syntaxGenerator.MethodDeclaration(
                    "Dispose",
                    accessibility: Accessibility.Public,
                    statements: new[]
                    {
                        syntaxGenerator.IfDisposedReturn(usesUnderscoreNames),
                        syntaxGenerator.SetDisposedTrue(usesUnderscoreNames)
                    });

                if (updated.Members.TryGetLast(
                       x => (x as MethodDeclarationSyntax)?.Modifiers.Any(SyntaxKind.PublicKeyword) == true,
                       out MemberDeclarationSyntax method))
                {
                    updated = updated.InsertNodesAfter(method, new[] { disposeMethod });
                }
                else if (updated.Members.TryGetFirst(x => x.IsKind(SyntaxKind.MethodDeclaration), out method))
                {
                    updated = updated.InsertNodesBefore(method, new[] { disposeMethod });
                }
                else
                {
                    updated = (TypeDeclarationSyntax)syntaxGenerator.AddMembers(updated, disposeMethod);
                }

                updated = updated.WithThrowIfDisposed(type, syntaxGenerator, usesUnderscoreNames);
            }

            if (!IsSealed(typeDeclaration))
            {
                updated = (TypeDeclarationSyntax)syntaxGenerator.WithModifiers(updated, DeclarationModifiers.Sealed);
                updated = (TypeDeclarationSyntax)MakeSealedRewriter.Default.Visit(updated);
            }

            updated = updated.WithIDisposableInterface(syntaxGenerator, type);
            var newRoot = syntaxRoot.ReplaceNode(typeDeclaration, updated);
            newRoot = newRoot.WithUsingSystem();

            return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
        }

        private static bool IsSealed(TypeDeclarationSyntax type)
        {
            return type.Modifiers.Any(SyntaxKind.SealedKeyword);
        }

        private class MakeSealedRewriter : CSharpSyntaxRewriter
        {
            public static readonly MakeSealedRewriter Default = new MakeSealedRewriter();

            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.VirtualKeyword), out SyntaxToken modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Remove(modifier));
                }

                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.ProtectedKeyword), out modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Replace(modifier, SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
                }

                return base.VisitFieldDeclaration(node);
            }

            public override SyntaxNode VisitEventDeclaration(EventDeclarationSyntax node)
            {
                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.VirtualKeyword), out SyntaxToken modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Remove(modifier));
                }

                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.ProtectedKeyword), out modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Replace(modifier, SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
                }

                return base.VisitEventDeclaration(node);
            }

            public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.VirtualKeyword), out SyntaxToken modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Remove(modifier));
                }

                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.ProtectedKeyword), out modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Replace(modifier, SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
                }

                return base.VisitPropertyDeclaration(node);
            }

            public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
            {
                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.VirtualKeyword), out SyntaxToken modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Remove(modifier));
                }

                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.ProtectedKeyword), out modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Replace(modifier, SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
                }

                if (node.FirstAncestor<PropertyDeclarationSyntax>()
                        ?.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.PrivateKeyword), out modifier) == true)
                {
                    if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.PrivateKeyword), out modifier))
                    {
                        node = node.WithModifiers(node.Modifiers.Remove(modifier));
                    }
                }

                return base.VisitAccessorDeclaration(node);
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.VirtualKeyword), out SyntaxToken modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Remove(modifier));
                }

                if (node.Modifiers.TryGetSingle(x => x.IsKind(SyntaxKind.ProtectedKeyword), out modifier))
                {
                    node = node.WithModifiers(node.Modifiers.Replace(modifier, SyntaxFactory.Token(SyntaxKind.PrivateKeyword)));
                }

                return base.VisitMethodDeclaration(node);
            }
        }
    }
}