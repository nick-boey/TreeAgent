namespace Homespun.Tests.Features.Beads.Fixtures;

/// <summary>
/// Test fixture that creates a temporary copy of a beads SQLite database for testing.
/// </summary>
public class BeadsTestDatabaseFixture : IDisposable
{
    private readonly string _tempDbPath;
    private bool _disposed;

    /// <summary>
    /// Path to the temporary database file.
    /// </summary>
    public string DatabasePath => _tempDbPath;

    /// <summary>
    /// Directory containing the temporary database (acts as project path).
    /// </summary>
    public string ProjectPath { get; }

    /// <summary>
    /// Creates a new test fixture by copying an existing beads database.
    /// </summary>
    /// <param name="sourceProjectPath">Path to the project containing .beads/beads.db</param>
    public BeadsTestDatabaseFixture(string sourceProjectPath)
    {
        var sourceDbPath = Path.Combine(sourceProjectPath, ".beads", "beads.db");

        if (!File.Exists(sourceDbPath))
        {
            throw new FileNotFoundException($"Source database not found at: {sourceDbPath}");
        }

        // Create a unique temp directory for this test
        ProjectPath = Path.Combine(Path.GetTempPath(), $"beads-test-{Guid.NewGuid()}");
        var tempBeadsDir = Path.Combine(ProjectPath, ".beads");
        Directory.CreateDirectory(tempBeadsDir);

        _tempDbPath = Path.Combine(tempBeadsDir, "beads.db");

        // Copy the database file (but not WAL/SHM files - we want a clean copy)
        File.Copy(sourceDbPath, _tempDbPath);
    }

    /// <summary>
    /// Creates an empty test database with the expected schema.
    /// </summary>
    public static BeadsTestDatabaseFixture CreateEmpty()
    {
        // Create a temp directory
        var projectPath = Path.Combine(Path.GetTempPath(), $"beads-test-{Guid.NewGuid()}");
        var beadsDir = Path.Combine(projectPath, ".beads");
        Directory.CreateDirectory(beadsDir);

        var dbPath = Path.Combine(beadsDir, "beads.db");

        // Create database with minimal schema
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS issues (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                description TEXT,
                status TEXT DEFAULT 'open',
                priority INTEGER DEFAULT 2,
                issue_type TEXT DEFAULT 'task',
                assignee TEXT,
                parent_id TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                closed_at TEXT,
                close_reason TEXT,
                deleted_at TEXT
            );

            CREATE TABLE IF NOT EXISTS labels (
                issue_id TEXT NOT NULL,
                label TEXT NOT NULL,
                PRIMARY KEY (issue_id, label),
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS dependencies (
                issue_id TEXT NOT NULL,
                depends_on_id TEXT NOT NULL,
                type TEXT DEFAULT 'blocks',
                created_at TEXT,
                PRIMARY KEY (issue_id, depends_on_id),
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE,
                FOREIGN KEY (depends_on_id) REFERENCES issues(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                issue_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                actor TEXT,
                old_value TEXT,
                new_value TEXT,
                comment TEXT,
                created_at TEXT NOT NULL,
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
            );
            """;
        command.ExecuteNonQuery();

        return new BeadsTestDatabaseFixture(projectPath, dbPath);
    }

    // Private constructor for CreateEmpty
    private BeadsTestDatabaseFixture(string projectPath, string dbPath)
    {
        ProjectPath = projectPath;
        _tempDbPath = dbPath;
    }

    /// <summary>
    /// Inserts a test issue into the database.
    /// </summary>
    public void InsertIssue(string id, string title, string status = "open", string issueType = "task",
        int priority = 2, string? description = null, string? assignee = null)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_tempDbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO issues (id, title, description, status, priority, issue_type, assignee, created_at, updated_at)
            VALUES ($id, $title, $description, $status, $priority, $issueType, $assignee, $createdAt, $updatedAt)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$description", description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$priority", priority);
        command.Parameters.AddWithValue("$issueType", issueType);
        command.Parameters.AddWithValue("$assignee", assignee ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Adds a label to an issue.
    /// </summary>
    public void AddLabel(string issueId, string label)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_tempDbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO labels (issue_id, label) VALUES ($issueId, $label)";
        command.Parameters.AddWithValue("$issueId", issueId);
        command.Parameters.AddWithValue("$label", label);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Adds a dependency between issues.
    /// </summary>
    public void AddDependency(string issueId, string dependsOnId, string type = "blocks")
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_tempDbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dependencies (issue_id, depends_on_id, type, created_at)
            VALUES ($issueId, $dependsOnId, $type, $createdAt)
            """;
        command.Parameters.AddWithValue("$issueId", issueId);
        command.Parameters.AddWithValue("$dependsOnId", dependsOnId);
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // Delete the entire temp directory
            if (Directory.Exists(ProjectPath))
            {
                Directory.Delete(ProjectPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}
