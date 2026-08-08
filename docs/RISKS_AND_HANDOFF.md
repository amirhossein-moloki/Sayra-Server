# Risks, Open Questions & Stage 01-02 Handoff

This document summarizes the architectural risks, open technical questions requiring validation, and the exact handoff package intended to bootstrap **Stage 01-02: Domain & Bounded Context Design**.

---

## 1. Architectural Risks

### R-01: Client Protocol Immutability Lock-In
* **Description**: Since we cannot modify the compiled client binary, any unexpected deviation between our custom TCP/UDP parsers and the actual client serialization/handshake payload will cause total communication failure.
* **Mitigation**: Stage 01-02 must dedicate efforts to mapping out message contracts exactly matching reverse-engineered packets from the actual client.

### R-02: Local CA / PKI Certificate Management Failure
* **Description**: Managing a local Certificate Authority (Root CA) can be prone to operational errors, such as clocks drifting on local machines, expired certificates, or clients failing to register the Root CA.
* **Mitigation**: Build robust certificate health dashboards and implement automated, non-disruptive certificate auto-renewal background workers inside the core monolith.

### R-03: Heavy TCP Memory Allocations under High Telemetry Loads
* **Description**: With hundreds of workstations sending frequent telemetry updates, high-frequency byte buffer parsing inside .NET Core Sockets could cause excessive Garbage Collection (GC) pressure, degrading API responsiveness.
* **Mitigation**: Mandate the use of pre-allocated buffers, `ArrayPool<byte>`, and `System.IO.Pipelines` to parse incoming TCP packets in a completely allocation-free manner.

---

## 2. Open Technical Questions

1. **UDP Signature Key Exchange**: How does the client verify that a UDP discovery response comes from an authentic, certified server? Does the client hold a hardcoded public key, or does it dynamically download a trust bundle?
2. **Offline Event Ingestion Queue limits**: When workstations recover from local network partitions, they flush their offline event queues. What is the maximum batch size supported by the client in a single post request, and how does the backend enforce rate limits on these flushes?
3. **Hardware Fingerprint Algorithm**: How does the licensing module generate its server identity fingerprint? What native OS syscalls (MAC address, motherboard BIOS UUID, CPU ID) does it use, and how do we ensure consistency across Windows and Linux Docker container host mappings?

---

## 3. Stage 01-02 Handoff Specification

Stage 01-02 will consume the architectural boundaries defined in Stage 01-01 to perform deep Domain Modeling and Bounded Context design.

### 3.1. Core Domain Interfaces to Model

```csharp
namespace Sayra.Core.Interfaces
{
    // The strict interface that allows other modules to query workstation status without sharing DB schemas
    public interface IWorkstationPublicApi
    {
        Task<bool> IsWorkstationAvailableAsync(Guid workstationId);
        Task<WorkstationDto> GetWorkstationDetailsAsync(Guid workstationId);
    }

    // The strict interface for session tracking, accessible to Billing and Remote Ops modules
    public interface ISessionPublicApi
    {
        Task<SessionDto> GetActiveSessionAsync(Guid workstationId);
        Task<bool> StartSessionAsync(Guid workstationId, Guid userId, decimal initialCredit);
        Task EndSessionAsync(Guid sessionId, string terminationReason);
    }

    // Shared domain events dispatched onto the In-Memory Event Bus
    public record WorkstationOnlineEvent(Guid WorkstationId, string IpAddress, string ClientVersion, DateTime Timestamp);
    public record SessionStartedEvent(Guid SessionId, Guid WorkstationId, Guid UserId, DateTime StartTime);
    public record SessionEndedEvent(Guid SessionId, Guid WorkstationId, Guid UserId, DateTime EndTime, decimal TotalCharge);
}
```

### 3.2. Concrete Architectural Constraints for Domain Modeling
* **Constraint 1**: Cross-module database foreign keys are strictly prohibited.
* **Constraint 2**: All financial values in calculations, interfaces, and shared DTOs must use the `decimal` type. No `double` or `float` types are permitted.
* **Constraint 3**: Timestamps inside shared events and DB entities must be strictly UTC (`DateTimeKind.Utc`).
