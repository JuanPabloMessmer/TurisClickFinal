using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Moq;

namespace TurisClick.Api.Tests.Support;

/// <summary>
/// Un <see cref="DbSet{T}"/> en memoria para los tests que mockean el DbContext.
///
/// Hace falta por una razón concreta: las extensiones async de EF (`FirstOrDefaultAsync`, `AnyAsync`)
/// exigen que el proveedor de consultas sea asíncrono, y un `List.AsQueryable()` pelado no lo es. Esto no
/// reemplaza a los tests de integración —ahí corre PostgreSQL de verdad—; sirve para que un test de
/// validaciones pueda decir "esta tabla está vacía" sin montar una base.
/// </summary>
internal static class FakeDbSet
{
    public static DbSet<T> From<T>(params T[] items) where T : class
    {
        var data = items.AsQueryable();
        var set = new Mock<DbSet<T>>();

        set.As<IQueryable<T>>().Setup(m => m.Provider).Returns(new AsyncQueryProvider(data.Provider));
        set.As<IQueryable<T>>().Setup(m => m.Expression).Returns(data.Expression);
        set.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(data.ElementType);
        set.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(() => data.GetEnumerator());
        set.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(() => new SyncAsyncEnumerator<T>(data.GetEnumerator()));

        return set.Object;
    }

    /// <summary>
    /// Delega todo en el proveedor LINQ-to-objects y envuelve el resultado en una tarea ya completada. El
    /// objetivo es que el código bajo prueba corra tal cual está escrito, no simular latencia.
    /// </summary>
    private sealed class AsyncQueryProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => inner.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new AsyncQueryable<TElement>(inner, expression);

        public object? Execute(Expression expression) => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            // TResult es Task<algo>: se ejecuta sincrónicamente y se envuelve en ese Task<algo>.
            var resultType = typeof(TResult).GetGenericArguments()[0];

            var executed = typeof(IQueryProvider)
                .GetMethods()
                .Single(m => m.Name == nameof(IQueryProvider.Execute) && m.IsGenericMethod)
                .MakeGenericMethod(resultType)
                .Invoke(inner, [expression]);

            return (TResult)typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [executed])!;
        }
    }

    private sealed class AsyncQueryable<T>(IQueryProvider inner, Expression expression) : IQueryable<T>, IAsyncEnumerable<T>
    {
        public Type ElementType => typeof(T);

        public Expression Expression => expression;

        public IQueryProvider Provider => new AsyncQueryProvider(inner);

        public IEnumerator<T> GetEnumerator() => inner.Execute<IEnumerable<T>>(expression).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new SyncAsyncEnumerator<T>(GetEnumerator());
    }

    private sealed class SyncAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());

        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
