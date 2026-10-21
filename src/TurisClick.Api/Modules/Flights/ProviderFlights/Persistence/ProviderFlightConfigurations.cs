using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TurisClick.Api.Modules.Flights.ProviderFlights.Entities;
using TurisClick.Api.Modules.Flights.Services;

namespace TurisClick.Api.Modules.Flights.ProviderFlights.Persistence;

/// <summary>`provider_flights` (migración 0015). Las reglas que la base puede sostener sola, las sostiene.</summary>
public class ProviderFlightConfiguration : IEntityTypeConfiguration<ProviderFlight>
{
    public void Configure(EntityTypeBuilder<ProviderFlight> builder)
    {
        builder.ToTable("provider_flights", t =>
        {
            t.HasCheckConstraint("ck_provider_flights_route", "origin_iata <> destination_iata");
            t.HasCheckConstraint("ck_provider_flights_iata",
                "origin_iata ~ '^[A-Z]{3}$' AND destination_iata ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_provider_flights_bags", "checked_bags BETWEEN 0 AND 3 AND carry_on_bags BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_provider_flights_cancellation_policy",
                $"cancellation_policy IS NULL OR cancellation_policy ~ '{Reservations.Policies.CancellationPolicy.ColumnRegex}'");
        });

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(f => f.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(f => f.OriginIata).HasColumnName("origin_iata").HasMaxLength(3).IsRequired();
        builder.Property(f => f.DestinationIata).HasColumnName("destination_iata").HasMaxLength(3).IsRequired();
        builder.Property(f => f.CarrierName).HasColumnName("carrier_name").HasMaxLength(100).IsRequired();
        builder.Property(f => f.CarrierIata).HasColumnName("carrier_iata").HasMaxLength(2);
        builder.Property(f => f.FlightNumber).HasColumnName("flight_number").HasMaxLength(10).IsRequired();
        builder.Property(f => f.CabinClass)
            .HasColumnName("cabin_class")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(FlightCabinClass.ECONOMY);
        builder.Property(f => f.CheckedBags).HasColumnName("checked_bags").IsRequired();
        builder.Property(f => f.CarryOnBags).HasColumnName("carry_on_bags").IsRequired();
        builder.Property(f => f.CancellationPolicy)
            .HasColumnName("cancellation_policy")
            .HasMaxLength(Reservations.Policies.CancellationPolicy.MaxLength);
        builder.Property(f => f.FareRules).HasColumnName("fare_rules").HasMaxLength(2000);
        builder.Property(f => f.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ProviderFlightStatus.DRAFT);
        builder.Property(f => f.SuspendedAt).HasColumnName("suspended_at");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(f => f.CompanyId).HasDatabaseName("ix_provider_flights_company");
        // La búsqueda filtra por ruta y estado.
        builder.HasIndex(f => new { f.OriginIata, f.DestinationIata, f.Status }).HasDatabaseName("ix_provider_flights_route");

        builder.HasOne(f => f.Company)
            .WithMany()
            .HasForeignKey(f => f.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(f => f.Departures)
            .WithOne(d => d.ProviderFlight)
            .HasForeignKey(d => d.ProviderFlightId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>`provider_flight_departures`: el cupo nunca negativo ni mayor que el total, lo garantiza la base.</summary>
public class ProviderFlightDepartureConfiguration : IEntityTypeConfiguration<ProviderFlightDeparture>
{
    public void Configure(EntityTypeBuilder<ProviderFlightDeparture> builder)
    {
        builder.ToTable("provider_flight_departures", t =>
        {
            t.HasCheckConstraint("ck_provider_flight_departures_seats",
                "total_seats > 0 AND reserved_seats >= 0 AND reserved_seats <= total_seats");
            t.HasCheckConstraint("ck_provider_flight_departures_times", "arrival_at > departure_at");
            t.HasCheckConstraint("ck_provider_flight_departures_prices",
                "adult_price >= 0 AND child_price >= 0 AND infant_price >= 0");
            t.HasCheckConstraint("ck_provider_flight_departures_currency", "currency ~ '^[A-Z]{3}$'");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(d => d.ProviderFlightId).HasColumnName("provider_flight_id").IsRequired();

        // Hora local del aeropuerto, sin huso: timestamp WITHOUT time zone, igual que el resto del módulo.
        builder.Property(d => d.DepartureAt).HasColumnName("departure_at").HasColumnType("timestamp without time zone").IsRequired();
        builder.Property(d => d.ArrivalAt).HasColumnName("arrival_at").HasColumnType("timestamp without time zone").IsRequired();

        builder.Property(d => d.TotalSeats).HasColumnName("total_seats").IsRequired();
        builder.Property(d => d.ReservedSeats).HasColumnName("reserved_seats").IsRequired().HasDefaultValue(0);
        builder.Property(d => d.AdultPrice).HasColumnName("adult_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(d => d.ChildPrice).HasColumnName("child_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(d => d.InfantPrice).HasColumnName("infant_price").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(d => d.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ProviderFlightDepartureStatus.OPEN);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.Ignore(d => d.AvailableSeats);

        builder.HasIndex(d => new { d.ProviderFlightId, d.DepartureAt }).HasDatabaseName("ix_provider_flight_departures_flight_date");
    }
}

/// <summary>`reservation_passengers`: se borran con su ítem; ningún otro lado los referencia.</summary>
public class ReservationPassengerConfiguration : IEntityTypeConfiguration<ReservationPassenger>
{
    public void Configure(EntityTypeBuilder<ReservationPassenger> builder)
    {
        builder.ToTable("reservation_passengers");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.ReservationItemId).HasColumnName("reservation_item_id").IsRequired();
        builder.Property(p => p.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(p => p.GivenName).HasColumnName("given_name").HasMaxLength(60).IsRequired();
        builder.Property(p => p.FamilyName).HasColumnName("family_name").HasMaxLength(60).IsRequired();
        builder.Property(p => p.BornOn).HasColumnName("born_on").IsRequired();
        builder.Property(p => p.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(p => new { p.ReservationItemId, p.SortOrder })
            .IsUnique()
            .HasDatabaseName("ux_reservation_passengers_item_order");

        builder.HasOne(p => p.ReservationItem)
            .WithMany(i => i.Passengers)
            .HasForeignKey(p => p.ReservationItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
