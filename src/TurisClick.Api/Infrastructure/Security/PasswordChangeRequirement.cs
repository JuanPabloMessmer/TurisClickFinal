using Microsoft.AspNetCore.Authorization;

namespace TurisClick.Api.Infrastructure.Security;

public static class ClaimNames
{
    /// <summary>Marca que la cuenta sigue usando la contraseña temporal con la que la creó un administrador.</summary>
    public const string MustChangePassword = "must_change_password";
}

/// <summary>
/// Mientras una cuenta arrastre su contraseña temporal, no puede operar.
///
/// Vive en la autorización y no en el frontend a propósito: si la puerta estuviera sólo en la pantalla, una
/// credencial temporal filtrada —que es exactamente la que circula por fuera del sistema, en un mensaje o un
/// papel— alcanzaría para publicar productos o leer reservas. Acá el token mismo lo dice y la API lo rechaza.
///
/// Lo único que queda abierto es cambiar la contraseña y leer el propio perfil, porque sin eso la persona no
/// tendría forma de salir del estado.
/// </summary>
public class PasswordChangeNotPendingRequirement : IAuthorizationRequirement;

public class PasswordChangeNotPendingHandler : AuthorizationHandler<PasswordChangeNotPendingRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PasswordChangeNotPendingRequirement requirement)
    {
        var pending = context.User.FindFirst(ClaimNames.MustChangePassword)?.Value == "true";

        if (pending)
            context.Fail(new AuthorizationFailureReason(
                this, "La cuenta tiene que cambiar su contraseña temporal antes de operar."));
        else
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
