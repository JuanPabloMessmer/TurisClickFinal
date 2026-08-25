using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Modules.Reservations.Services;

namespace TurisClick.Api.Modules.Reservations;

public static class ReservationsModuleExtensions
{
    public static IServiceCollection AddReservationsModule(this IServiceCollection services)
    {
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IReservationItemRepository, ReservationItemRepository>();
        services.AddScoped<IReservationService, ReservationService>();

        // Placeholder hasta integrar una pasarela real — ver IPaymentGateway.
        services.AddScoped<IPaymentGateway, SimulatedPaymentGateway>();

        return services;
    }
}
