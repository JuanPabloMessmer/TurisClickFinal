using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Modules.Reservations.Services;

namespace TurisClick.Api.Modules.Reservations;

public static class ReservationsModuleExtensions
{
    public static IServiceCollection AddReservationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IReservationItemRepository, ReservationItemRepository>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IReservationBookingService, ReservationBookingService>();
        services.AddScoped<IReservationExpirationService, ReservationExpirationService>();
        services.AddScoped<IReservationCancellationService, ReservationCancellationService>();
        services.AddScoped<IPaymentLedger, PaymentLedger>();
        services.AddScoped<IAdminPaymentsService, AdminPaymentsService>();
        services.Configure<CancellationResolutionOptions>(
            configuration.GetSection(CancellationResolutionOptions.SectionName));
        services.AddHostedService<CancellationResolutionBackgroundService>();
        services.Configure<ReservationExpirationOptions>(configuration.GetSection(ReservationExpirationOptions.SectionName));
        services.AddHostedService<ReservationExpirationBackgroundService>();

        // Placeholder hasta integrar una pasarela real — ver IPaymentGateway.
        services.AddScoped<IPaymentGateway, SimulatedPaymentGateway>();

        return services;
    }
}
