using TurisClick.Api.Modules.Flights.Dtos;

namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// Decide si una oferta del metabuscador se puede abrir en el sitio del vendedor (Oleada 19). La app abre
/// **sólo** lo que esto devuelve como disponible, así que acá están todas las reglas:
/// <list type="bullet">
/// <item>Una oferta que no es de venta real (simulada o de sandbox) nunca redirige: no hay compra que hacer.</item>
/// <item>El enlace lo da el proveedor; TurisClick no arma ni completa URLs.</item>
/// <item>Sólo <c>https</c>, sin usuario ni contraseña en la URL, puerto por defecto, host por nombre (no IP) y
/// el host **idéntico** a uno autorizado para ese proveedor: <c>aerolinea.com.otro.net</c> o
/// <c>pagos.aerolinea.com</c> no pasan si no están en la lista.</item>
/// </list>
/// </summary>
public static class FlightRedirectPolicy
{
    public const int MaxUrlLength = 2048;

    public static FlightRedirectResponse Evaluate(FlightOffer offer, bool simulated, IReadOnlyCollection<string> allowedHosts)
    {
        var seller = string.IsNullOrWhiteSpace(offer.SellerName) ? offer.OwnerName : offer.SellerName.Trim();

        if (simulated || !offer.LiveMode)
            return Unavailable(seller, FlightRedirectReasons.Demo);
        if (string.IsNullOrWhiteSpace(offer.BookingUrl))
            return Unavailable(seller, FlightRedirectReasons.NoLink);

        return TryNormalize(offer.BookingUrl, allowedHosts) is { } url
            ? new FlightRedirectResponse { Available = true, Url = url, SellerName = seller }
            : Unavailable(seller, FlightRedirectReasons.LinkNotAllowed);
    }

    /// <summary>La URL normalizada si cumple todas las reglas; null si no.</summary>
    public static string? TryNormalize(string raw, IReadOnlyCollection<string> allowedHosts)
    {
        if (raw.Length > MaxUrlLength) return null;
        // Espacios, controles y barras invertidas: los navegadores los "corrigen" de formas distintas.
        if (raw.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')) return null;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;

        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (!uri.IsDefaultPort) return null;
        if (uri.HostNameType != UriHostNameType.Dns) return null;

        var host = uri.IdnHost.ToLowerInvariant();
        var allowed = allowedHosts.Any(h => string.Equals(h.Trim().ToLowerInvariant(), host, StringComparison.Ordinal));
        return allowed ? uri.AbsoluteUri : null;
    }

    private static FlightRedirectResponse Unavailable(string? seller, string reason) =>
        new() { Available = false, SellerName = seller, UnavailableReason = reason };
}
