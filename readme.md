# Game Platform: Load-Balanced Game Servers with a Consistent Wallet

A .NET 10 system where a load balancer distributes requests across **5 independent game server processes** that share one database. The focus is **financial correctness under concurrency**: no double spending, no lost updates, no duplicate bets, even when the same request reaches two different servers at the same time.

The [requirements](#requirements) are listed at the end, together with a [coverage table](#requirements-coverage) that shows what is implemented and what is still open.

## Architecture

```
Client
  |
  v
Load Balancer (:5000)  -- routing, health checks, timeouts
  |
  +-- Game Server #1 (:6001) --+
  +-- Game Server #2 (:6002)   |
  +-- Game Server #3 (:6003)   +--> Shared SQLite database
  +-- Game Server #4 (:6004)   |    (wallets, bets, ledger, idempotency)
  +-- Game Server #5 (:6005) --+
```

Responsibilities are strictly separated:

| Component | Responsible for |
|---|---|
| **Load Balancer** | Routing, health checks, request distribution. No business logic |
| **Game Server** | Request validation, bet processing, application services |
| **Database** | Persistent state, transactions, concurrency control, idempotency constraints |

Financial correctness never depends on the load balancer or on process-local memory or C# locks.

## Key design decisions

### Ledger as the source of truth
Every change to a wallet is an immutable `LedgerEntry`. The wallet `Balance` is a projection of the ledger, updated in the **same transaction** as the ledger entry. `GET /api/wallets/{id}/balance/verify` recalculates the balance from the ledger and reports whether both match. The initial wallet balance is also written to the ledger, so every wallet can be reconciled.

### Atomic bet transaction
One bet runs as a single database transaction: check idempotency → load and validate the wallet → check the balance → create the bet → create the ledger entry → update the balance → store the idempotency result → commit. If any step fails, everything is rolled back.

### Optimistic concurrency across processes
`Wallet.Version` is an EF Core concurrency token, so the `UPDATE` only succeeds if nobody changed the wallet in between. On a conflict (or SQLite busy/locked), `FinancialTransactionExecutor` rolls back and **retries up to 5 times with increasing backoff**, re-reading the current balance each time. A bet that no longer fits the balance is rejected with `409 Conflict`. Example: two servers each receive an 80 bet against a balance of 100; exactly one succeeds.

### Idempotency enforced by the database
Every bet requires an `Idempotency-Key` header. The result of the operation is stored with the key, and a repeated request returns the original result instead of creating a second bet. **Unique indexes** on the idempotency key and operation id make the database the final authority when two servers race with the same key.

### SQLite for multiple processes
WAL journal mode and a 5-second busy timeout let several processes read and write the same database file safely.

### Load balancer
- Round-robin selection behind an `IServerSelector` interface, so the strategy can be replaced through configuration and DI.
- A background `ServerHealthMonitor` polls `/health/ready` on every server (configurable interval) and routes only to healthy servers; `503` when none is available.
- Configurable request timeout; client cancellation is passed through to the game server.
- Server list in `appsettings.json`: adding a server is a configuration change only.
- Hop-by-hop headers are stripped in both directions.

### Observability
Every request carries an `X-Request-Id` (created if missing), forwarded by the load balancer and added to the logging scope together with the server instance name.

## API

| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/wallets` | Create a wallet with an initial balance |
| `GET` | `/api/wallets/{id}` | Get a wallet |
| `GET` | `/api/wallets/{id}/balance/verify` | Compare the wallet balance with the ledger |
| `POST` | `/api/bets` | Place a bet (`Idempotency-Key` header required) |
| `GET` | `/health/live`, `/health/ready` | Liveness and readiness (database check) |

## Run

Requires the .NET 10 SDK and PowerShell.

```powershell
./start-game-servers.ps1     # 5 game servers on ports 6001-6005
./start-load-balancer.ps1    # load balancer on port 5000
./check-health.ps1           # health of all servers
./test-loadbalancer.ps1      # request distribution through the load balancer
./test-concurrency.ps1       # parallel bets against the same wallet
./stop-all.ps1
```

```
dotnet test
```

## Tech stack

C# · .NET 10 · ASP.NET Core · Entity Framework Core 10 · SQLite (WAL) · Health checks · xUnit · PowerShell

## Requirements coverage

✅ implemented · 🟡 partly implemented

| # | Requirement | Status | Implementation |
|---|---|---|---|
| 1–2 | Topology: load balancer, 5+ game servers, shared database | ✅ | Separate processes; `start-game-servers.ps1` starts 5 instances |
| 3 | Load balancer | ✅ | Round-robin, health-based routing, timeout, cancellation, configurable server list. Strategy is an interface; round-robin is the only one so far |
| 4 | Game server | ✅ | Independent ASP.NET Core instances on configurable ports, same API, JSON validation |
| 5 | Database | ✅ | One SQLite database (WAL), EF Core |
| 6–8 | Bets, wallets, ledger | ✅ | Entities with unique indexes; ledger is append-only |
| 9 | Balance projection | ✅ | Balance and ledger updated in one transaction; `balance/verify` endpoint |
| 10 | Atomicity | ✅ | Whole bet in one transaction, rolled back on any error |
| 11–12 | Concurrency, optimistic concurrency | ✅ | `Version` concurrency token; automatic retry (5 attempts), then `409 Conflict` |
| 13 | Transaction isolation | 🟡 | Explicit `ReadCommitted` plus version check; concurrency verified by script, not yet by automated tests |
| 14 | No process-local locks | ✅ | Financial consistency is enforced only by the database |
| 15 | Idempotency | 🟡 | Repeated requests return the original result; unique indexes prevent duplicates. Two simultaneous requests with the same key: the second should also return the original result instead of an error |
| 16 | Failure handling | 🟡 | Timeouts, cancellation, unhealthy servers and idempotent client retries are handled; retry to another server is planned |
| 17 | Health checks | ✅ | `/health/live`, `/health/ready` with a database check |
| 18 | Configuration | ✅ | Servers and timeouts in `appsettings.json` |
| 19 | Logging and observability | ✅ | `X-Request-Id` across both tiers, server name, status, duration, conflicts and retries |
| 20 | Automated tests | 🟡 | Ledger/balance consistency test; the other scenarios are covered by PowerShell scripts and are being moved to xUnit |
| 21 | Separation of responsibilities | ✅ | No business logic in the load balancer |

## Requirements

<details>
<summary>Full system requirements (click to expand)</summary>

#### 1. Overview

The system consists of three logical components:

- Load Balancer
- Multiple Game Server instances
- One shared database

The system must support running multiple Game Server instances concurrently. Each Game Server instance represents an independent process and listens on a different HTTP port.

The Load Balancer acts as a lightweight request broker. It receives incoming requests and forwards them to available Game Server instances.

All Game Server instances share the same database.

The initial deployment must support at least 5 Game Server instances running concurrently.

#### 2. System Topology

The expected architecture is:

```
Client
   |
   v
Load Balancer
   |
   +---- Game Server #1
   |
   +---- Game Server #2
   |
   +---- Game Server #3
   |
   +---- Game Server #4
   |
   +---- Game Server #5
                |
                v
           Shared Database
```

The Load Balancer and Game Server instances are separate application processes.

There is a single shared database used by all Game Server instances.

Game Server instances must not maintain separate copies of wallet or bet state.

#### 3. Load Balancer

The Load Balancer must:

- Accept incoming HTTP requests.
- Maintain a configurable list of Game Server instances.
- Forward requests to available Game Server instances.
- Support at least 5 Game Server instances running simultaneously.
- Support parallel request processing.
- Distribute requests between available Game Server instances.
- Detect unavailable or unhealthy instances.
- Avoid routing new requests to unhealthy instances.
- Support configurable request timeouts.
- Support request cancellation.
- Support a configurable request-distribution strategy.

The initial distribution strategy should be round-robin.

The Load Balancer must not contain game, wallet, bet, or financial business logic.

#### 4. Game Server

Each Game Server must:

- Run as an independent .NET 10 application instance.
- Listen on a configurable HTTP port.
- Expose the same HTTP API as every other Game Server instance.
- Accept JSON requests.
- Validate and deserialize incoming requests.
- Process game operations.
- Use Entity Framework Core for persistence.
- Connect to the shared database.
- Support concurrent request processing.
- Be safe when multiple Game Server instances process requests simultaneously.

Game Server instances must not rely on process-local memory or C# locks for financial consistency.

#### 5. Database

The system must use one shared database instance.

All Game Server instances must connect to the same database.

The initial database provider is SQLite.

Entity Framework Core must be used for database access.

The database must contain the data required to support:

- Bets
- Wallets
- Ledger entries
- Idempotency

The database is the authoritative source for financial state.

#### 6. Bets

A Bet represents a game betting operation.

A Bet must contain at least:

- Id
- Amount
- Wallet/Player reference
- Operation/Request identifier
- CreatedAt
- Status

The exact status values must be defined by the business requirements.

A bet must not be created more than once for the same idempotent operation.

#### 7. Wallets

A Wallet represents a player's financial account.

A Wallet must contain at least:

- Id
- Balance
- Type
- Concurrency/version information

The wallet balance represents a balance projection.

Wallet state must be protected against concurrent modifications from different Game Server instances.

#### 8. Ledger

All financial changes to a Wallet must be represented by ledger entries.

A ledger entry must contain at least:

- Id
- WalletId
- Amount
- Transaction type
- Reference to the related business operation
- Idempotency key
- CreatedAt

Ledger entries are immutable after creation.

Ledger entries must not be updated or deleted as part of normal business operations.

The ledger is the authoritative history of financial changes.

Every change to a wallet balance must have a corresponding ledger entry.

#### 9. Balance Projection

The Wallet balance is a projection derived from the ledger.

For example:

```
Deposit       +100
Bet             -20
Win             +40
Bet             -10
-------------------
Balance         110
```

The system must maintain the Wallet balance consistently with the ledger.

The ledger entry and the corresponding Wallet balance update must occur within the same database transaction.

The system must provide a way to verify or recalculate the wallet balance from the ledger.

#### 10. Atomicity

Financial operations must be atomic.

For a bet operation, the following operations must be part of the same database transaction:

- Validate the wallet.
- Validate sufficient balance.
- Check idempotency.
- Create the Bet.
- Create the Ledger entry.
- Update the Wallet balance projection.
- Store the operation result/idempotency information.
- Commit the transaction.

If any operation fails, the entire transaction must be rolled back.

A partially completed financial operation must never be visible.

#### 11. Concurrency

The system must support concurrent requests targeting the same wallet.

For example, if the wallet has a balance of 100 and two Game Server instances simultaneously receive bets of 80:

```
Game Server #1 -> Bet 80
Game Server #2 -> Bet 80
```

the system must ensure that both requests cannot successfully spend the same 100 balance.

The final database state must remain financially consistent.

The system must prevent:

- Lost updates.
- Double spending.
- Invalid negative balances.
- Duplicate bets.
- Duplicate ledger entries.
- Inconsistent balance projections.

Concurrency correctness must work across different Game Server processes.

#### 12. Optimistic Concurrency

Wallets must support optimistic concurrency control.

A version/concurrency token must be used to detect when a Wallet has been modified by another transaction.

If two Game Server instances attempt to modify the same Wallet concurrently, the database/application must detect the conflict.

The system must define whether a concurrency conflict should:

- Be retried automatically, or
- Return a conflict response to the client.

Concurrency conflicts must never result in silent data loss.

#### 13. Transaction Isolation

The system must explicitly define the transaction isolation level used for financial operations.

The selected isolation level must prevent invalid concurrent wallet operations.

Database-level transaction and concurrency mechanisms must be used instead of relying on process-local application locks.

The selected strategy must be documented and covered by automated concurrency tests.

#### 14. Locking

Financial consistency must not depend on a C# lock or other process-local synchronization mechanism.

Because multiple Game Server processes can access the same database simultaneously, synchronization must be enforced by the shared database.

The locking strategy must be compatible with the selected database provider.

#### 15. Idempotency

All financial operations must be idempotent.

Each operation must contain a unique idempotency key.

If the same operation is submitted multiple times using the same idempotency key:

- Only one financial transaction may be created.
- Only one Bet may be created.
- Only one Ledger entry may be created.
- The Wallet may only be modified once.

Subsequent requests should return the result of the original operation.

The database must enforce uniqueness of the idempotency key.

Idempotency must work even when duplicate requests are received by different Game Server instances.

For example:

```
Request A -> Load Balancer -> Game Server #1
Request A -> Load Balancer -> Game Server #3
```

Both requests must result in only one financial operation.

#### 16. Failure Handling

The system must define behavior for:

- Game Server process failure.
- Load Balancer connection failure.
- Database failure.
- Database transaction failure.
- Request timeout.
- Client disconnect.
- Client retry.
- Game Server failure after database commit but before the response is returned.
- Duplicate requests sent to different Game Server instances.

A failure must not cause a financial operation to be applied twice.

#### 17. Health Checks

Each Game Server must expose a health/readiness endpoint.

The Load Balancer must use the endpoint to determine whether a Game Server is available to receive requests.

An unhealthy Game Server must not receive new requests.

#### 18. Configuration

The Load Balancer must have a configurable list of Game Server instances.

For example:

```
GameServer1 = http://localhost:6001
GameServer2 = http://localhost:6002
GameServer3 = http://localhost:6003
GameServer4 = http://localhost:6004
GameServer5 = http://localhost:6005
```

Adding another Game Server should require configuration rather than changes to business logic.

All Game Server instances must use the same database connection configuration.

#### 19. Logging and Observability

Every request must have a unique correlation/request identifier.

The identifier must be available in logs produced by the Load Balancer and Game Server.

The system should log:

- Request ID.
- Game Server instance.
- Request type.
- Processing result.
- Processing duration.
- Errors.
- Database/concurrency conflicts.
- Idempotency conflicts.
- Retries.

Sensitive financial information must not be unnecessarily written to logs.

#### 20. Testing

The system must include automated tests for:

- Normal bet processing.
- Insufficient balance.
- Concurrent bets against the same wallet.
- Concurrent operations against different wallets.
- Duplicate requests.
- Same idempotency key received by different Game Server instances.
- Transaction rollback.
- Optimistic concurrency conflicts.
- Ledger/balance consistency.
- Load Balancer request distribution.
- Unhealthy Game Server handling.
- Game Server failure.
- Request timeout.
- Retry after timeout.
- Running at least 5 Game Server instances simultaneously.

Concurrency tests must verify the final database state, not only HTTP responses.

#### 21. Core Architectural Principles

The following responsibilities must remain separated:

- **Load Balancer**: routing, health checks, availability, request distribution.
- **Game Server**: request validation, game/business logic, application services.
- **Database**: persistent state, transactions, concurrency control, financial consistency, idempotency constraints.

Financial correctness must not depend on the Load Balancer or process-local memory.

</details>
