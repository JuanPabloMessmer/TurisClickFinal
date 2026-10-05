using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Flights.Entities;

namespace TurisClick.Api.Modules.Flights.Persistence;

/// <summary>
/// Mapeo del dominio de vuelos. Todo es aditivo: ninguna tabla existente cambia de forma, y lo único que
/// toca a `packages` es una columna nueva con default.
/// </summary>
public class PackageFlightRuleConfiguration : IEntityTypeConfiguration<PackageFlightRule>
{
    public void Configure(EntityTypeBuilder<PackageFlightRule> builder)
    {
        builder.ToTable("package_flight_rules", t =>
        {
            // Los códigos IATA se guardan normalizados; la base lo hace cumplir además del Service.
            t.HasCheckConstraint("ck_package_flight_rules_destination", "destination_iata ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_package_flight_rules_origins", "allowed_origin_iatas ~ '^[A-Z]{3}(,[A-Z]{3})*$'");
            // Un desfase de más de una semana respecto del viaje no es una regla: es un error de carga.
            t.HasCheckConstraint("ck_package_flight_rules_offsets",
                "outbound_offset_days BETWEEN -7 AND 7 AND inbound_offset_days BETWEEN -7 AND 7");
        });

        // La PK es el package_id: la regla existe para un paquete y sólo uno.
        builder.HasKey(r => r.PackageId);
        builder.Property(r => r.PackageId).HasColumnName("package_id");

        builder.Property(r => r.DestinationIata).HasColumnName("destination_iata").HasMaxLength(3).IsRequired();
        builder.Property(r => r.AllowedOriginIatas).HasColumnName("allowed_origin_iatas").HasMaxLength(200).IsRequired();

        builder.Property(r => r.CabinClass)
            .HasColumnName("cabin_class")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.OutboundOffsetDays).HasColumnName("outbound_offset_days").IsRequired();
        builder.Property(r => r.InboundOffsetDays).HasColumnName("inbound_offset_days").IsRequired();
        builder.Property(r => r.RoundTrip).HasColumnName("round_trip").IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        // Si el paquete se borra, su regla se va con él: no tiene sentido sin el producto.
        builder.HasOne(r => r.Package)
            .WithOne(p => p.FlightRule)
            .HasForeignKey<PackageFlightRule>(r => r.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FlightQuoteConfiguration : IEntityTypeConfiguration<FlightQuote>
{
    public void Configure(EntityTypeBuilder<FlightQuote> builder)
    {
        builder.ToTable("flight_quotes", t =>
        {
            t.HasCheckConstraint("ck_flight_quotes_currency", "currency ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_flight_quotes_amounts", "total_amount >= 0 AND initial_amount >= 0");
            t.HasCheckConstraint("ck_flight_quotes_travelers", "travelers > 0");
            t.HasCheckConstraint("ck_flight_quotes_inbound", "inbound_date IS NULL OR inbound_date >= outbound_date");
        });

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(q => q.PackageId).HasColumnName("package_id").IsRequired();
        builder.Property(q => q.PackageAvailabilityId).HasColumnName("package_availability_id").IsRequired();
        builder.Property(q => q.TouristId).HasColumnName("tourist_id");

        builder.Property(q => q.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(q => q.ProviderOfferId).HasColumnName("provider_offer_id").HasMaxLength(200).IsRequired();

        builder.Property(q => q.OriginIata).HasColumnName("origin_iata").HasMaxLength(3).IsRequired();
        builder.Property(q => q.DestinationIata).HasColumnName("destination_iata").HasMaxLength(3).IsRequired();

        builder.Property(q => q.OutboundDate).HasColumnName("outbound_date").IsRequired();
        builder.Property(q => q.InboundDate).HasColumnName("inbound_date");

        builder.Property(q => q.Travelers).HasColumnName("travelers").IsRequired();

        builder.Property(q => q.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(q => q.InitialAmount).HasColumnName("initial_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(q => q.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.Property(q => q.ExpiresAt).HasColumnName("expires_at");
        builder.Property(q => q.QuotedAt).HasColumnName("quoted_at").HasDefaultValueSql("now()");
        builder.Property(q => q.RevalidatedAt).HasColumnName("revalidated_at");

        builder.Property(q => q.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(q => q.ItinerarySummary).HasColumnName("itinerary_summary").HasMaxLength(500).IsRequired();

        builder.HasIndex(q => new { q.PackageId, q.QuotedAt }).HasDatabaseName("ix_flight_quotes_package_quoted");
        builder.HasIndex(q => q.TouristId).HasDatabaseName("ix_flight_quotes_tourist_id");

        builder.HasOne(q => q.Package)
            .WithMany()
            .HasForeignKey(q => q.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        // La salida es la referencia de la cotización; si se borra, la cotización deja de tener sentido.
        builder.HasOne(q => q.PackageAvailability)
            .WithMany()
            .HasForeignKey(q => q.PackageAvailabilityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FlightBookingConfiguration : IEntityTypeConfiguration<FlightBooking>
{
    public void Configure(EntityTypeBuilder<FlightBooking> builder)
    {
        builder.ToTable("flight_bookings", t =>
        {
            t.HasCheckConstraint("ck_flight_bookings_currency", "currency ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_flight_bookings_amount", "total_amount >= 0");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(b => b.ReservationId).HasColumnName("reservation_id").IsRequired();
        builder.Property(b => b.FlightQuoteId).HasColumnName("flight_quote_id").IsRequired();

        builder.Property(b => b.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(b => b.ProviderOrderId).HasColumnName("provider_order_id").HasMaxLength(200);
        builder.Property(b => b.BookingReference).HasColumnName("booking_reference").HasMaxLength(20);

        // 40 y no 20: RECONCILIATION_REQUIRED no entra en veinte caracteres, y recortar el nombre del
        // estado para que quepa en una columna sería dejar que el almacenamiento decida el vocabulario del
        // dominio. Ampliar un varchar en Postgres es un cambio de metadatos, sin reescritura de la tabla.
        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(b => b.TotalAmount).HasColumnName("total_amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(b => b.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(b => b.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(b => b.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(b => b.FailedAt).HasColumnName("failed_at");
        builder.Property(b => b.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

        // ---- snapshot inmutable de lo comprado ----
        builder.Property(b => b.OriginIata).HasColumnName("origin_iata").HasMaxLength(3).IsRequired();
        builder.Property(b => b.DestinationIata).HasColumnName("destination_iata").HasMaxLength(3).IsRequired();
        builder.Property(b => b.OutboundDate).HasColumnName("outbound_date").IsRequired();
        builder.Property(b => b.InboundDate).HasColumnName("inbound_date");
        builder.Property(b => b.Travelers).HasColumnName("travelers").IsRequired();

        builder.Property(b => b.CarrierIata).HasColumnName("carrier_iata").HasMaxLength(3);
        builder.Property(b => b.CarrierName).HasColumnName("carrier_name").HasMaxLength(100);

        builder.Property(b => b.OutboundDepartureAt).HasColumnName("outbound_departure_at").HasColumnType("timestamp");
        builder.Property(b => b.OutboundArrivalAt).HasColumnName("outbound_arrival_at").HasColumnType("timestamp");
        builder.Property(b => b.OutboundFlightNumber).HasColumnName("outbound_flight_number").HasMaxLength(10);

        builder.Property(b => b.InboundDepartureAt).HasColumnName("inbound_departure_at").HasColumnType("timestamp");
        builder.Property(b => b.InboundArrivalAt).HasColumnName("inbound_arrival_at").HasColumnType("timestamp");
        builder.Property(b => b.InboundFlightNumber).HasColumnName("inbound_flight_number").HasMaxLength(10);

        builder.Property(b => b.ItinerarySummary).HasColumnName("itinerary_summary").HasMaxLength(500).IsRequired();

        // ---- reconciliación ----
        builder.Property(b => b.ReconciliationAttempts).HasColumnName("reconciliation_attempts").IsRequired();
        builder.Property(b => b.NextReconciliationAt).HasColumnName("next_reconciliation_at");
        builder.Property(b => b.LastReconciliationAt).HasColumnName("last_reconciliation_at");

        // Una reserva tiene a lo sumo un vuelo, y la base lo garantiza: es lo que vuelve segura la
        // doble solicitud concurrente, igual que el índice único sobre ai_itinerary_id.
        builder.HasIndex(b => b.ReservationId).IsUnique().HasDatabaseName("ux_flight_bookings_reservation_id");
        builder.HasIndex(b => b.IdempotencyKey).IsUnique().HasDatabaseName("ux_flight_bookings_idempotency_key");

        // Una cotización se puede reservar UNA vez. Es la idempotencia real del flujo: dos toques del
        // botón, o un reintento del cliente tras un timeout, chocan contra este índice en vez de retener
        // cupo dos veces y comprar dos pasajes.
        builder.HasIndex(b => b.FlightQuoteId).IsUnique().HasDatabaseName("ux_flight_bookings_flight_quote_id");

        // Lo que lee el reconciliador: las pocas filas con un desenlace sin resolver.
        builder.HasIndex(b => new { b.Status, b.NextReconciliationAt })
            .HasDatabaseName("ix_flight_bookings_reconciliation");

        builder.HasOne(b => b.Reservation)
            .WithOne()
            .HasForeignKey<FlightBooking>(b => b.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        // La cotización se conserva: es la evidencia de qué se le mostró a la persona al comprar.
        builder.HasOne(b => b.FlightQuote)
            .WithMany()
            .HasForeignKey(b => b.FlightQuoteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
