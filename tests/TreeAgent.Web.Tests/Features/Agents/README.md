# Claude Code Integration Tests

This directory contains integration tests for Claude Code interaction.

## Quick Start

Run the Claude Code integration tests:

```bash
dotnet test --filter "Category=Integration&Category=ClaudeCode&Category!=Deprecated"
```

## Test Approach

The tests use the **query mode** approach inspired by [happy-cli](https://github.com/slopus/happy-cli):

### Key Features

1. **`--print` mode**: One-shot queries that complete when Claude finishes responding
2. **`--output-format stream-json`**: Structured JSON output for reliable parsing
3. **`--verbose`**: Includes system messages with session information
4. **Parallel execution**: Tests run concurrently using NUnit's `[Parallelizable]` attribute

### Message Types

Claude Code outputs JSON messages with these types:
- `system`: Session initialization (contains `session_id`)
- `user`: User prompts
- `assistant`: Claude's responses
- `result`: Final result with success/error status

### Why This Approach?

The previous interactive mode approach had issues:
- Used fixed `Task.Delay` waits (unreliable timing)
- Could not detect when Claude was ready
- Tests ran sequentially (slow)
- Interactive stdin/stdout coordination often failed

The query mode approach solves these by:
- Detecting readiness via `system` message parsing
- Detecting completion via `result` message parsing
- Supporting parallel test execution
- Using structured output instead of raw text

## Test Files

- `ClaudeCodeQueryIntegrationTests.cs`: Active tests using query mode
- `Helpers/ClaudeCodeTestProcess.cs`: Test process wrapper for queries
- `Fixtures/ClaudeCodeQueryFixture.cs`: Test fixture with isolated working directories

### Deprecated

- `ClaudeCodeProcessIntegrationTests.cs`: Old interactive mode tests (marked `[Explicit]`)
- `ClaudeCodeProcessManagerIntegrationTests.cs`: Old manager tests (marked `[Explicit]`)

## Requirements

- Claude Code CLI must be installed and available
- Set `CLAUDE_CODE_PATH` environment variable to override the default path
