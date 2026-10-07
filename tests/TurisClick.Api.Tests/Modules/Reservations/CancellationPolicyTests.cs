using TurisClick.Api.Modules.Reservations.Policies;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Reservations;

/// <summary>
/// La política de cancelación como objeto de valor. Se prueba acá y no por HTTP porque es aritmética y
/// reglas: lo que importa es que el porcentaje que sale sea el que el operador quiso decir, y que una
/// política contradictoria no se pueda guardar.
/// </summary>
public class CancellationPolicyTests
{
    private static CancellationPolicy Standard() =>
        CancellationPolicy.Parse("30:100;15:50;0:0");

    [Fact]
    public void SeSerializaYSeVuelveALeerIgual()
    {
        var policy = Standard();

        Assert.Equal("30:100;15:50;0:0", policy.Serialize());
        Assert.Equal(policy.Ordered(), CancellationPolicy.Parse(policy.Serialize()).Ordered());
    }

    [Theory]
    [InlineData(60, 100)]
    [InlineData(30, 100)]  // el límite del tramo entra en el tramo
    [InlineData(29, 50)]
    [InlineData(15, 50)]
    [InlineData(14, 0)]
    [InlineData(0, 0)]
    public void ElPorcentajeSaleDelTramoQueCorresponde(int daysBefore, int expected)
    {
        Assert.Equal(expected, Standard().ResolvePercentage(daysBefore));
    }

    [Fact]
    public void SiNingunTramoAplicaNoSeDevuelveNada()
    {
        // Una política que arranca en 15 días no dice nada sobre cancelar la noche anterior. En esa ausencia
        // el default es 0: inventarle un reembolso al operador sería inventarle una obligación.
        var policy = CancellationPolicy.Parse("15:50");

        Assert.Equal(50, policy.ResolvePercentage(20));
        Assert.Equal(0, policy.ResolvePercentage(3));
    }

    [Fact]
    public void UnaPoliticaValidaNoTieneErrores()
    {
        Assert.Empty(Standard().Validate());
    }

    [Fact]
    public void DosTramosParaElMismoDiaEsUnaContradiccion()
    {
        var policy = new CancellationPolicy([new CancellationTier(30, 100), new CancellationTier(30, 50)]);

        Assert.Contains(policy.Validate(), error => error.Contains("misma cantidad de días"));
    }

    [Fact]
    public void UnTramoMasCercanoNoPuedeDevolverMasQueUnoMasLejano()
    {
        // Devolver más cuanto más cerca de la salida siempre es un error de carga, nunca una política real.
        var policy = new CancellationPolicy([new CancellationTier(30, 50), new CancellationTier(10, 100)]);

        Assert.Contains(policy.Validate(), error => error.Contains("no puede devolver más"));
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(400, 50)]
    [InlineData(30, 101)]
    [InlineData(30, -5)]
    public void LosValoresFueraDeRangoSeRechazan(int days, int percentage)
    {
        var policy = new CancellationPolicy([new CancellationTier(days, percentage)]);

        Assert.NotEmpty(policy.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("treinta:100")]
    [InlineData("30")]
    [InlineData("30:100;")]
    [InlineData(null)]
    public void UnTextoQueNoEsUnaPoliticaNoSeParsea(string? value)
    {
        // "30:100;" sí parsea (el separador final se ignora), así que se comprueba aparte más abajo.
        if (value == "30:100;")
        {
            Assert.True(CancellationPolicy.TryParse(value, out _));
            return;
        }

        Assert.False(CancellationPolicy.TryParse(value, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void MasDeSeisTramosNoSeAdmite()
    {
        var tiers = Enumerable.Range(0, 7).Select(i => new CancellationTier(i * 10, 100 - i * 10)).ToList();

        Assert.Contains(new CancellationPolicy(tiers).Validate(), error => error.Contains("más de 6 tramos"));
    }

    [Fact]
    public void SinPoliticaNoEsUnError()
    {
        // Ausencia de política es una decisión válida del operador: significa "no se cancela desde la app".
        Assert.Empty(CancellationPolicyRules.Validate(null));
        Assert.Empty(CancellationPolicyRules.Validate([]));
        Assert.Null(CancellationPolicyRules.Serialize([]));
    }

    [Fact]
    public void LosTramosSeDevuelvenDeMayorAMenorAnticipacion()
    {
        var tiers = CancellationPolicyRules.Deserialize("0:0;30:100;15:50");

        Assert.Equal([30, 15, 0], tiers.Select(t => t.MinDaysBefore));
        Assert.Equal([100, 50, 0], tiers.Select(t => t.RefundPercentage));
    }
}
