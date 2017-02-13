﻿namespace Gu.Analyzers
{
    using System.Collections.Generic;
    using System.Threading;

    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal sealed class CallsWalker : CSharpSyntaxWalker
    {
        private static readonly Pool<CallsWalker> Pool = new Pool<CallsWalker>(
            () => new CallsWalker(),
            x =>
                {
                    x.invocations.Clear();
                    x.initializers.Clear();
                    x.objectCreations.Clear();
                    x.method = null;
                    x.semanticModel = null;
                    x.cancellationToken = CancellationToken.None;
                });

        private readonly List<InvocationExpressionSyntax> invocations = new List<InvocationExpressionSyntax>();
        private readonly List<ConstructorInitializerSyntax> initializers = new List<ConstructorInitializerSyntax>();
        private readonly List<ObjectCreationExpressionSyntax> objectCreations = new List<ObjectCreationExpressionSyntax>();

        private IMethodSymbol method;
        private SemanticModel semanticModel;
        private CancellationToken cancellationToken;

        private CallsWalker()
        {
        }

        public IReadOnlyList<InvocationExpressionSyntax> Invocations => this.invocations;

        public IReadOnlyList<ConstructorInitializerSyntax> Initializers => this.initializers;

        public IReadOnlyList<ObjectCreationExpressionSyntax> ObjectCreations => this.objectCreations;

        public static Pool<CallsWalker>.Pooled GetCallsInContext(IMethodSymbol method, SyntaxNode context, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            var pooled = Pool.GetOrCreate();
            pooled.Item.method = method;
            pooled.Item.semanticModel = semanticModel;
            pooled.Item.cancellationToken = cancellationToken;
            var type = semanticModel.GetDeclaredSymbolSafe(context.FirstAncestor<TypeDeclarationSyntax>(), cancellationToken);
            while (type != null && type != KnownSymbol.Object)
            {
                foreach (var reference in type.DeclaringSyntaxReferences)
                {
                    pooled.Item.Visit(reference.GetSyntax(cancellationToken));
                }

                type = type.BaseType;
            }

            return pooled;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var invokedMethod = this.semanticModel.GetSymbolSafe(node, this.cancellationToken) as IMethodSymbol;
            if (this.method.Equals(invokedMethod))
            {
                this.invocations.Add(node);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitConstructorInitializer(ConstructorInitializerSyntax node)
        {
            if (this.method.MethodKind == MethodKind.Constructor)
            {
                var ctor = this.semanticModel.GetSymbolSafe(node, this.cancellationToken);
                if (this.method.Equals(ctor))
                {
                    this.initializers.Add(node);
                }
            }
            else
            {
                this.initializers.Add(node);
            }

            base.VisitConstructorInitializer(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (this.method.MethodKind == MethodKind.Constructor)
            {
                var ctor = this.semanticModel.GetSymbolSafe(node, this.cancellationToken);
                if (this.method.Equals(ctor))
                {
                    this.objectCreations.Add(node);
                }
            }
            else
            {
                this.objectCreations.Add(node);
            }

            base.VisitObjectCreationExpression(node);
        }
    }
}