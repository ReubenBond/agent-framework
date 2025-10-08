# AgentGateway Unit Tests

This project contains comprehensive unit tests for the AgentGateway conversation storage implementations.

## Test Structure

### InMemoryConversationStorageTests
Tests for the in-memory implementation of `IConversationStorage`.

**Coverage:**
- ✅ Conversation CRUD operations
- ✅ Message CRUD operations
- ✅ Pagination (limit, after, before cursors)
- ✅ Ordering (ascending/descending)
- ✅ Error handling (duplicates, non-existent entities)
- ✅ Cascade deletion (messages deleted with conversation)

### ConversationGrainTests
Tests for the Orleans grain implementation that manages individual conversations.

**Coverage:**
- ✅ Grain state persistence
- ✅ Conversation lifecycle (create, read, update, delete)
- ✅ Message management within a grain
- ✅ OrderedDictionary insertion order preservation
- ✅ Efficient pagination using IndexOf/GetAt
- ✅ Error handling

**Key Features Tested:**
- Uses `OrderedDictionary<TKey, TValue>` for message storage
- Validates insertion order is maintained
- Tests efficient indexed access patterns

### OrleansConversationStorageTests
Tests for the complete Orleans-backed storage implementation.

**Coverage:**
- ✅ Full IConversationStorage interface implementation
- ✅ Integration with ConversationGrain and ConversationIndexGrain
- ✅ Index maintenance (add/remove conversations)
- ✅ Multi-conversation scenarios
- ✅ Isolation between conversations
- ✅ End-to-end workflows

### ConversationIndexGrainTests
Tests for the singleton grain that maintains the conversation index.

**Coverage:**
- ✅ Index management (add, remove, list)
- ✅ Duplicate handling
- ✅ Sorting by creation timestamp
- ✅ Pagination with cursors
- ✅ Limit clamping
- ✅ Complex pagination scenarios

## Running Tests

### Run all tests
```powershell
dotnet test
```

### Run specific test class
```powershell
dotnet test --filter "FullyQualifiedName~InMemoryConversationStorageTests"
dotnet test --filter "FullyQualifiedName~ConversationGrainTests"
dotnet test --filter "FullyQualifiedName~OrleansConversationStorageTests"
dotnet test --filter "FullyQualifiedName~ConversationIndexGrainTests"
```

### Run with code coverage
```powershell
dotnet test --collect:"XPlat Code Coverage"
```

## Test Framework

- **xUnit**: Primary testing framework
- **FluentAssertions**: Readable assertions
- **Microsoft.Orleans.TestingHost**: Orleans testing infrastructure
- **Moq**: Mocking framework (if needed for future tests)

## Orleans Test Cluster

The Orleans tests use `TestCluster` which:
- Provides an in-memory Orleans cluster
- Uses memory grain storage
- Isolated per test class instance
- Automatic cleanup via `IAsyncLifetime`

## Test Patterns

### Arrange-Act-Assert
All tests follow the AAA pattern:
```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    var input = CreateTestData();
    
    // Act
    var result = await _storage.Method(input);
    
    // Assert
    result.Should().NotBeNull();
}
```

### Helper Methods
Common test data creation:
- `CreateTestConversation()` - Creates test conversation objects
- `CreateTestMessage()` - Creates test message objects

### Test Naming Convention
`MethodName_Scenario_ExpectedBehavior`

Examples:
- `CreateConversationAsync_ShouldCreateConversation`
- `AddMessageAsync_ToNonExistentConversation_ShouldThrow`
- `ListMessagesAsync_WithLimit_ShouldReturnLimitedResults`

## Test Coverage Summary

| Component | Tests | Coverage Areas |
|-----------|-------|----------------|
| InMemoryConversationStorage | 21 | CRUD, Pagination, Ordering, Errors |
| ConversationGrain | 18 | Grain lifecycle, Messages, OrderedDictionary |
| OrleansConversationStorage | 24 | Full storage, Integration, Index sync |
| ConversationIndexGrain | 13 | Index management, Pagination, Sorting |
| **Total** | **76** | **Comprehensive** |

## Key Test Scenarios

### OrderedDictionary Testing
- Validates insertion order preservation
- Tests efficient `IndexOf()` and `GetAt()` usage
- Verifies pagination without LINQ operations

### Pagination Testing
- Cursor-based pagination (after/before)
- Limit enforcement and clamping
- HasMore flag accuracy
- Multi-page navigation

### Concurrency (via Orleans)
- Grain single-threaded execution model
- State persistence across grain activations
- Multiple conversation isolation

### Error Handling
- Duplicate IDs
- Non-existent entities
- Invalid operations

## Future Enhancements

Potential additional tests:
- [ ] Performance benchmarks
- [ ] Concurrent operation stress tests
- [ ] Grain reactivation scenarios
- [ ] Storage provider failure simulation
- [ ] Large dataset pagination performance

## Dependencies

```xml
<PackageReference Include="FluentAssertions" />
<PackageReference Include="Microsoft.Orleans.TestingHost" />
<PackageReference Include="xunit" />
<PackageReference Include="xunit.runner.visualstudio" />
```

## CI/CD Integration

These tests are designed to run in CI/CD pipelines:
- No external dependencies required
- Fast execution (in-memory)
- Deterministic results
- Isolated test instances
