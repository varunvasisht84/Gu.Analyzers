namespace Gu.Analyzers.Test.GU0033DontIgnoreReturnValueOfTypeIDisposableTests
{
    using System.Threading.Tasks;
    using NUnit.Framework;

    internal partial class HappyPath : HappyPathVerifier<GU0033DontIgnoreReturnValueOfTypeIDisposable>
    {
        private static readonly string DisposableCode = @"
using System;

public class Disposable : IDisposable
{
    public void Dispose()
    {
    }
}";

        [Test]
        public async Task AssigningLocal()
        {
            var testCode = @"
using System;

public sealed class Foo
{
    public Foo()
    {
        var disposable = new Disposable();
    }
}";
            await this.VerifyHappyPathAsync(DisposableCode, testCode)
                      .ConfigureAwait(false);
        }

        [Test]
        public async Task ChainedCtor()
        {
            var testCode = @"
using System;

public sealed class Foo : IDisposable
{
    public Foo()
        : this(new Disposable())
    {
    }

    private Foo(IDisposable disposable)
    {
        this.Disposable = disposable;
    }

    public IDisposable Disposable { get; }

    public void Dispose()
    {
        this.Disposable.Dispose();
    }
}";
            await this.VerifyHappyPathAsync(DisposableCode, testCode)
                      .ConfigureAwait(false);
        }

        [Test]
        public async Task ChainedCtors()
        {
            var testCode = @"
using System;

public sealed class Foo : IDisposable
{
    private readonly int meh;

    public Foo()
        : this(new Disposable())
    {
    }

    private Foo(IDisposable disposable)
        : this(disposable, 1)
    {
    }

    private Foo(IDisposable disposable, int meh)
    {
        this.meh = meh;
        this.Disposable = disposable;
    }

    public IDisposable Disposable { get; }

    public void Dispose()
    {
        this.Disposable.Dispose();
    }
}";
            await this.VerifyHappyPathAsync(DisposableCode, testCode)
                      .ConfigureAwait(false);
        }

        [Test]
        public async Task ChainedCtorCallsBaseCtorDisposedInThis()
        {
            var baseCode = @"
using System;

public class FooBase : IDisposable
{
    private readonly IDisposable disposable;
    private bool disposed;

    protected FooBase(IDisposable disposable)
    {
        this.disposable = disposable;
    }

    public IDisposable Disposable => this.disposable;

    public void Dispose()
    {
        this.Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
    }
}";

            var testCode = @"
using System;

public sealed class Foo : FooBase
{
    private bool disposed;

    public Foo()
        : this(new Disposable())
    {
    }

    private Foo(IDisposable disposable)
        : base(disposable)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        if (disposing)
        {
            this.Disposable.Dispose();
        }

        base.Dispose(disposing);
    }
}";
            await this.VerifyHappyPathAsync(DisposableCode, baseCode, testCode)
                      .ConfigureAwait(false);
        }

        [Test]
        public async Task ChainedBaseCtorDisposedInThis()
        {
            var baseCode = @"
using System;

public class FooBase : IDisposable
{
    private readonly object disposable;
    private bool disposed;

    protected FooBase(IDisposable disposable)
    {
        this.disposable = disposable;
    }

    public object Bar
    {
        get
        {
            return this.disposable;
        }
    }

    public void Dispose()
    {
        this.Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
    }
}";

            var testCode = @"
using System;

public sealed class Foo : FooBase
{
    private bool disposed;

    public Foo()
        : base(new Disposable())
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        if (disposing)
        {
            (this.Bar as IDisposable)?.Dispose();
        }

        base.Dispose(disposing);
    }
}";
            await this.VerifyHappyPathAsync(DisposableCode, baseCode, testCode)
                      .ConfigureAwait(false);
        }

        [Test]
        public async Task RealisticExtensionMethodClass()
        {
            var testCode = @"
    using System;
    using System.Collections.Generic;

    internal static class EnumerableExt
    {
        internal static bool TryGetAtIndex<TCollection, TItem>(this TCollection source, int index, out TItem result)
            where TCollection : IReadOnlyList<TItem>
        {
            result = default(TItem);
            if (source == null)
            {
                return false;
            }

            if (source.Count <= index)
            {
                return false;
            }

            result = source[index];
            return true;
        }

        internal static bool TryGetSingle<TCollection, TItem>(this TCollection source, out TItem result)
            where TCollection : IReadOnlyList<TItem>
        {
            if (source.Count == 1)
            {
                result = source[0];
                return true;
            }

            result = default(TItem);
            return false;
        }

        internal static bool TryGetSingle<TCollection, TItem>(this TCollection source, Func<TItem, bool> selector, out TItem result)
            where TCollection : IReadOnlyList<TItem>
        {
            foreach (var item in source)
            {
                if (selector(item))
                {
                    result = item;
                    return true;
                }
            }

            result = default(TItem);
            return false;
        }

        internal static bool TryGetFirst<TCollection, TItem>(this TCollection source, out TItem result)
            where TCollection : IReadOnlyList<TItem>
        {
            if (source.Count == 0)
            {
                result = default(TItem);
                return false;
            }

            result = source[0];
            return true;
        }

        internal static bool TryGetFirst<TCollection, TItem>(this TCollection source, Func<TItem, bool> selector, out TItem result)
            where TCollection : IReadOnlyList<TItem>
        {
            foreach (var item in source)
            {
                if (selector(item))
                {
                    result = item;
                    return true;
                }
            }

            result = default(TItem);
            return false;
        }

        internal static bool TryGetLast<TCollection, TItem>(this TCollection source, out TItem result)
            where TCollection : IReadOnlyList<TItem>
        {
            if (source.Count == 0)
            {
                result = default(TItem);
                return false;
            }

            result = source[source.Count - 1];
            return true;
        }

        internal static bool TryGetLast<TCollection, TItem>(this TCollection source, Func<TItem, bool> selector, out TItem result)
             where TCollection : IReadOnlyList<TItem>
        {
            for (var i = source.Count - 1; i >= 0; i--)
            {
                var item = source[i];
                if (selector(item))
                {
                    result = item;
                    return true;
                }
            }

            result = default(TItem);
            return false;
        }
    }";

            await this.VerifyHappyPathAsync(testCode)
                      .ConfigureAwait(false);
        }

        [Test]
        public async Task IfTry()
        {
            var testCode = @"
public class Foo
{
    private void Bar()
    {
        int value;
        if(Try(out value))
        {
        }
    }

    private bool Try(out int value)
    {
        value = 1;
        return true;
    }
}";

            await this.VerifyHappyPathAsync(testCode)
                      .ConfigureAwait(false);
        }

        [Test]
        public async Task MatehodWithFuncTaskAsParameter()
        {
            var testCode = @"
using System;
using System.Threading.Tasks;
public class Foo
{
    public void Meh()
    {
        this.Bar(() => Task.Delay(0));
    }
    public void Bar(Func<Task> func)
    {
    }
}";

            await this.VerifyHappyPathAsync(testCode)
                      .ConfigureAwait(false);
        }

        [Test]
        public async Task MethodWithFuncStreamAsParameter()
        {
            var testCode = @"
using System;
using System.IO;

public class Foo
{
    public void Meh()
    {
        this.Bar(() => File.OpenRead(string.Empty));
    }

    public void Bar(Func<Stream> func)
    {
    }
}";

            await this.VerifyHappyPathAsync(testCode)
                      .ConfigureAwait(false);
        }
    }
}