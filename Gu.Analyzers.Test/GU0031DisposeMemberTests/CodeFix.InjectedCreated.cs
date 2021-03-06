﻿namespace Gu.Analyzers.Test.GU0031DisposeMemberTests
{
    using System.Threading.Tasks;

    using NUnit.Framework;

    internal partial class CodeFix : CodeFixVerifier<GU0031DisposeMember, DisposeMemberCodeFixProvider>
    {
        internal class InjectedCreated : NestedCodeFixVerifier<CodeFix>
        {
            [Test]
            public async Task CtorPassingCreatedIntoPrivateCtor()
            {
                var testCode = @"
using System;

public sealed class Foo : IDisposable
{
    ↓private readonly IDisposable disposable;

    public Foo()
        : this(new Disposable())
    {
    }

    private Foo(IDisposable disposable)
    {
        this.disposable = disposable;
    }

    public void Dispose()
    {
    }
}";
                var expected = this.CSharpDiagnostic()
                                   .WithLocationIndicated(ref testCode)
                                   .WithMessage("Dispose member.");
                await this.VerifyCSharpDiagnosticAsync(new[] { DisposableCode, testCode }, expected)
                          .ConfigureAwait(false);

                var fixedCode = @"
using System;

public sealed class Foo : IDisposable
{
    private readonly IDisposable disposable;

    public Foo()
        : this(new Disposable())
    {
    }

    private Foo(IDisposable disposable)
    {
        this.disposable = disposable;
    }

    public void Dispose()
    {
        this.disposable.Dispose();
    }
}";
                await this.VerifyCSharpFixAsync(new[] { DisposableCode, testCode }, new[] { DisposableCode, fixedCode })
                          .ConfigureAwait(false);
            }

            [Test]
            public async Task FieldAssignedWithFactoryPassingCreatedIntoPrivateCtor()
            {
                var testCode = @"
using System;

public sealed class Foo : IDisposable
{
    ↓private readonly IDisposable disposable;

    private Foo(IDisposable disposable)
    {
        this.disposable = disposable;
    }

    public static Foo Create()
    {
        return new Foo(new Disposable());
    }

    public void Dispose()
    {
    }
}";
                var expected = this.CSharpDiagnostic()
                                   .WithLocationIndicated(ref testCode)
                                   .WithMessage("Dispose member.");
                await this.VerifyCSharpDiagnosticAsync(new[] { DisposableCode, testCode }, expected)
                          .ConfigureAwait(false);

                var fixedCode = @"
using System;

public sealed class Foo : IDisposable
{
    private readonly IDisposable disposable;

    private Foo(IDisposable disposable)
    {
        this.disposable = disposable;
    }

    public static Foo Create()
    {
        return new Foo(new Disposable());
    }

    public void Dispose()
    {
        this.disposable.Dispose();
    }
}";
                await this.VerifyCSharpFixAsync(new[] { DisposableCode, testCode }, new[] { DisposableCode, fixedCode })
                          .ConfigureAwait(false);
            }

            [Test]
            public async Task FieldAssignedWithExtensionMethodFactoryAssigningInCtor()
            {
                var factoryCode = @"
using System;

public static class Factory
{
    public static IDisposable AsDisposable(this object value)
    {
        return new Disposable();
    }
}";

                var testCode = @"
using System;

public sealed class Foo : IDisposable
{
    ↓private readonly IDisposable disposable;

    public Foo(object value)
    {
        this.disposable = value.AsDisposable();
    }

    public void Dispose()
    {
    }
}";
                var expected = this.CSharpDiagnostic()
                                   .WithLocationIndicated(ref testCode)
                                   .WithMessage("Dispose member.");
                await this.VerifyCSharpDiagnosticAsync(new[] { DisposableCode, factoryCode, testCode }, expected)
                          .ConfigureAwait(false);

                var fixedCode = @"
using System;

public sealed class Foo : IDisposable
{
    private readonly IDisposable disposable;

    public Foo(object value)
    {
        this.disposable = value.AsDisposable();
    }

    public void Dispose()
    {
        this.disposable?.Dispose();
    }
}";
                await this.VerifyCSharpFixAsync(new[] { DisposableCode, factoryCode, testCode }, new[] { DisposableCode, factoryCode, fixedCode })
              .ConfigureAwait(false);
            }

            [Test]
            public async Task FieldAssignedWithGenericExtensionMethodFactoryAssigningInCtor()
            {
                var factoryCode = @"
using System;

public static class Factory
{
    public static IDisposable AsDisposable<T>(this T value)
    {
        return new Disposable();
    }
}";

                var testCode = @"
using System;

public sealed class Foo : IDisposable
{
    ↓private readonly IDisposable disposable;

    public Foo(object value)
    {
        this.disposable = value.AsDisposable();
    }

    public void Dispose()
    {
    }
}";
                var expected = this.CSharpDiagnostic()
                                   .WithLocationIndicated(ref testCode)
                                   .WithMessage("Dispose member.");
                await this.VerifyCSharpDiagnosticAsync(new[] { DisposableCode, factoryCode, testCode }, expected)
                          .ConfigureAwait(false);

                var fixedCode = @"
using System;

public sealed class Foo : IDisposable
{
    private readonly IDisposable disposable;

    public Foo(object value)
    {
        this.disposable = value.AsDisposable();
    }

    public void Dispose()
    {
        this.disposable?.Dispose();
    }
}";
                await this.VerifyCSharpFixAsync(new[] { DisposableCode, factoryCode, testCode }, new[] { DisposableCode, factoryCode, fixedCode })
              .ConfigureAwait(false);
            }

            [Test]
            public async Task FieldAssignedWithInjectedListOfIntGetEnumeratorInCtor()
            {
                var testCode = @"
namespace RoslynSandbox
{
    using System;
    using System.Collections.Generic;

    public class Foo : IDisposable
    {
        ↓private readonly IEnumerator<int> current;

        public Foo(List<int> list)
        {
            this.current = list.GetEnumerator();
        }

        public void Dispose()
        {
        }
    }
}";
                var expected = this.CSharpDiagnostic()
                                   .WithLocationIndicated(ref testCode)
                                   .WithMessage("Dispose member.");
                await this.VerifyCSharpDiagnosticAsync(testCode, expected).ConfigureAwait(false);

                var fixedCode = @"
namespace RoslynSandbox
{
    using System;
    using System.Collections.Generic;

    public class Foo : IDisposable
    {
        private readonly IEnumerator<int> current;

        public Foo(List<int> list)
        {
            this.current = list.GetEnumerator();
        }

        public void Dispose()
        {
            this.current?.Dispose();
        }
    }
}";
                await this.VerifyCSharpFixAsync(testCode, fixedCode).ConfigureAwait(false);
            }
        }
    }
}