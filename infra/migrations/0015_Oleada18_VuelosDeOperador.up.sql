START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items DROP CONSTRAINT ck_reservation_items_product_shape;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD booking_reference character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD issuance_note character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD issuance_status character varying(20) NOT NULL DEFAULT 'NOT_REQUIRED';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD issued_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD issued_by uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD provider_flight_departure_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD provider_flight_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD ticket_numbers character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE companies ADD can_sell_flights boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE companies ADD flight_seller_authorized_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE companies ADD flight_seller_authorized_by uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE companies ADD flight_seller_registration character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE TABLE provider_flights (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        company_id uuid NOT NULL,
        origin_iata character varying(3) NOT NULL,
        destination_iata character varying(3) NOT NULL,
        carrier_name character varying(100) NOT NULL,
        carrier_iata character varying(2),
        flight_number character varying(10) NOT NULL,
        cabin_class character varying(20) NOT NULL DEFAULT 'ECONOMY',
        checked_bags integer NOT NULL,
        carry_on_bags integer NOT NULL,
        cancellation_policy character varying(60),
        fare_rules character varying(2000),
        status character varying(20) NOT NULL DEFAULT 'DRAFT',
        suspended_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_provider_flights" PRIMARY KEY (id),
        CONSTRAINT ck_provider_flights_bags CHECK (checked_bags BETWEEN 0 AND 3 AND carry_on_bags BETWEEN 0 AND 2),
        CONSTRAINT ck_provider_flights_cancellation_policy CHECK (cancellation_policy IS NULL OR cancellation_policy ~ '^[0-9]{1,3}:[0-9]{1,3}(;[0-9]{1,3}:[0-9]{1,3}){0,5}$'),
        CONSTRAINT ck_provider_flights_iata CHECK (origin_iata ~ '^[A-Z]{3}$' AND destination_iata ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_provider_flights_route CHECK (origin_iata <> destination_iata),
        CONSTRAINT "FK_provider_flights_companies_company_id" FOREIGN KEY (company_id) REFERENCES companies (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE TABLE reservation_passengers (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        reservation_item_id uuid NOT NULL,
        type character varying(10) NOT NULL,
        given_name character varying(60) NOT NULL,
        family_name character varying(60) NOT NULL,
        born_on date NOT NULL,
        sort_order integer NOT NULL,
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_reservation_passengers" PRIMARY KEY (id),
        CONSTRAINT "FK_reservation_passengers_reservation_items_reservation_item_id" FOREIGN KEY (reservation_item_id) REFERENCES reservation_items (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE TABLE provider_flight_departures (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        provider_flight_id uuid NOT NULL,
        departure_at timestamp without time zone NOT NULL,
        arrival_at timestamp without time zone NOT NULL,
        total_seats integer NOT NULL,
        reserved_seats integer NOT NULL DEFAULT 0,
        adult_price numeric(12,2) NOT NULL,
        child_price numeric(12,2) NOT NULL,
        infant_price numeric(12,2) NOT NULL,
        currency character varying(3) NOT NULL,
        status character varying(20) NOT NULL DEFAULT 'OPEN',
        created_at timestamp with time zone NOT NULL DEFAULT (now()),
        updated_at timestamp with time zone NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_provider_flight_departures" PRIMARY KEY (id),
        CONSTRAINT ck_provider_flight_departures_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_provider_flight_departures_prices CHECK (adult_price >= 0 AND child_price >= 0 AND infant_price >= 0),
        CONSTRAINT ck_provider_flight_departures_seats CHECK (total_seats > 0 AND reserved_seats >= 0 AND reserved_seats <= total_seats),
        CONSTRAINT ck_provider_flight_departures_times CHECK (arrival_at > departure_at),
        CONSTRAINT "FK_provider_flight_departures_provider_flights_provider_flight~" FOREIGN KEY (provider_flight_id) REFERENCES provider_flights (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE INDEX ix_reservation_items_issuance ON reservation_items (issuance_status, product_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE INDEX ix_reservation_items_provider_flight_departure ON reservation_items (provider_flight_departure_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE INDEX "IX_reservation_items_provider_flight_id" ON reservation_items (provider_flight_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD CONSTRAINT ck_reservation_items_issuance CHECK (issuance_status = 'NOT_REQUIRED' OR product_type = 'FLIGHT');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD CONSTRAINT ck_reservation_items_issued_tickets CHECK (issuance_status <> 'ISSUED' OR (ticket_numbers IS NOT NULL AND issued_at IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD CONSTRAINT ck_reservation_items_product_shape CHECK ((product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL AND experience_availability_id IS NOT NULL AND package_availability_id IS NULL AND provider_flight_id IS NULL AND provider_flight_departure_id IS NULL) OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL AND package_availability_id IS NOT NULL AND experience_availability_id IS NULL AND provider_flight_id IS NULL AND provider_flight_departure_id IS NULL) OR (product_type = 'FLIGHT' AND provider_flight_id IS NOT NULL AND provider_flight_departure_id IS NOT NULL AND experience_id IS NULL AND package_id IS NULL AND experience_availability_id IS NULL AND package_availability_id IS NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE INDEX ix_provider_flight_departures_flight_date ON provider_flight_departures (provider_flight_id, departure_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE INDEX ix_provider_flights_company ON provider_flights (company_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE INDEX ix_provider_flights_route ON provider_flights (origin_iata, destination_iata, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    CREATE UNIQUE INDEX ux_reservation_passengers_item_order ON reservation_passengers (reservation_item_id, sort_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD CONSTRAINT "FK_reservation_items_provider_flight_departures_provider_fligh~" FOREIGN KEY (provider_flight_departure_id) REFERENCES provider_flight_departures (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    ALTER TABLE reservation_items ADD CONSTRAINT "FK_reservation_items_provider_flights_provider_flight_id" FOREIGN KEY (provider_flight_id) REFERENCES provider_flights (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261021012403_0015_Oleada18_VuelosDeOperador') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261021012403_0015_Oleada18_VuelosDeOperador', '10.0.10');
    END IF;
END $EF$;
COMMIT;

