# Development Instructions

This file contains instructions for Claude Code when working on this project.

## Development Practices

### Test Driven Development (TDD)

Use Test Driven Development practices where possible:

1. **Write tests first** - Before implementing a feature, write failing tests that define the expected behavior
2. **Red-Green-Refactor** - Follow the TDD cycle:
   - Red: Write a failing test
   - Green: Write minimal code to make the test pass
   - Refactor: Clean up the code while keeping tests green
3. **Test naming** - Use descriptive test names that explain the scenario and expected outcome
4. **Test coverage** - Aim for comprehensive coverage of business logic in services

### Project Structure

```
src/TreeAgent.Web/
├── Components/       # Blazor components and pages
├── Data/
│   └── Entities/    # EF Core entity classes
├── Services/        # Business logic services
└── Program.cs       # Application entry point
```

### Running the Application

```bash
cd src/TreeAgent.Web
dotnet run
```

### Running Tests

```bash
dotnet test
```

### Database

- SQLite database with EF Core
- Migrations applied automatically on startup
- Database file: `treeagent.db` (gitignored)

### Creating Migrations

```bash
cd src/TreeAgent.Web
dotnet ef migrations add <MigrationName>
```
