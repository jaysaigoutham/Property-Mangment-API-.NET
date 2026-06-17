# Property Marketplace Backend

Production-style .NET 10 microservices backend for a property listing marketplace.

## Services

- `Gateway.Api`: YARP edge gateway for all public routes.
- `Identity.Api`: registration, login, refresh tokens, profiles, and roles.
- `Listings.Api`: listing CRUD, owner checks, public search/filtering, approval workflow, and Redis read caching.
- `Media.Api`: MinIO/S3-compatible image upload URLs and image metadata.
- `Engagement.Api`: favorites, saved searches, inquiries, reviews, and agent profiles.
- `Notifications.Api`: Kafka consumer that records notification logs.
- `Admin.Api`: secured admin orchestration for users, listings, reviews, notifications, and service status.
- `BuildingBlocks`: shared JWT, Kafka, outbox, Redis, PostgreSQL, logging, health, and service defaults.

## Infrastructure

`docker-compose.yml` starts:

- PostgreSQL for service-owned databases.
- Redis for caching.
- Apache Kafka KRaft for domain events.
- MinIO for S3-compatible property images.
- One container per .NET service.

Kafka topics are created by the `kafka-init` service:

`user.registered`, `listing.created`, `listing.updated`, `listing.approved`, `listing.rejected`, `inquiry.created`, `review.created`, `favorite.created`, `notification.requested`.

## Run

```powershell
copy .env.example .env
docker compose up --build
```

The gateway listens on `http://localhost:8080`.

Direct service ports:

- Identity: `http://localhost:8081`
- Listings: `http://localhost:8082`
- Media: `http://localhost:8083`
- Engagement: `http://localhost:8084`
- Notifications: `http://localhost:8085`
- Admin: `http://localhost:8086`
- MinIO console: `http://localhost:9001`

## Useful Requests

Register an admin:

```http
POST http://localhost:8080/auth/register
Content-Type: application/json

{
  "email": "admin@example.com",
  "password": "Password123!",
  "displayName": "Admin",
  "role": "admin"
}
```

Register an agent:

```http
POST http://localhost:8080/auth/register
Content-Type: application/json

{
  "email": "agent@example.com",
  "password": "Password123!",
  "displayName": "Agent One",
  "role": "agent"
}
```

Create a listing with an agent/admin bearer token:

```http
POST http://localhost:8080/listings
Authorization: Bearer <token>
Content-Type: application/json

{
  "title": "Modern apartment near transit",
  "description": "Bright three bedroom apartment with parking.",
  "city": "Colombo",
  "state": "Western",
  "country": "Sri Lanka",
  "addressLine": "Main Street",
  "price": 250000,
  "bedrooms": 3,
  "bathrooms": 2,
  "areaSqm": 120,
  "propertyType": "Apartment",
  "amenities": ["parking", "pool"]
}
```

Approve a listing with an admin token:

```http
POST http://localhost:8080/admin/listings/<listing-id>/approve
Authorization: Bearer <admin-token>
```

## Build and Test

```powershell
dotnet build PropertyMarketplace.slnx
dotnet test PropertyMarketplace.slnx
```

The default integration tests validate the infrastructure contract without starting Docker containers. The Testcontainers packages are installed so deeper container-backed tests can be added around the same PostgreSQL, Redis, Kafka, and MinIO dependencies.

## Notes

- Local startup uses `EnsureCreated` by default to make the scaffold easy to run. Set `Database:UseMigrations=true` after generating EF Core migrations for production migration workflows.
- The default JWT signing key is for development only. Use `.env` or secret management for real deployments.
- Search is implemented with PostgreSQL indexed filtering and text matching for v1. OpenSearch can be added later if richer ranking or geo-search becomes necessary.
