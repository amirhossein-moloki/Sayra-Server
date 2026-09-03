# Testing Strategy & Execution Guide

This document details the testing architecture, test project structure, execution procedures, and quality expectations for the **SAYRA Central Backend**.

---

## 1. Test Architecture & Project Structure

The repository organizes tests into three distinct test projects located under `tests/`:

```text
tests/
├── Sayra.Backend.UnitTests/         # Fast, isolated domain & application unit tests (No DB required)
├── Sayra.Backend.IntegrationTests/  # Infrastructure, PostgreSQL, Redis, TCP/TLS integration tests
└── Sayra.Backend.ArchitectureTests/ # ArchUnit structural boundary enforcement tests
```

---

## 2. Running Tests

### 2.1. Fast Unit Test Suite (370+ Tests)
Unit tests evaluate domain entities, value objects, CQRS handlers, schema validation, configuration normalizer, delta engine, sequence validator, and security hashing in total isolation without needing Docker containers.

Run unit tests via .NET CLI:
```bash
dotnet test tests/Sayra.Backend.UnitTests/Sayra.Backend.UnitTests.csproj
```

### 2.2. Architecture Invariant Tests
Architecture tests verify Clean Architecture layer isolation rules, ensuring `Domain` has no infrastructure dependencies and modules do not reference internal schemas.

Run architecture tests:
```bash
dotnet test tests/Sayra.Backend.ArchitectureTests/Sayra.Backend.ArchitectureTests.csproj
```

### 2.3. Full Integration Test Suite
Integration tests validate end-to-end flow against live PostgreSQL and Redis instances. Ensure `docker compose up -d` is running prior to execution.

Run integration tests:
```bash
dotnet test tests/Sayra.Backend.IntegrationTests/Sayra.Backend.IntegrationTests.csproj
```

---

## 3. Testing Conventions & Guidelines

1. **Test Framework**: [xUnit](https://xunit.net/) with [FluentAssertions](https://fluentassertions.com/) and [Moq](https://github.com/devlooped/moq).
2. **Naming Standard**: `[UnitUnderTest]_[Scenario]_[ExpectedOutcome]` (e.g., `Debit_WithInsufficientBalance_ShouldThrowFinancialDomainException`).
3. **Decimal Verification**: Financial unit tests must assert exact `decimal` precision without rounding or floating-point conversions.
4. **Clean State**: Unit tests must instantiate new isolated domain entities or mocks per test method; never share static mutable state across test runs.
