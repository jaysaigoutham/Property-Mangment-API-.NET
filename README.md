# Property Marketplace Backend

Production-style .NET 10 microservices backend for a property listing marketplace.

This project is designed as a learning-friendly backend that still uses real production patterns: separate services, service-owned PostgreSQL databases, Redis caching, Apache Kafka events, MinIO image storage, JWT authentication, and role-based authorization.

The normal entry point for clients is the API Gateway:

```text
http://localhost:8080
```

Use direct service ports only when debugging a specific service.

## What This Backend Supports

- User registration, login, refresh tokens, and profiles.
- Buyer, agent, and admin roles.
- Property listing CRUD for agents.
- Public listing search and filtering.
- Listing approval workflow for admins.
- Image upload URL generation and media ordering.
- Favorites, saved searches, inquiries, reviews, and agent profiles.
- Paid listing ad checkout with Stripe or PayHere.
- Promo codes and ad packages managed through admin APIs.
- Kafka domain events and notification logging.

There is no frontend in this repository. A frontend prompt is available in `PropertyMarketplaceFrontendPrompt.txt`.

## Architecture

```text
Frontend / Postman
        |
        v
Gateway.Api :8080
        |
        +--> Identity.Api       -> PostgreSQL
        +--> Listings.Api       -> PostgreSQL + Redis + Kafka
        +--> Media.Api          -> PostgreSQL + MinIO
        +--> Payments.Api       -> PostgreSQL + Kafka + Stripe/PayHere
        +--> Engagement.Api     -> PostgreSQL + Kafka
        +--> Notifications.Api  -> PostgreSQL + Kafka consumer
        +--> Admin.Api          -> forwards secured admin calls to other services
```

### Main Patterns

- **API Gateway with YARP**: `Gateway.Api` receives public HTTP calls and routes them to the correct service.
- **Microservices**: each API owns one business area.
- **Service-owned databases**: each service uses its own PostgreSQL database.
- **Redis caching**: used for frequently read listing search/detail data and shared cache configuration.
- **Kafka KRaft**: used for asynchronous domain events without ZooKeeper.
- **Outbox pattern**: services write events to their database first, then a background worker publishes them to Kafka.
- **MinIO**: local S3-compatible storage for property images.
- **JWT authentication**: clients send `Authorization: Bearer <token>` for protected endpoints.
- **Role-based authorization**: buyer, agent, and admin roles control access.

## Services

| Service | Port | Responsibility |
| --- | ---: | --- |
| `Gateway.Api` | 8080 | Public edge gateway for all client traffic. |
| `Identity.Api` | 8081 | Register, login, refresh tokens, profile, and users. |
| `Listings.Api` | 8082 | Listing CRUD, search, owner checks, approval workflow, Redis caching. |
| `Media.Api` | 8083 | MinIO upload URLs and listing image metadata. |
| `Engagement.Api` | 8084 | Favorites, saved searches, inquiries, reviews, agent profiles. |
| `Notifications.Api` | 8085 | Kafka consumer that records notification logs. |
| `Admin.Api` | 8086 | Secured admin orchestration and proxy APIs. |
| `Payments.Api` | 8087 | Paid ads, promo codes, Stripe/PayHere checkout, ad entitlements. |

## Repository Structure

```text
.
+-- src/
|   +-- Admin.Api/
|   +-- BuildingBlocks/
|   +-- Engagement.Api/
|   +-- Gateway.Api/
|   +-- Identity.Api/
|   +-- Listings.Api/
|   +-- Media.Api/
|   +-- Notifications.Api/
|   +-- Payments.Api/
+-- tests/
|   +-- PropertyMarketplace.UnitTests/
|   +-- PropertyMarketplace.IntegrationTests/
+-- docker/
|   +-- postgres/init/
+-- Dockerfile
+-- docker-compose.yml
+-- .env.example
+-- PropertyMarketplace.postman_collection.json
+-- PropertyMarketplaceFrontendPrompt.txt
+-- PropertyMarketplace.slnx
+-- README.md
```

### Important Files

- `PropertyMarketplace.slnx`: .NET solution file.
- `docker-compose.yml`: starts PostgreSQL, Redis, Kafka, MinIO, and all API services.
- `Dockerfile`: shared Docker build file for the .NET services.
- `.env.example`: example environment variables.
- `docker/postgres/init/01-create-service-databases.sql`: creates service databases on first PostgreSQL startup.
- `PropertyMarketplace.postman_collection.json`: Postman collection grouped by API area.
- `PropertyMarketplaceFrontendPrompt.txt`: prompt for generating a frontend project.
- `src/BuildingBlocks`: shared JWT, Kafka, outbox, Redis, PostgreSQL, logging, health, and service defaults.

## Prerequisites

Install these before running the project:

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Git
- Postman, Insomnia, Bruno, or another API client

Check .NET:

```powershell
dotnet --version
```

Check Docker:

```powershell
docker --version
docker compose version
```

## First-Time Setup

1. Open the repository folder:

```powershell
cd C:\Users\jayasai\Documents\Codex\2026-06-17\create-a-simple-net-core-backend
```

2. Create your local environment file:

```powershell
copy .env.example .env
```

3. Start all infrastructure and services:

```powershell
docker compose up --build
```

4. Wait until the services are running. Then verify the Gateway:

```http
GET http://localhost:8080/health/ready
```

Expected response:

```json
{
  "status": "ready"
}
```

The response also includes a timestamp.

## Common Commands

Restore dependencies:

```powershell
dotnet restore
```

Build the solution:

```powershell
dotnet build PropertyMarketplace.slnx
```

Run tests:

```powershell
dotnet test PropertyMarketplace.slnx
```

Start Docker environment:

```powershell
docker compose up --build
```

Stop Docker environment:

```powershell
docker compose down
```

View logs for one service:

```powershell
docker compose logs -f listings-api
```

Rebuild only one service:

```powershell
docker compose up --build listings-api
```

## Infrastructure

`docker-compose.yml` starts:

- PostgreSQL 17
- Redis 7
- Apache Kafka 4 in KRaft mode
- MinIO
- One container per .NET API service

### PostgreSQL Databases

The PostgreSQL init script creates these databases:

- `property_identity`
- `property_listings`
- `property_media`
- `property_engagement`
- `property_notifications`
- `property_payments`

Each service owns and writes to its own database. Services should not directly write to another service's database.

### Kafka Topics

Kafka topics are created by the `kafka-init` service:

```text
user.registered
listing.created
listing.updated
listing.approved
listing.rejected
inquiry.created
review.created
favorite.created
notification.requested
payment.checkout.created
payment.completed
payment.failed
promo-code.created
promo-code.updated
promo-code.deleted
ad.entitlement.created
```

### MinIO

MinIO stores uploaded property images.

- API endpoint: `http://localhost:9000`
- Console: `http://localhost:9001`
- Default local username: `minioadmin`
- Default local password: `minioadmin`
- Bucket: `property-images`

## API Access

Use the Gateway for normal API calls:

```text
http://localhost:8080
```

Direct service URLs are useful for debugging:

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

## OpenAPI And Swagger

OpenAPI JSON is enabled in development mode.

Example:

```text
http://localhost:8082/openapi/v1.json
```

Swagger UI pages like `/swagger/index.html` are not currently installed. If you need a visual API explorer, use the Postman collection or add Swagger UI later with a package such as `Swashbuckle.AspNetCore`.

## Postman Collection

Import this file into Postman:

```text
PropertyMarketplace.postman_collection.json
```

The collection includes folders for:

- Gateway / Health
- Auth
- Listings
- Media
- Engagement
- Payments
- Admin
- Notifications

It also includes variables for base URLs, JWT tokens, and common IDs.

## Authentication

Protected endpoints require a JWT bearer token:

```http
Authorization: Bearer <token>
```

The backend supports these roles:

- `buyer`
- `agent`
- `admin`

Role examples:

- Buyers can favorite listings, save searches, send inquiries, and create reviews.
- Agents can create listings, manage media, and pay for listing ads.
- Admins can approve/reject listings, moderate reviews, manage payment packages/promo codes, and inspect activity.

## Beginner API Walkthrough

This walkthrough uses the Gateway URL: `http://localhost:8080`.

### 1. Register An Admin

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

### 2. Register An Agent

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

### 3. Login And Copy The Token

```http
POST http://localhost:8080/auth/login
Content-Type: application/json

{
  "email": "agent@example.com",
  "password": "Password123!"
}
```

Copy `accessToken` from the response. Use it in protected requests:

```http
Authorization: Bearer <agent-token>
```

### 4. Create A Listing As Agent

```http
POST http://localhost:8080/listings
Authorization: Bearer <agent-token>
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

Copy the returned listing `id`.

### 5. Create An Ad Package As Admin

First login as admin, then use the admin token.

```http
POST http://localhost:8080/admin/payments/ad-packages
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "name": "Standard Listing Ad",
  "description": "Publishes one listing for 30 days.",
  "price": 25,
  "currency": "USD",
  "durationDays": 30,
  "isActive": true,
  "displayOrder": 10
}
```

Copy the returned ad package `id`.

### 6. Create A Promo Code As Admin

```http
POST http://localhost:8080/admin/payments/promo-codes
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "code": "WELCOME10",
  "description": "Ten percent off first listing ad.",
  "discountType": "Percent",
  "percentOff": 10,
  "amountOff": null,
  "currency": "USD",
  "maxRedemptions": 100,
  "perUserLimit": 1,
  "validFromUtc": "2026-01-01T00:00:00Z",
  "expiresAtUtc": "2027-01-01T00:00:00Z",
  "autoDeleteAtUtc": "2027-02-01T00:00:00Z",
  "isActive": true
}
```

### 7. Preview Checkout As Agent

```http
POST http://localhost:8080/payments/checkouts/preview
Authorization: Bearer <agent-token>
Content-Type: application/json

{
  "listingId": "<listing-id>",
  "adPackageId": "<ad-package-id>",
  "promoCode": "WELCOME10"
}
```

The backend calculates:

- original amount
- discount amount
- final amount
- currency

The frontend must not calculate the final payable amount.

### 8. Create Checkout As Agent

Stripe:

```http
POST http://localhost:8080/payments/checkouts
Authorization: Bearer <agent-token>
Content-Type: application/json

{
  "listingId": "<listing-id>",
  "adPackageId": "<ad-package-id>",
  "provider": "Stripe",
  "promoCode": "WELCOME10",
  "successUrl": "http://localhost:3000/payment/success",
  "cancelUrl": "http://localhost:3000/payment/cancel"
}
```

PayHere:

```http
POST http://localhost:8080/payments/checkouts
Authorization: Bearer <agent-token>
Content-Type: application/json

{
  "listingId": "<listing-id>",
  "adPackageId": "<ad-package-id>",
  "provider": "PayHere",
  "promoCode": null,
  "successUrl": "http://localhost:3000/payment/success",
  "cancelUrl": "http://localhost:3000/payment/cancel"
}
```

Hosted payment providers require real provider secrets. Without valid secrets, checkout creation can fail with a configuration error.

### 9. Submit Listing For Approval

```http
POST http://localhost:8080/listings/<listing-id>/submit
Authorization: Bearer <agent-token>
```

Important: this endpoint checks for a completed paid listing ad entitlement. If payment is missing, the backend returns a payment-required response and the listing remains in `Draft`.

### 10. Approve Listing As Admin

```http
POST http://localhost:8080/admin/listings/<listing-id>/approve
Authorization: Bearer <admin-token>
```

### 11. Search Public Listings

```http
GET http://localhost:8080/listings?city=Colombo&propertyType=Apartment&page=1&pageSize=20
```

Only approved listings appear in public search.

## Payment Provider Configuration

Payment provider secrets are read from environment/config:

- `STRIPE_SECRET_KEY`
- `STRIPE_WEBHOOK_SECRET`
- `PAYHERE_MERCHANT_ID`
- `PAYHERE_MERCHANT_SECRET`
- `PAYHERE_CHECKOUT_URL`
- `PAYHERE_NOTIFY_URL`

Do not put real secrets in source code.

Hosted checkout flow:

1. Client asks backend for checkout preview.
2. Client creates checkout.
3. Backend calculates final price and creates provider checkout.
4. Client redirects to Stripe or PayHere hosted checkout.
5. Provider sends webhook to backend.
6. Backend verifies webhook and creates ad entitlement.
7. Agent can submit listing for approval.

## Admin APIs

Admin APIs are backend-only. This repository does not include an admin web UI.

Admin routes go through the Gateway:

```text
http://localhost:8080/admin/...
```

Useful admin endpoints:

- `GET /admin/summary`
- `GET /admin/users`
- `POST /admin/listings/{listingId}/approve`
- `POST /admin/listings/{listingId}/reject`
- `GET /admin/reviews/pending`
- `POST /admin/reviews/{reviewId}/publish`
- `POST /admin/reviews/{reviewId}/reject`
- `GET /admin/notifications`
- `GET /admin/payments/ad-packages`
- `POST /admin/payments/ad-packages`
- `PUT /admin/payments/ad-packages/{adPackageId}`
- `DELETE /admin/payments/ad-packages/{adPackageId}`
- `GET /admin/payments/promo-codes`
- `POST /admin/payments/promo-codes`
- `PUT /admin/payments/promo-codes/{promoCodeId}`
- `DELETE /admin/payments/promo-codes/{promoCodeId}`
- `GET /admin/payments/checkouts`

## Building And Testing

Build:

```powershell
dotnet build PropertyMarketplace.slnx
```

Test:

```powershell
dotnet test PropertyMarketplace.slnx
```

The default integration tests validate the infrastructure contract without starting Docker containers. Testcontainers packages are installed so deeper container-backed tests can be added later around PostgreSQL, Redis, Kafka, and MinIO.

## Development Notes

- Local startup uses `EnsureCreated` by default. This keeps local setup simple.
- Production should use EF Core migrations instead of `EnsureCreated`.
- Set `Database:UseMigrations=true` only after proper migrations are generated.
- The default JWT signing key is for development only.
- Use `.env`, environment variables, or secret management for real deployments.
- Search is implemented with PostgreSQL indexed filtering and text matching for v1.
- OpenSearch can be added later if richer search ranking or geo-search is required.
- Payments use hosted checkout. The frontend should never calculate or trust final payable amounts.
- Kafka events are published through the outbox pattern for more reliable async messaging.

## Troubleshooting

### Docker Is Not Running

If `docker compose up --build` fails immediately, check Docker Desktop is open and running.

```powershell
docker version
```

### Ports Are Already In Use

This project uses ports:

```text
5432, 6379, 8080-8087, 9000, 9001, 9092
```

If a port is busy, stop the conflicting process or change the port mapping in `docker-compose.yml`.

### Database Init Script Did Not Run

PostgreSQL init scripts run only when the database volume is first created.

If you changed `docker/postgres/init/01-create-service-databases.sql` after the volume already existed, recreate the volume:

```powershell
docker compose down -v
docker compose up --build
```

Warning: `docker compose down -v` deletes local Docker volumes for this project, including local PostgreSQL and MinIO data.

### 401 Unauthorized

You are missing a token or using an expired token.

Fix:

1. Login again.
2. Copy the new `accessToken`.
3. Send it as:

```http
Authorization: Bearer <token>
```

### 403 Forbidden

Your token is valid, but your role is not allowed.

Examples:

- Buyer trying to create listings.
- Agent trying to access admin APIs.
- Non-admin trying to approve listings.

### Listing Submit Is Blocked

`POST /listings/{id}/submit` requires a completed paid ad entitlement.

Fix:

1. Create or select an ad package.
2. Preview checkout.
3. Complete checkout.
4. Wait for provider webhook to mark payment completed.
5. Submit the listing again.

### Payment Checkout Fails

Check provider environment variables:

- Stripe requires `STRIPE_SECRET_KEY`.
- Stripe webhooks require `STRIPE_WEBHOOK_SECRET`.
- PayHere requires `PAYHERE_MERCHANT_ID` and `PAYHERE_MERCHANT_SECRET`.

Local fake secrets are not enough for real hosted checkout calls.

### Kafka Topics Are Missing

Check Kafka init logs:

```powershell
docker compose logs kafka-init
```

The app also has topic constants in `src/BuildingBlocks/Events/KafkaTopics.cs`.

### MinIO Images Are Not Loading

Check MinIO is running:

```powershell
docker compose logs minio
```

Open the console:

```text
http://localhost:9001
```

Use username/password:

```text
minioadmin / minioadmin
```

## Suggested Learning Path For Junior Developers

1. Start with `Gateway.Api` to understand routing.
2. Read `Identity.Api` to understand registration, login, JWTs, and roles.
3. Read `Listings.Api` to understand CRUD, search, caching, and ownership checks.
4. Read `BuildingBlocks` to understand shared service setup, PostgreSQL, Kafka outbox, and auth policies.
5. Read `Payments.Api` to understand checkout, promo codes, provider abstractions, and webhook processing.
6. Use the Postman collection to call APIs manually.
7. Watch Docker logs while sending requests to see how services behave.

Useful command while learning:

```powershell
docker compose logs -f gateway-api listings-api payments-api
```
