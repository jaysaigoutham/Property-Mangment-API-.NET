# AGENTS.md

Guidance for AI coding agents and contributors working in this repository.

## Project Summary

This is a .NET 10 microservices backend for a property marketplace. The system uses:

- ASP.NET Core Minimal APIs
- YARP API Gateway
- PostgreSQL service-owned databases
- Redis caching
- Apache Kafka KRaft for domain events
- Outbox pattern for reliable event publishing
- MinIO for S3-compatible image storage
- JWT bearer authentication
- Role-based authorization for buyer, agent, and admin users

Normal client traffic should go through:

```text
http://localhost:8080
```

Direct service ports are for debugging only.

## Repository Layout

```text
src/
  Admin.Api/
  BuildingBlocks/
  Engagement.Api/
  Gateway.Api/
  Identity.Api/
  Listings.Api/
  Media.Api/
  Notifications.Api/
  Payments.Api/
tests/
  PropertyMarketplace.UnitTests/
  PropertyMarketplace.IntegrationTests/
docker/
  postgres/init/
```

## Architecture Standards

- Keep services separated by business capability.
- Do not make one service write directly to another service's database.
- Use HTTP through service APIs for synchronous cross-service checks.
- Use Kafka events for asynchronous domain communication.
- Use the outbox pattern when publishing service domain events.
- Add shared infrastructure only in `BuildingBlocks` when it is useful across multiple services.
- Prefer Gateway routes for external/public access.
- Keep Admin APIs backend-only unless a user explicitly asks for an admin UI.

## Service Standards

Each API service should generally follow the existing pattern:

- Minimal API endpoints in `Program.cs`.
- Domain models, request/response records, mapping, validation, and `DbContext` grouped in local model files when the service is small.
- `AddMarketplaceServiceDefaults(...)` for common logging, health, OpenAPI, auth, CORS, rate limiting, Redis cache, and JSON enum behavior.
- `AddPostgresDb<TContext>(...)` for PostgreSQL.
- `AddKafkaOutbox<TContext>(...)` when the service publishes Kafka events.
- `app.UseMarketplaceServiceDefaults()` in the request pipeline.
- `public partial class Program;` for test hosting compatibility.

## Auth And Roles

Use existing role names from `BuildingBlocks.Auth.AppRoles`:

- `buyer`
- `agent`
- `admin`

Use the existing authorization policies:

- `BuyerOrAgent`
- `AgentOrAdmin`
- `AdminOnly`

Protected APIs must require JWT bearer tokens:

```http
Authorization: Bearer <token>
```

Do not introduce cookie auth unless explicitly requested.

## Database Standards

- PostgreSQL is the system database engine.
- Each service owns a separate database:
  - `property_identity`
  - `property_listings`
  - `property_media`
  - `property_engagement`
  - `property_notifications`
  - `property_payments`
- Local startup currently uses `EnsureCreated` by default.
- Production migration work should use EF Core migrations, but do not add migrations unless requested.
- Keep database schema ownership inside the service that owns the data.

## Kafka And Events

Kafka topic names live in:

```text
src/BuildingBlocks/Events/KafkaTopics.cs
```

When adding a new event:

1. Add a topic constant.
2. Add it to `KafkaTopics.All`.
3. Add topic creation to `docker-compose.yml` under `kafka-init`.
4. Publish through `OutboxMessage.Create(...)`.
5. Keep event payloads small and stable.

Current event topics include listing, user, engagement, payment, promo-code, notification, and entitlement events.

## Payments Standards

Payments are implemented in `Payments.Api`.

Important rules:

- The server calculates package price, discount, and final payable amount.
- The frontend must never send or decide final payable amount.
- Stripe and PayHere secrets must come from environment/config only.
- Webhooks must validate provider signatures.
- Webhook processing must be idempotent.
- Listing submission requires a completed active listing ad entitlement.

Do not add real payment provider secrets to the repository.

## Caching Standards

- Redis is available through the shared service defaults.
- Listing search/detail caching exists in `Listings.Api`.
- Cache public read-heavy data, not sensitive user secrets.
- Invalidate or expire cache entries after listing changes.

## Media Standards

- Property images use MinIO.
- `Media.Api` generates upload URLs and stores metadata.
- Do not route file bytes through unrelated services.
- Keep image metadata tied to listing IDs.

## API Documentation Standards

- OpenAPI JSON is enabled in development through `AddOpenApi()`.
- Swagger UI is not currently installed.
- The Postman collection is:

```text
PropertyMarketplace.postman_collection.json
```

If adding endpoints, update the Postman collection when requested.

## Docker Standards

Use Docker Compose for local full-system runs:

```powershell
docker compose up --build
```

Stop the system:

```powershell
docker compose down
```

View logs:

```powershell
docker compose logs -f <service-name>
```

When adding a service, update:

- `PropertyMarketplace.slnx`
- `docker-compose.yml`
- `Dockerfile` only if the shared build pattern changes
- Gateway routes if public access is needed
- README if developer workflow changes

## Build And Test Commands

Build:

```powershell
dotnet build PropertyMarketplace.slnx
```

Test:

```powershell
dotnet test PropertyMarketplace.slnx
```

Run these after code changes unless the user explicitly asks to skip validation.

## Coding Style

- Follow existing Minimal API style.
- Keep changes small and scoped to the request.
- Prefer clear request/response records.
- Prefer explicit validation messages for API input.
- Use async EF Core calls with `CancellationToken`.
- Use `AsNoTracking()` for read-only queries.
- Keep names descriptive and consistent with existing services.
- Use JSON string enum behavior already configured in service defaults.
- Do not add new frameworks or infrastructure if an existing local pattern solves the task.

## Documentation Style

- Keep README guidance practical and copy-paste friendly.
- Use ASCII-safe diagrams unless Mermaid is useful.
- Mermaid diagrams are acceptable in Markdown.
- Update docs when adding major services, routes, setup requirements, or infrastructure.

## Do Not Do Without Explicit Request

- Do not add a frontend project to this backend repo.
- Do not add admin UI pages.
- Do not add real secrets or credentials.
- Do not switch Kafka to RabbitMQ, ZooKeeper Kafka, or Redpanda.
- Do not replace PostgreSQL search with OpenSearch.
- Do not introduce payments beyond listing ads unless requested.
- Do not delete Docker volumes or reset Git history unless explicitly asked.
- Do not revert unrelated user changes.

## Useful Files To Inspect First

- `README.md`
- `docker-compose.yml`
- `PropertyMarketplace.slnx`
- `src/BuildingBlocks/ServiceDefaults/ServiceDefaultsExtensions.cs`
- `src/BuildingBlocks/Events/KafkaTopics.cs`
- The target service's `Program.cs`
- The target service's model file

## Local URLs

| Service | URL |
| --- | --- |
| Gateway | `http://localhost:8080` |
| Identity | `http://localhost:8081` |
| Listings | `http://localhost:8082` |
| Media | `http://localhost:8083` |
| Engagement | `http://localhost:8084` |
| Notifications | `http://localhost:8085` |
| Admin | `http://localhost:8086` |
| Payments | `http://localhost:8087` |
| MinIO Console | `http://localhost:9001` |

## Final Response Standard

When finishing work:

- Summarize what changed.
- Mention important files.
- Report build/test results.
- Mention any skipped validation or known follow-up work.
