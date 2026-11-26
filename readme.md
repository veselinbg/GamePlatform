Game Server System Requirements
1. Overview

The system consists of three logical components:

Load Balancer
Multiple Game Server instances
One shared database

The system must support running multiple Game Server instances concurrently. Each Game Server instance represents an independent process and listens on 
a different HTTP port.

The Load Balancer acts as a lightweight request broker. It receives incoming requests and forwards them to available Game Server instances.

All Game Server instances share the same database.

The initial deployment must support at least 5 Game Server instances running concurrently.

2. System Topology

The expected architecture is:

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


The Load Balancer and Game Server instances are separate application processes.

There is a single shared database used by all Game Server instances.

Game Server instances must not maintain separate copies of wallet or bet state.

3. Load Balancer

The Load Balancer must:

Accept incoming HTTP requests.
Maintain a configurable list of Game Server instances.
Forward requests to available Game Server instances.
Support at least 5 Game Server instances running simultaneously.
Support parallel request processing.
Distribute requests between available Game Server instances.
Detect unavailable or unhealthy instances.
Avoid routing new requests to unhealthy instances.
Support configurable request timeouts.
Support request cancellation.
Support a configurable request-distribution strategy.

The initial distribution strategy should be round-robin.

The Load Balancer must not contain game, wallet, bet, or financial business logic.

4. Game Server

Each Game Server must:

Run as an independent .NET 10 application instance.
Listen on a configurable HTTP port.
Expose the same HTTP API as every other Game Server instance.
Accept JSON requests.
Validate and deserialize incoming requests.
Process game operations.
Use Entity Framework Core for persistence.
Connect to the shared database.
Support concurrent request processing.
Be safe when multiple Game Server instances process requests simultaneously.

Game Server instances must not rely on process-local memory or C# locks for financial consistency.

5. Database

The system must use one shared database instance.

All Game Server instances must connect to the same database.

The initial database provider is SQLite.

Entity Framework Core must be used for database access.

The database must contain the data required to support:

Bets
Wallets
Ledger entries
Idempotency

The database is the authoritative source for financial state.

6. Bets

A Bet represents a game betting operation.

A Bet must contain at least:

Id
Amount
Wallet/Player reference
Operation/Request identifier
CreatedAt
Status

The exact status values must be defined by the business requirements.

A bet must not be created more than once for the same idempotent operation.

7. Wallets

A Wallet represents a player's financial account.

A Wallet must contain at least:

Id
Balance
Type
Concurrency/version information

The wallet balance represents a balance projection.

Wallet state must be protected against concurrent modifications from different Game Server instances.

8. Ledger

All financial changes to a Wallet must be represented by ledger entries.

A ledger entry must contain at least:

Id
WalletId
Amount
Transaction type
Reference to the related business operation
Idempotency key
CreatedAt

Ledger entries are immutable after creation.

Ledger entries must not be updated or deleted as part of normal business operations.

The ledger is the authoritative history of financial changes.

Every change to a wallet balance must have a corresponding ledger entry.

9. Balance Projection

The Wallet balance is a projection derived from the ledger.

For example:

Deposit       +100
Bet             -20
Win             +40
Bet             -10
-------------------
Balance         110


The system must maintain the Wallet balance consistently with the ledger.

The ledger entry and the corresponding Wallet balance update must occur within the same database transaction.

The system must provide a way to verify or recalculate the wallet balance from the ledger.

10. Atomicity

Financial operations must be atomic.

For a bet operation, the following operations must be part of the same database transaction:

Validate the wallet.
Validate sufficient balance.
Check idempotency.
Create the Bet.
Create the Ledger entry.
Update the Wallet balance projection.
Store the operation result/idempotency information.
Commit the transaction.

If any operation fails, the entire transaction must be rolled back.

A partially completed financial operation must never be visible.

11. Concurrency

The system must support concurrent requests targeting the same wallet.

For example, if the wallet has a balance of 100 and two Game Server instances simultaneously receive bets of 80:

Game Server #1 -> Bet 80
Game Server #2 -> Bet 80


the system must ensure that both requests cannot successfully spend the same 100 balance.

The final database state must remain financially consistent.

The system must prevent:

Lost updates.
Double spending.
Invalid negative balances.
Duplicate bets.
Duplicate ledger entries.
Inconsistent balance projections.

Concurrency correctness must work across different Game Server processes.

12. Optimistic Concurrency

Wallets must support optimistic concurrency control.

A version/concurrency token must be used to detect when a Wallet has been modified by another transaction.

If two Game Server instances attempt to modify the same Wallet concurrently, the database/application must detect the conflict.

The system must define whether a concurrency conflict should:

Be retried automatically, or
Return a conflict response to the client.

Concurrency conflicts must never result in silent data loss.

13. Transaction Isolation

The system must explicitly define the transaction isolation level used for financial operations.

The selected isolation level must prevent invalid concurrent wallet operations.

Database-level transaction and concurrency mechanisms must be used instead of relying on process-local application locks.

The selected strategy must be documented and covered by automated concurrency tests.

14. Locking

Financial consistency must not depend on a C# lock or other process-local synchronization mechanism.

Because multiple Game Server processes can access the same database simultaneously, synchronization must be enforced by the shared database.

The locking strategy must be compatible with the selected database provider.

15. Idempotency

All financial operations must be idempotent.

Each operation must contain a unique idempotency key.

If the same operation is submitted multiple times using the same idempotency key:

Only one financial transaction may be created.
Only one Bet may be created.
Only one Ledger entry may be created.
The Wallet may only be modified once.
Subsequent requests should return the result of the original operation.

The database must enforce uniqueness of the idempotency key.

Idempotency must work even when duplicate requests are received by different Game Server instances.

For example:

Request A -> Load Balancer -> Game Server #1
Request A -> Load Balancer -> Game Server #3


Both requests must result in only one financial operation.

16. Failure Handling

The system must define behavior for:

Game Server process failure.
Load Balancer connection failure.
Database failure.
Database transaction failure.
Request timeout.
Client disconnect.
Client retry.
Game Server failure after database commit but before the response is returned.
Duplicate requests sent to different Game Server instances.

A failure must not cause a financial operation to be applied twice.

17. Health Checks

Each Game Server must expose a health/readiness endpoint.

The Load Balancer must use the endpoint to determine whether a Game Server is available to receive requests.

An unhealthy Game Server must not receive new requests.

18. Configuration

The Load Balancer must have a configurable list of Game Server instances.

For example:

GameServer1 = http://localhost:6001
GameServer2 = http://localhost:6002
GameServer3 = http://localhost:6003
GameServer4 = http://localhost:6004
GameServer5 = http://localhost:6005


Adding another Game Server should require configuration rather than changes to business logic.

All Game Server instances must use the same database connection configuration.

19. Logging and Observability

Every request must have a unique correlation/request identifier.

The identifier must be available in logs produced by the Load Balancer and Game Server.

The system should log:

Request ID.
Game Server instance.
Request type.
Processing result.
Processing duration.
Errors.
Database/concurrency conflicts.
Idempotency conflicts.
Retries.

Sensitive financial information must not be unnecessarily written to logs.

20. Testing

The system must include automated tests for:

Normal bet processing.
Insufficient balance.
Concurrent bets against the same wallet.
Concurrent operations against different wallets.
Duplicate requests.
Same idempotency key received by different Game Server instances.
Transaction rollback.
Optimistic concurrency conflicts.
Ledger/balance consistency.
Load Balancer request distribution.
Unhealthy Game Server handling.
Game Server failure.
Request timeout.
Retry after timeout.
Running at least 5 Game Server instances simultaneously.

Concurrency tests must verify the final database state, not only HTTP responses.

21. Core Architectural Principles

The following responsibilities must remain separated:

Load Balancer

Responsible for:

Routing
Health checks
Availability
Request distribution

Game Server

Responsible for:

Request validation
Game/business logic
Application services

Database

Responsible for:

Persistent state
Transactions
Concurrency control
Financial consistency
Idempotency constraints

Financial correctness must not depend on the Load Balancer or process-local memory.