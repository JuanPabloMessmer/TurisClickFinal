using Xunit;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Todos los tests de integración comparten esta colección para correr secuencialmente entre sí
/// (nunca en paralelo unos con otros) y reutilizar una única TurisClickApiFactory. Sin esto, xUnit
/// corre cada clase de test en paralelo por defecto, y cada una dispara su propia invocación del
/// entry point de Program.cs — que comparten el Log.Logger estático de Serilog — provocando una
/// condición de carrera real (`Log.CloseAndFlush()` de una instancia interfiriendo con el arranque
/// de otra) que se manifestaba como "The entry point exited without ever building an IHost".
/// </summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<TurisClickApiFactory>
{
    public const string Name = "TurisClick API collection";
}
