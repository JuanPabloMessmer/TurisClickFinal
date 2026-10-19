namespace TurisClick.Api.Modules.Destinations.Dtos;

/// <summary>
/// URL pública HTTPS de la imagen recién subida. El Backoffice la manda después como ImageUrl en el alta o la
/// edición del destino: la subida no modifica ningún destino por sí sola.
/// </summary>
public class UploadedImageResponse
{
    public string Url { get; set; } = string.Empty;
}
