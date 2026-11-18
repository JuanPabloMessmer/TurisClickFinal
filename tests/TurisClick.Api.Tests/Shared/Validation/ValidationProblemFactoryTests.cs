using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using TurisClick.Api.Shared.Validation;
using Xunit;

namespace TurisClick.Api.Tests.Shared.Validation;

/// <summary>R-14 — la respuesta 400 de validación sale en español y con las claves del formulario.</summary>
public class ValidationProblemFactoryTests
{
    private static ValidationProblemDetails Problem(ModelStateDictionary state)
    {
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), state);
        var result = Assert.IsType<BadRequestObjectResult>(ValidationProblemFactory.Create(context));
        return Assert.IsType<ValidationProblemDetails>(result.Value);
    }

    [Fact]
    public void ElTituloEsEnEspanolYLleveElCodigoDeError()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("LegalDocument", "El NIT sólo puede tener números.");

        var problem = Problem(state);

        Assert.Equal("Hay datos para corregir.", problem.Title);
        Assert.Equal(400, problem.Status);
        Assert.Equal("VALIDATION_FAILED", problem.Extensions["errorCode"]);
        Assert.Equal(["El NIT sólo puede tener números."], problem.Errors["legalDocument"]);
    }

    [Theory]
    [InlineData("CompanyName", "companyName")]
    [InlineData("$.contactPhone", "contactPhone")]
    [InlineData("Items[0].Title", "items[0].title")]
    [InlineData("request", "")]
    [InlineData("$", "")]
    public void LasClavesQuedanComoEnElFormulario(string key, string expected) =>
        Assert.Equal(expected, ValidationProblemFactory.ToFieldKey(key));

    [Fact]
    public void LosMensajesPorDefectoEnInglesSeReemplazan()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("Price", "The field Price must be between 0 and 1.79769313486232E+308.");
        state.AddModelError("Title", "The Title field is required.");
        state.AddModelError("$.destinationId", "The JSON value could not be converted to System.Guid. Path: $.destinationId | LineNumber: 0 | BytePositionInLine: 42.");

        var problem = Problem(state);

        Assert.Equal([ValidationProblemFactory.InvalidValueMessage], problem.Errors["price"]);
        Assert.Equal([ValidationProblemFactory.RequiredMessage], problem.Errors["title"]);
        Assert.Equal([ValidationProblemFactory.InvalidValueMessage], problem.Errors["destinationId"]);
        Assert.DoesNotContain(problem.Errors.Values.SelectMany(v => v), m => m.Contains("The ") || m.Contains("System."));
    }

    [Fact]
    public void SiHayUnMensajePropioSeDescartaElGenerico()
    {
        // Las propiedades no anulables tienen un [Required] implícito: su mensaje en inglés no debe aparecer
        // junto al de la regla propia que ya explicó el problema.
        var state = new ModelStateDictionary();
        state.AddModelError("CompanyName", "The CompanyName field is required.");
        state.AddModelError("CompanyName", "El nombre de la empresa es obligatorio.");

        var problem = Problem(state);

        Assert.Equal(["El nombre de la empresa es obligatorio."], problem.Errors["companyName"]);
    }
}
