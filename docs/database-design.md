# TurisClick — FASE 3: Diseño de base de datos (PostgreSQL)

Fuente de verdad: [`docs/domain-model.md`](./domain-model.md). Este documento traduce cada entidad del dominio a una tabla física de PostgreSQL. Es diseño únicamente — no se ha creado ningún proyecto ni migración todavía (eso es FASE 5).

## Convenciones físicas

- **Motor:** PostgreSQL 15+.
- **Nombres:** tablas en `snake_case` plural, columnas en `snake_case`. Enums de dominio se mapean a **tipos `ENUM` nativos de PostgreSQL** (bien soportados por Npgsql/EF Core).
- **Clave primaria:** `uuid` con `DEFAULT gen_random_uuid()` en todas las tablas (built-in desde PostgreSQL 13, sin requerir `pgcrypto`). Se prefiere sobre `bigint identity` para no exponer IDs secuenciales enumerables en una API pública.
- **Auditoría:** `created_at timestamptz NOT NULL DEFAULT now()`; las tablas editables agregan `updated_at timestamptz NOT NULL DEFAULT now()` (se actualiza desde la capa de Service, no con trigger, para mantener la lógica en el backend).
- **Dinero:** `numeric(12,2)` para todo precio/subtotal.
- **Moneda:** `char(3) NOT NULL`, con `CHECK (currency ~ '^[A-Z]{3}$')` a nivel de formato. La lista definitiva de códigos permitidos (ISO 4217) se valida en la capa de Service (constante en código), **no** como tabla de referencia — evita introducir una entidad `Currency`/`ExchangeRate` que el dominio decidió no modelar todavía.
- **Invariantes cruzados no expresables en `CHECK`** (ej. "la `Experience` referenciada por un `PackageItem` debe pertenecer a la misma `Company` que el `Package`) se documentan igual junto a la tabla, pero se aplican en la capa de Service — PostgreSQL no puede comparar columnas de otra tabla dentro de un `CHECK` sin un trigger, y no se justifica agregar triggers para esto en el alcance actual.
- **Borrado:** `ON DELETE CASCADE` únicamente en relaciones de composición fuerte (ej. `experience_images` no tiene sentido sin su `Experience`). En relaciones de referencia (ej. `reservation_items.experience_id`) se usa `ON DELETE RESTRICT` (o simplemente sin cascada) porque un producto no debería poder borrarse si tiene reservas históricas — de hecho, el dominio ya resuelve esto con `SUSPENDED`/`UNPUBLISHED` en vez de borrado físico.

## Nota de implementación: dependencia circular `users` ↔ `companies`

`users.company_id → companies.id` y `companies.approved_by_user_id → users.id` se referencian mutuamente. En la migración (FASE 5), esto se resuelve creando ambas tablas sin una de las dos FKs inline y agregándola después con `ALTER TABLE`:

```sql
-- 1) crear companies sin la FK a users
-- 2) crear users (ya puede referenciar companies)
ALTER TABLE companies
  ADD CONSTRAINT fk_companies_approved_by_user
  FOREIGN KEY (approved_by_user_id) REFERENCES users(id);
```

---

## Tipos ENUM

```sql
CREATE TYPE user_role AS ENUM ('TOURIST', 'PROVIDER', 'ADMIN');
CREATE TYPE user_status AS ENUM ('ACTIVE', 'SUSPENDED');
CREATE TYPE company_status AS ENUM ('PENDING_APPROVAL', 'APPROVED', 'REJECTED', 'SUSPENDED');
CREATE TYPE destination_type AS ENUM ('COUNTRY', 'REGION', 'CITY');
CREATE TYPE publication_status AS ENUM ('DRAFT', 'PUBLISHED', 'UNPUBLISHED', 'SUSPENDED');
CREATE TYPE package_item_kind AS ENUM ('EXPERIENCE_REFERENCE', 'DESCRIPTIVE');
CREATE TYPE availability_slot_status AS ENUM ('OPEN', 'CLOSED');
CREATE TYPE reservation_status AS ENUM ('PENDING_PAYMENT', 'CONFIRMED', 'PAYMENT_FAILED', 'CANCELLED', 'EXPIRED');
CREATE TYPE reservation_item_status AS ENUM ('PENDING_PAYMENT', 'CONFIRMED', 'CANCELLED');
CREATE TYPE product_type AS ENUM ('EXPERIENCE', 'PACKAGE');
CREATE TYPE ai_conversation_status AS ENUM ('ACTIVE', 'CLOSED');
CREATE TYPE message_sender AS ENUM ('TOURIST', 'AI');
CREATE TYPE ai_itinerary_status AS ENUM ('DRAFT', 'SAVED', 'BOOKED', 'DISCARDED');
```

---

## 1. Autenticación / Usuarios

```sql
CREATE TABLE companies (
  id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name                 varchar(150) NOT NULL,
  description          text,
  legal_document       varchar(50) NOT NULL,
  contact_email        varchar(255) NOT NULL,
  contact_phone        varchar(30),
  status               company_status NOT NULL DEFAULT 'PENDING_APPROVAL',
  approved_by_user_id  uuid,               -- FK a users.id, agregada después (ver nota circular)
  approved_at          timestamptz,
  rejection_reason     text,
  created_at           timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_companies_legal_document UNIQUE (legal_document)
);

CREATE TABLE users (
  id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  full_name      varchar(150) NOT NULL,
  email          varchar(255) NOT NULL,
  password_hash  varchar(255) NOT NULL,
  role           user_role NOT NULL,
  status         user_status NOT NULL DEFAULT 'ACTIVE',
  company_id     uuid REFERENCES companies(id),
  created_at     timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_users_email UNIQUE (email),
  CONSTRAINT ck_users_provider_has_company
    CHECK ( (role = 'PROVIDER' AND company_id IS NOT NULL)
         OR (role <> 'PROVIDER' AND company_id IS NULL) )
);

ALTER TABLE companies
  ADD CONSTRAINT fk_companies_approved_by_user
  FOREIGN KEY (approved_by_user_id) REFERENCES users(id);

CREATE INDEX ix_users_company_id ON users(company_id);
CREATE INDEX ix_companies_status ON companies(status);

CREATE TABLE refresh_tokens (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  token_hash  varchar(255) NOT NULL,
  expires_at  timestamptz NOT NULL,
  revoked_at  timestamptz,
  created_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT uq_refresh_tokens_token_hash UNIQUE (token_hash)
);

CREATE INDEX ix_refresh_tokens_user_id ON refresh_tokens(user_id);
```

---

## 2. Catálogo turístico

```sql
CREATE TABLE destinations (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name        varchar(150) NOT NULL,
  type        destination_type NOT NULL,
  parent_id   uuid REFERENCES destinations(id),
  created_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_destinations_country_no_parent
    CHECK ( (type = 'COUNTRY' AND parent_id IS NULL)
         OR (type <> 'COUNTRY' AND parent_id IS NOT NULL) ),
  CONSTRAINT uq_destinations_name_parent_type UNIQUE (name, parent_id, type)
);

CREATE INDEX ix_destinations_parent_id ON destinations(parent_id);
CREATE INDEX ix_destinations_type ON destinations(type);

CREATE TABLE categories (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name         varchar(100) NOT NULL,
  description  text,
  CONSTRAINT uq_categories_name UNIQUE (name)
);
```

---

## 3. Experiencias

```sql
CREATE TABLE experiences (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id        uuid NOT NULL REFERENCES companies(id),
  destination_id    uuid NOT NULL REFERENCES destinations(id),
  title             varchar(200) NOT NULL,
  description       text NOT NULL,
  includes_text     text,
  excludes_text     text,
  duration_minutes  int,
  duration_label    varchar(100),
  price             numeric(12,2) NOT NULL,
  currency          char(3) NOT NULL,
  status            publication_status NOT NULL DEFAULT 'DRAFT',
  created_at        timestamptz NOT NULL DEFAULT now(),
  updated_at        timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_experiences_price CHECK (price >= 0),
  CONSTRAINT ck_experiences_duration CHECK (duration_minutes IS NULL OR duration_minutes > 0),
  CONSTRAINT ck_experiences_currency CHECK (currency ~ '^[A-Z]{3}$')
);

CREATE INDEX ix_experiences_company_id ON experiences(company_id);
CREATE INDEX ix_experiences_destination_id ON experiences(destination_id);
CREATE INDEX ix_experiences_status_destination ON experiences(status, destination_id);

CREATE TABLE experience_images (
  id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  experience_id  uuid NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
  url            varchar(500) NOT NULL,
  sort_order     int NOT NULL DEFAULT 0,
  is_cover       boolean NOT NULL DEFAULT false
);

CREATE INDEX ix_experience_images_experience_id ON experience_images(experience_id);
CREATE UNIQUE INDEX ux_experience_images_one_cover
  ON experience_images(experience_id) WHERE is_cover = true;

CREATE TABLE experience_categories (
  experience_id  uuid NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
  category_id    uuid NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
  PRIMARY KEY (experience_id, category_id)
);

CREATE INDEX ix_experience_categories_category_id ON experience_categories(category_id);

CREATE TABLE experience_availabilities (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  experience_id   uuid NOT NULL REFERENCES experiences(id) ON DELETE CASCADE,
  date            date NOT NULL,
  start_time      time,                 -- NULL = disponibilidad de día completo
  total_slots     int NOT NULL,
  reserved_slots  int NOT NULL DEFAULT 0,
  status          availability_slot_status NOT NULL DEFAULT 'OPEN',
  CONSTRAINT ck_experience_availabilities_slots
    CHECK (total_slots > 0 AND reserved_slots >= 0 AND reserved_slots <= total_slots)
);

CREATE INDEX ix_experience_availabilities_experience_date
  ON experience_availabilities(experience_id, date);
CREATE UNIQUE INDEX ux_experience_availabilities_timed
  ON experience_availabilities(experience_id, date, start_time) WHERE start_time IS NOT NULL;
CREATE UNIQUE INDEX ux_experience_availabilities_fullday
  ON experience_availabilities(experience_id, date) WHERE start_time IS NULL;
```

---

## 4. Paquetes

```sql
CREATE TABLE packages (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id       uuid NOT NULL REFERENCES companies(id),
  destination_id   uuid NOT NULL REFERENCES destinations(id),
  title            varchar(200) NOT NULL,
  description      text NOT NULL,
  conditions_text  text,
  duration_days    int NOT NULL,
  price            numeric(12,2) NOT NULL,
  currency         char(3) NOT NULL,
  status           publication_status NOT NULL DEFAULT 'DRAFT',
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_packages_duration CHECK (duration_days > 0),
  CONSTRAINT ck_packages_price CHECK (price >= 0),
  CONSTRAINT ck_packages_currency CHECK (currency ~ '^[A-Z]{3}$')
);

CREATE INDEX ix_packages_company_id ON packages(company_id);
CREATE INDEX ix_packages_destination_id ON packages(destination_id);
CREATE INDEX ix_packages_status_destination ON packages(status, destination_id);

CREATE TABLE package_items (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  package_id   uuid NOT NULL REFERENCES packages(id) ON DELETE CASCADE,
  day_number   int NOT NULL,
  sort_order   int NOT NULL DEFAULT 0,
  kind         package_item_kind NOT NULL,
  experience_id  uuid REFERENCES experiences(id),
  title        varchar(200),
  description  text,
  CONSTRAINT ck_package_items_day CHECK (day_number >= 1),
  CONSTRAINT ck_package_items_kind_shape
    CHECK ( (kind = 'EXPERIENCE_REFERENCE' AND experience_id IS NOT NULL)
         OR (kind = 'DESCRIPTIVE' AND experience_id IS NULL) ),
  CONSTRAINT ck_package_items_descriptive_title
    CHECK (kind <> 'DESCRIPTIVE' OR title IS NOT NULL)
);
-- Invariante NO expresable aquí: experience_id.company_id debe ser igual a package_id.company_id.
-- Se valida en PackageService al crear/editar un PackageItem (UC-P-07/08).

CREATE INDEX ix_package_items_package_id ON package_items(package_id);
CREATE INDEX ix_package_items_experience_id ON package_items(experience_id);

CREATE TABLE package_images (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  package_id  uuid NOT NULL REFERENCES packages(id) ON DELETE CASCADE,
  url         varchar(500) NOT NULL,
  sort_order  int NOT NULL DEFAULT 0,
  is_cover    boolean NOT NULL DEFAULT false
);

CREATE INDEX ix_package_images_package_id ON package_images(package_id);
CREATE UNIQUE INDEX ux_package_images_one_cover
  ON package_images(package_id) WHERE is_cover = true;

CREATE TABLE package_categories (
  package_id   uuid NOT NULL REFERENCES packages(id) ON DELETE CASCADE,
  category_id  uuid NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
  PRIMARY KEY (package_id, category_id)
);

CREATE INDEX ix_package_categories_category_id ON package_categories(category_id);

CREATE TABLE package_availabilities (
  id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  package_id      uuid NOT NULL REFERENCES packages(id) ON DELETE CASCADE,
  departure_date  date NOT NULL,
  total_slots     int NOT NULL,
  reserved_slots  int NOT NULL DEFAULT 0,
  status          availability_slot_status NOT NULL DEFAULT 'OPEN',
  CONSTRAINT ck_package_availabilities_slots
    CHECK (total_slots > 0 AND reserved_slots >= 0 AND reserved_slots <= total_slots),
  CONSTRAINT uq_package_availabilities_departure UNIQUE (package_id, departure_date)
);

CREATE INDEX ix_package_availabilities_package_date
  ON package_availabilities(package_id, departure_date);
```

---

## 5. Inteligencia artificial

> Se crea antes que `reservations` porque `reservations.ai_itinerary_id` la referencia.

```sql
CREATE TABLE ai_conversations (
  id                        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tourist_id                uuid NOT NULL REFERENCES users(id),
  status                    ai_conversation_status NOT NULL DEFAULT 'ACTIVE',
  preferred_destination_id  uuid REFERENCES destinations(id),
  start_date                date,
  end_date                  date,
  travelers_count           int,
  budget_total              numeric(12,2),
  budget_currency           char(3),
  duration_days             int,
  restrictions_notes        text,
  created_at                timestamptz NOT NULL DEFAULT now(),
  updated_at                timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_ai_conversations_dates CHECK (end_date IS NULL OR start_date IS NULL OR end_date >= start_date),
  CONSTRAINT ck_ai_conversations_travelers CHECK (travelers_count IS NULL OR travelers_count > 0),
  CONSTRAINT ck_ai_conversations_budget CHECK (budget_total IS NULL OR budget_total >= 0),
  CONSTRAINT ck_ai_conversations_currency CHECK (budget_currency IS NULL OR budget_currency ~ '^[A-Z]{3}$')
);

CREATE INDEX ix_ai_conversations_tourist_id ON ai_conversations(tourist_id);

CREATE TABLE ai_conversation_categories (
  ai_conversation_id  uuid NOT NULL REFERENCES ai_conversations(id) ON DELETE CASCADE,
  category_id         uuid NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
  PRIMARY KEY (ai_conversation_id, category_id)
);

CREATE TABLE ai_messages (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  ai_conversation_id  uuid NOT NULL REFERENCES ai_conversations(id) ON DELETE CASCADE,
  sender              message_sender NOT NULL,
  content             text NOT NULL,
  created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_ai_messages_conversation_created
  ON ai_messages(ai_conversation_id, created_at);

CREATE TABLE ai_itineraries (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  ai_conversation_id  uuid NOT NULL REFERENCES ai_conversations(id) ON DELETE CASCADE,
  tourist_id          uuid NOT NULL REFERENCES users(id),
  title               varchar(200),
  status              ai_itinerary_status NOT NULL DEFAULT 'DRAFT',
  version             int NOT NULL DEFAULT 1,
  created_at          timestamptz NOT NULL DEFAULT now(),
  updated_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_ai_itineraries_tourist_id ON ai_itineraries(tourist_id);
CREATE INDEX ix_ai_itineraries_conversation_id ON ai_itineraries(ai_conversation_id);
CREATE INDEX ix_ai_itineraries_status ON ai_itineraries(status);

CREATE TABLE ai_itinerary_items (
  id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  ai_itinerary_id             uuid NOT NULL REFERENCES ai_itineraries(id) ON DELETE CASCADE,
  day_number                  int NOT NULL,
  sort_order                  int NOT NULL DEFAULT 0,
  product_type                product_type NOT NULL,
  experience_id               uuid REFERENCES experiences(id),
  package_id                  uuid REFERENCES packages(id),
  experience_availability_id  uuid REFERENCES experience_availabilities(id),
  package_availability_id     uuid REFERENCES package_availabilities(id),
  estimated_unit_price        numeric(12,2) NOT NULL,
  currency                    char(3) NOT NULL,
  CONSTRAINT ck_ai_itinerary_items_day CHECK (day_number >= 1),
  CONSTRAINT ck_ai_itinerary_items_product_shape
    CHECK ( (product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL
             AND package_availability_id IS NULL)
         OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL
             AND experience_availability_id IS NULL) ),
  CONSTRAINT ck_ai_itinerary_items_currency CHECK (currency ~ '^[A-Z]{3}$')
);

CREATE INDEX ix_ai_itinerary_items_itinerary_id ON ai_itinerary_items(ai_itinerary_id);
```

---

## 6. Reservas

```sql
CREATE TABLE reservations (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tourist_id       uuid NOT NULL REFERENCES users(id),
  ai_itinerary_id  uuid REFERENCES ai_itineraries(id),
  status           reservation_status NOT NULL DEFAULT 'PENDING_PAYMENT',
  expires_at       timestamptz,
  created_at       timestamptz NOT NULL DEFAULT now(),
  confirmed_at     timestamptz,
  cancelled_at     timestamptz
);

CREATE INDEX ix_reservations_tourist_id ON reservations(tourist_id);
CREATE INDEX ix_reservations_status ON reservations(status);
CREATE INDEX ix_reservations_ai_itinerary_id ON reservations(ai_itinerary_id);

CREATE TABLE reservation_items (
  id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  reservation_id               uuid NOT NULL REFERENCES reservations(id) ON DELETE CASCADE,
  company_id                  uuid NOT NULL REFERENCES companies(id),
  product_type                product_type NOT NULL,
  experience_id                uuid REFERENCES experiences(id),
  package_id                   uuid REFERENCES packages(id),
  experience_availability_id   uuid REFERENCES experience_availabilities(id),
  package_availability_id      uuid REFERENCES package_availabilities(id),
  travelers                   int NOT NULL,
  unit_price                  numeric(12,2) NOT NULL,
  currency                    char(3) NOT NULL,
  subtotal                    numeric(12,2) NOT NULL,
  status                      reservation_item_status NOT NULL DEFAULT 'PENDING_PAYMENT',
  day_number                  int,
  created_at                  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_reservation_items_travelers CHECK (travelers > 0),
  CONSTRAINT ck_reservation_items_amounts CHECK (unit_price >= 0 AND subtotal >= 0),
  CONSTRAINT ck_reservation_items_currency CHECK (currency ~ '^[A-Z]{3}$'),
  CONSTRAINT ck_reservation_items_product_shape
    CHECK ( (product_type = 'EXPERIENCE' AND experience_id IS NOT NULL AND package_id IS NULL
             AND experience_availability_id IS NOT NULL AND package_availability_id IS NULL)
         OR (product_type = 'PACKAGE' AND package_id IS NOT NULL AND experience_id IS NULL
             AND package_availability_id IS NOT NULL AND experience_availability_id IS NULL) )
);

CREATE INDEX ix_reservation_items_reservation_id ON reservation_items(reservation_id);
CREATE INDEX ix_reservation_items_company_status ON reservation_items(company_id, status);
CREATE INDEX ix_reservation_items_experience_availability ON reservation_items(experience_availability_id);
CREATE INDEX ix_reservation_items_package_availability ON reservation_items(package_availability_id);
```

---

## Resumen de invariantes que NO están en el esquema físico (van en Service)

| Invariante | Por qué no es un `CHECK`/FK |
|---|---|
| `PackageItem.experience_id.company_id == PackageItem.package_id.company_id` | Requiere comparar una columna de otra fila/tabla — Postgres no lo permite en `CHECK` sin trigger. |
| `AiItineraryItem`/`ReservationItem`: el `ExperienceAvailability`/`PackageAvailability` referenciado debe pertenecer al mismo `Experience`/`Package` que el ítem | Misma razón — comparación cruzada de FKs. |
| `Reservation` "parcialmente cancelada" | Deliberadamente no persistido (decisión del usuario en FASE 2); se calcula agregando `reservation_items.status` por `reservation_id`. |
| Total de una `Reservation`/`AiItinerary` (posible multi-moneda) | Deliberadamente no persistido; se calcula agregando `subtotal` por `currency`. |
| Cierre de aprobación: solo un `ADMIN` puede escribir `companies.status` | Autorización, no integridad de datos — se aplica en el `Controller`/`Service` vía el rol del JWT. |

---

## Código DBML (dbdiagram.io)

Pega este bloque completo en [dbdiagram.io](https://dbdiagram.io) para visualizar el ERD.

```dbml
Enum user_role {
  TOURIST
  PROVIDER
  ADMIN
}

Enum user_status {
  ACTIVE
  SUSPENDED
}

Enum company_status {
  PENDING_APPROVAL
  APPROVED
  REJECTED
  SUSPENDED
}

Enum destination_type {
  COUNTRY
  REGION
  CITY
}

Enum publication_status {
  DRAFT
  PUBLISHED
  UNPUBLISHED
  SUSPENDED
}

Enum package_item_kind {
  EXPERIENCE_REFERENCE
  DESCRIPTIVE
}

Enum availability_slot_status {
  OPEN
  CLOSED
}

Enum reservation_status {
  PENDING_PAYMENT
  CONFIRMED
  PAYMENT_FAILED
  CANCELLED
  EXPIRED
}

Enum reservation_item_status {
  PENDING_PAYMENT
  CONFIRMED
  CANCELLED
}

Enum product_type {
  EXPERIENCE
  PACKAGE
}

Enum ai_conversation_status {
  ACTIVE
  CLOSED
}

Enum message_sender {
  TOURIST
  AI
}

Enum ai_itinerary_status {
  DRAFT
  SAVED
  BOOKED
  DISCARDED
}

Table companies {
  id uuid [pk]
  name varchar(150) [not null]
  description text
  legal_document varchar(50) [not null, unique]
  contact_email varchar(255) [not null]
  contact_phone varchar(30)
  status company_status [not null, default: 'PENDING_APPROVAL']
  approved_by_user_id uuid
  approved_at timestamptz
  rejection_reason text
  created_at timestamptz [not null]
}

Table users {
  id uuid [pk]
  full_name varchar(150) [not null]
  email varchar(255) [not null, unique]
  password_hash varchar(255) [not null]
  role user_role [not null]
  status user_status [not null, default: 'ACTIVE']
  company_id uuid
  created_at timestamptz [not null]
}

Table refresh_tokens {
  id uuid [pk]
  user_id uuid [not null]
  token_hash varchar(255) [not null, unique]
  expires_at timestamptz [not null]
  revoked_at timestamptz
  created_at timestamptz [not null]
}

Table destinations {
  id uuid [pk]
  name varchar(150) [not null]
  type destination_type [not null]
  parent_id uuid
  created_at timestamptz [not null]
}

Table categories {
  id uuid [pk]
  name varchar(100) [not null, unique]
  description text
}

Table experiences {
  id uuid [pk]
  company_id uuid [not null]
  destination_id uuid [not null]
  title varchar(200) [not null]
  description text [not null]
  includes_text text
  excludes_text text
  duration_minutes int
  duration_label varchar(100)
  price "numeric(12,2)" [not null]
  currency char(3) [not null]
  status publication_status [not null, default: 'DRAFT']
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
}

Table experience_images {
  id uuid [pk]
  experience_id uuid [not null]
  url varchar(500) [not null]
  sort_order int [not null, default: 0]
  is_cover boolean [not null, default: false]
}

Table experience_categories {
  experience_id uuid [not null]
  category_id uuid [not null]
  Indexes {
    (experience_id, category_id) [pk]
  }
}

Table experience_availabilities {
  id uuid [pk]
  experience_id uuid [not null]
  date date [not null]
  start_time time
  total_slots int [not null]
  reserved_slots int [not null, default: 0]
  status availability_slot_status [not null, default: 'OPEN']
}

Table packages {
  id uuid [pk]
  company_id uuid [not null]
  destination_id uuid [not null]
  title varchar(200) [not null]
  description text [not null]
  conditions_text text
  duration_days int [not null]
  price "numeric(12,2)" [not null]
  currency char(3) [not null]
  status publication_status [not null, default: 'DRAFT']
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
}

Table package_items {
  id uuid [pk]
  package_id uuid [not null]
  day_number int [not null]
  sort_order int [not null, default: 0]
  kind package_item_kind [not null]
  experience_id uuid
  title varchar(200)
  description text
}

Table package_images {
  id uuid [pk]
  package_id uuid [not null]
  url varchar(500) [not null]
  sort_order int [not null, default: 0]
  is_cover boolean [not null, default: false]
}

Table package_categories {
  package_id uuid [not null]
  category_id uuid [not null]
  Indexes {
    (package_id, category_id) [pk]
  }
}

Table package_availabilities {
  id uuid [pk]
  package_id uuid [not null]
  departure_date date [not null]
  total_slots int [not null]
  reserved_slots int [not null, default: 0]
  status availability_slot_status [not null, default: 'OPEN']
}

Table ai_conversations {
  id uuid [pk]
  tourist_id uuid [not null]
  status ai_conversation_status [not null, default: 'ACTIVE']
  preferred_destination_id uuid
  start_date date
  end_date date
  travelers_count int
  budget_total "numeric(12,2)"
  budget_currency char(3)
  duration_days int
  restrictions_notes text
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
}

Table ai_conversation_categories {
  ai_conversation_id uuid [not null]
  category_id uuid [not null]
  Indexes {
    (ai_conversation_id, category_id) [pk]
  }
}

Table ai_messages {
  id uuid [pk]
  ai_conversation_id uuid [not null]
  sender message_sender [not null]
  content text [not null]
  created_at timestamptz [not null]
}

Table ai_itineraries {
  id uuid [pk]
  ai_conversation_id uuid [not null]
  tourist_id uuid [not null]
  title varchar(200)
  status ai_itinerary_status [not null, default: 'DRAFT']
  version int [not null, default: 1]
  created_at timestamptz [not null]
  updated_at timestamptz [not null]
}

Table ai_itinerary_items {
  id uuid [pk]
  ai_itinerary_id uuid [not null]
  day_number int [not null]
  sort_order int [not null, default: 0]
  product_type product_type [not null]
  experience_id uuid
  package_id uuid
  experience_availability_id uuid
  package_availability_id uuid
  estimated_unit_price "numeric(12,2)" [not null]
  currency char(3) [not null]
}

Table reservations {
  id uuid [pk]
  tourist_id uuid [not null]
  ai_itinerary_id uuid
  status reservation_status [not null, default: 'PENDING_PAYMENT']
  expires_at timestamptz
  created_at timestamptz [not null]
  confirmed_at timestamptz
  cancelled_at timestamptz
}

Table reservation_items {
  id uuid [pk]
  reservation_id uuid [not null]
  company_id uuid [not null]
  product_type product_type [not null]
  experience_id uuid
  package_id uuid
  experience_availability_id uuid
  package_availability_id uuid
  travelers int [not null]
  unit_price "numeric(12,2)" [not null]
  currency char(3) [not null]
  subtotal "numeric(12,2)" [not null]
  status reservation_item_status [not null, default: 'PENDING_PAYMENT']
  day_number int
  created_at timestamptz [not null]
}

Ref: users.company_id > companies.id
Ref: companies.approved_by_user_id > users.id
Ref: refresh_tokens.user_id > users.id
Ref: destinations.parent_id > destinations.id
Ref: experiences.company_id > companies.id
Ref: experiences.destination_id > destinations.id
Ref: experience_images.experience_id > experiences.id
Ref: experience_categories.experience_id > experiences.id
Ref: experience_categories.category_id > categories.id
Ref: experience_availabilities.experience_id > experiences.id
Ref: packages.company_id > companies.id
Ref: packages.destination_id > destinations.id
Ref: package_items.package_id > packages.id
Ref: package_items.experience_id > experiences.id
Ref: package_images.package_id > packages.id
Ref: package_categories.package_id > packages.id
Ref: package_categories.category_id > categories.id
Ref: package_availabilities.package_id > packages.id
Ref: ai_conversations.tourist_id > users.id
Ref: ai_conversations.preferred_destination_id > destinations.id
Ref: ai_conversation_categories.ai_conversation_id > ai_conversations.id
Ref: ai_conversation_categories.category_id > categories.id
Ref: ai_messages.ai_conversation_id > ai_conversations.id
Ref: ai_itineraries.ai_conversation_id > ai_conversations.id
Ref: ai_itineraries.tourist_id > users.id
Ref: ai_itinerary_items.ai_itinerary_id > ai_itineraries.id
Ref: ai_itinerary_items.experience_id > experiences.id
Ref: ai_itinerary_items.package_id > packages.id
Ref: ai_itinerary_items.experience_availability_id > experience_availabilities.id
Ref: ai_itinerary_items.package_availability_id > package_availabilities.id
Ref: reservations.tourist_id > users.id
Ref: reservations.ai_itinerary_id > ai_itineraries.id
Ref: reservation_items.reservation_id > reservations.id
Ref: reservation_items.company_id > companies.id
Ref: reservation_items.experience_id > experiences.id
Ref: reservation_items.package_id > packages.id
Ref: reservation_items.experience_availability_id > experience_availabilities.id
Ref: reservation_items.package_availability_id > package_availabilities.id
```

---

## Orden de creación de tablas (respeta dependencias de FK)

1. `companies` (sin FK a `users` todavía)
2. `users`
3. `ALTER TABLE companies ADD CONSTRAINT ... approved_by_user_id`
4. `refresh_tokens`
5. `destinations`, `categories`
6. `experiences`, `experience_images`, `experience_categories`, `experience_availabilities`
7. `packages`, `package_items`, `package_images`, `package_categories`, `package_availabilities`
8. `ai_conversations`, `ai_conversation_categories`, `ai_messages`, `ai_itineraries`, `ai_itinerary_items`
9. `reservations`, `reservation_items`

Este orden coincide con las oleadas de implementación de `use-cases.md`, así que cada migración de FASE 5 solo agrega las tablas que su oleada necesita — no hace falta crear el esquema completo de una vez.

---

Diseño de base de datos cerrado. Quedo en pausa para tu revisión antes de pasar a **FASE 4 — Arquitectura del backend**.
