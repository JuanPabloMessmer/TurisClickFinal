using TurisClick.Api.Infrastructure.Health;
using Xunit;

namespace TurisClick.Api.Tests.Infrastructure;

/// <summary>
/// La huella del esquema se calcula en dos lenguajes: acá (lo que responde la app) y en
/// .github/scripts/schema_preflight.py (lo que espera el pipeline). Si divergen, ningún deploy pasa la
/// verificación. Este vector fijo está repetido en test_schema_preflight.py: los dos lados lo validan.
/// </summary>
public class SchemaFingerprintTests
{
    public const string VectorDatabase = "turisclick_db_v2";
    public static readonly string[] VectorMigrations =
    [
        "20260812013133_0001_Oleada0_Auth",
        "20261007024736_0014_Oleada13_AltaDeOperadoresPorAdmin",
    ];
    public const string VectorFingerprint = "f3e4276d553ede3d14d2f599f0bfbdae90fd83a5e8c89a738869cf86fe0708e5";

    [Fact]
    public void LaHuellaCoincideConElVectorCompartidoConElPipeline() =>
        Assert.Equal(VectorFingerprint, DatabaseSchemaInspector.Fingerprint(VectorDatabase, VectorMigrations));

    [Fact]
    public void LaHuellaCambiaConLaBase() =>
        Assert.NotEqual(VectorFingerprint, DatabaseSchemaInspector.Fingerprint("turisclick_db", VectorMigrations));

    [Fact]
    public void LaHuellaCambiaConElOrdenDeLasMigraciones() =>
        Assert.NotEqual(VectorFingerprint, DatabaseSchemaInspector.Fingerprint(VectorDatabase, VectorMigrations.Reverse()));
}
