using RabstackQuery.Example.Shared.Models;

namespace RabstackQuery.Example.Shared.Services;

/// <summary>
/// In-memory mock API with configurable delays and error injection.
/// Errors use specific exception types so the UI can display meaningful
/// messages (not generic "something went wrong").
/// </summary>
public sealed class MockTaskBoardApi : ITaskBoardApi
{
    private readonly MockApiSettings _settings;
    private readonly Random _random = new();
    private readonly Lock _lock = new();

    private readonly List<Project> _projects = [];
    private readonly List<TaskItem> _tasks = [];
    private readonly List<Comment> _comments = [];

    private int _nextProjectId = 1;
    private int _nextTaskId = 1;
    private int _nextCommentId = 1;

    public MockTaskBoardApi(MockApiSettings settings)
    {
        _settings = settings;
        SeedData();
    }

    // ── Projects ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            return _projects.Select(p => WithComputedCounts(p)).ToList();
        }
    }

    public async Task<Project> GetProjectAsync(int projectId, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            var project = _projects.FirstOrDefault(p => p.Id == projectId);
            if (project is null) throw new NotFoundException($"Project {projectId} not found");
            return WithComputedCounts(project);
        }
    }

    public async Task<Project> CreateProjectAsync(string name, string description, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            var project = new Project
            {
                Id = _nextProjectId++,
                Name = name,
                Description = description,
                Color = RandomColor(),
                TaskCount = 0,
                CompletedTaskCount = 0,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _projects.Add(project);
            return project;
        }
    }

    // ── Tasks ────────────────────────────────────────────────────────────

    public async Task<PagedResult<TaskItem>> GetTasksAsync(
        int projectId, string? cursor = null, int pageSize = 20, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            if (!_projects.Any(p => p.Id == projectId))
                throw new NotFoundException($"Project {projectId} not found");

            var allTasks = _tasks
                .Where(t => t.ProjectId == projectId)
                .OrderBy(t => t.CreatedAt)
                .ToList();

            // Cursor-based pagination: cursor is the ID of the last item seen
            var startIndex = 0;
            if (cursor is not null && int.TryParse(cursor, out var afterId))
            {
                startIndex = allTasks.FindIndex(t => t.Id == afterId) + 1;
                if (startIndex <= 0) startIndex = 0;
            }

            var page = allTasks.Skip(startIndex).Take(pageSize).ToList();
            var hasMore = startIndex + pageSize < allTasks.Count;

            return new PagedResult<TaskItem>
            {
                Items = page,
                NextCursor = hasMore ? page[^1].Id.ToString() : null,
                TotalCount = allTasks.Count
            };
        }
    }

    public async Task<TaskItem> GetTaskAsync(int projectId, int taskId, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.ProjectId == projectId && t.Id == taskId);
            if (task is null) throw new NotFoundException($"Task {taskId} not found in project {projectId}");
            return task with { CommentCount = _comments.Count(c => c.TaskId == taskId) };
        }
    }

    public async Task<TaskItem> CreateTaskAsync(
        int projectId, string title, TaskPriority priority, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            if (!_projects.Any(p => p.Id == projectId))
                throw new NotFoundException($"Project {projectId} not found");

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var task = new TaskItem
            {
                Id = _nextTaskId++,
                ProjectId = projectId,
                Title = title,
                Description = null,
                Priority = priority,
                Status = TaskItemStatus.Todo,
                AssigneeName = null,
                CommentCount = 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            _tasks.Add(task);
            return task;
        }
    }

    public async Task<TaskItem> UpdateTaskStatusAsync(
        int projectId, int taskId, TaskItemStatus status, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            var index = _tasks.FindIndex(t => t.ProjectId == projectId && t.Id == taskId);
            if (index < 0) throw new NotFoundException($"Task {taskId} not found");

            var updated = _tasks[index] with
            {
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            _tasks[index] = updated;
            return updated;
        }
    }

    public async Task<TaskItem> UpdateTaskAsync(
        int projectId, int taskId, string title, string? description,
        TaskPriority priority, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            var index = _tasks.FindIndex(t => t.ProjectId == projectId && t.Id == taskId);
            if (index < 0) throw new NotFoundException($"Task {taskId} not found");

            var updated = _tasks[index] with
            {
                Title = title,
                Description = description,
                Priority = priority,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            _tasks[index] = updated;
            return updated;
        }
    }

    public async Task DeleteTaskAsync(int projectId, int taskId, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            var index = _tasks.FindIndex(t => t.ProjectId == projectId && t.Id == taskId);
            if (index < 0) throw new NotFoundException($"Task {taskId} not found");

            _tasks.RemoveAt(index);
            _comments.RemoveAll(c => c.TaskId == taskId);
        }
    }

    // ── Comments ─────────────────────────────────────────────────────────

    public async Task<PagedResult<Comment>> GetCommentsAsync(
        int taskId, string? cursor = null, int pageSize = 10, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            var allComments = _comments
                .Where(c => c.TaskId == taskId)
                .OrderByDescending(c => c.CreatedAt)
                .ToList();

            var startIndex = 0;
            if (cursor is not null && int.TryParse(cursor, out var afterId))
            {
                startIndex = allComments.FindIndex(c => c.Id == afterId) + 1;
                if (startIndex <= 0) startIndex = 0;
            }

            var page = allComments.Skip(startIndex).Take(pageSize).ToList();
            var hasMore = startIndex + pageSize < allComments.Count;

            return new PagedResult<Comment>
            {
                Items = page,
                NextCursor = hasMore ? page[^1].Id.ToString() : null,
                TotalCount = allComments.Count
            };
        }
    }

    public async Task<Comment> AddCommentAsync(int taskId, string body, CancellationToken ct = default)
    {
        await SimulateNetworkAsync(ct);

        lock (_lock)
        {
            if (!_tasks.Any(t => t.Id == taskId))
                throw new NotFoundException($"Task {taskId} not found");

            var comment = new Comment
            {
                Id = _nextCommentId++,
                TaskId = taskId,
                Author = RandomAuthor(),
                Body = body,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _comments.Add(comment);
            return comment;
        }
    }

    /// <summary>
    /// Resets all data to the initial seed state. Called from the Settings
    /// panel's "Reset All Data" button.
    /// </summary>
    public void ResetData()
    {
        lock (_lock)
        {
            _projects.Clear();
            _tasks.Clear();
            _comments.Clear();
            _nextProjectId = 1;
            _nextTaskId = 1;
            _nextCommentId = 1;
            SeedData();
        }
    }

    // ── Network simulation ───────────────────────────────────────────────

    private async Task SimulateNetworkAsync(CancellationToken ct)
    {
        if (_settings.SimulateOffline)
            throw new OfflineException();

        var delay = _random.Next(_settings.MinDelayMs, _settings.MaxDelayMs + 1);
        await Task.Delay(delay, ct);

        if (_settings.ErrorRate > 0 && _random.NextDouble() < _settings.ErrorRate)
        {
            // Rotate through realistic error types
            var errorType = _random.Next(3);
            throw errorType switch
            {
                0 => new ConflictException("Task was modified by another user"),
                1 => new RateLimitException("Too many requests — try again in 5 seconds"),
                _ => new InvalidOperationException("Simulated server error")
            };
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private Project WithComputedCounts(Project project)
    {
        var tasks = _tasks.Where(t => t.ProjectId == project.Id).ToList();
        return project with
        {
            TaskCount = tasks.Count,
            CompletedTaskCount = tasks.Count(t => t.Status is TaskItemStatus.Done)
        };
    }

    private static readonly string[] Authors = ["Alice", "Bob", "Charlie", "Diana", "Eve"];
    private string RandomAuthor() => Authors[_random.Next(Authors.Length)];

    private static readonly string[] Colors = ["#4F46E5", "#059669", "#D97706", "#DC2626", "#7C3AED", "#2563EB"];
    private string RandomColor() => Colors[_random.Next(Colors.Length)];

    // ── Seed data ────────────────────────────────────────────────────────

    private void SeedData()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 3 projects with realistic names
        var projects = new[]
        {
            ("Mobile App Redesign", "Modernize the mobile experience with new design system", "#4F46E5"),
            ("API v2 Migration", "Migrate all endpoints to the new REST v2 specification", "#059669"),
            ("Q1 Marketing Campaign", "Coordinate cross-channel marketing for Q1 product launch", "#D97706")
        };

        foreach (var (name, desc, color) in projects)
        {
            _projects.Add(new Project
            {
                Id = _nextProjectId++,
                Name = name,
                Description = desc,
                Color = color,
                TaskCount = 0,
                CompletedTaskCount = 0,
                CreatedAt = now - _random.Next(1_000_000, 10_000_000)
            });
        }

        // Task templates per project — enough to demonstrate infinite scroll
        var taskTemplates = new Dictionary<int, (string Title, string? Desc, TaskPriority Priority, TaskItemStatus Status, string? Assignee)[]>
        {
            [1] = // Mobile App Redesign
            [
                ("Set up design tokens", "Define color palette, spacing, and typography tokens", TaskPriority.High, TaskItemStatus.Done, "Alice"),
                ("Create bottom navigation component", "Tab bar with 4 sections", TaskPriority.High, TaskItemStatus.Done, "Bob"),
                ("Implement dark mode", "Support system and manual toggle", TaskPriority.Medium, TaskItemStatus.Done, "Alice"),
                ("Build onboarding flow", "3-screen carousel with skip option", TaskPriority.Medium, TaskItemStatus.Done, "Charlie"),
                ("Design settings page", null, TaskPriority.Low, TaskItemStatus.Done, "Diana"),
                ("Implement pull-to-refresh", "Use platform-native refresh control", TaskPriority.Medium, TaskItemStatus.Review, "Bob"),
                ("Add haptic feedback", "Subtle vibration on button presses", TaskPriority.Low, TaskItemStatus.Review, "Eve"),
                ("Profile page layout", "Avatar, stats, and action buttons", TaskPriority.High, TaskItemStatus.InProgress, "Alice"),
                ("Notification preferences screen", null, TaskPriority.Medium, TaskItemStatus.InProgress, "Charlie"),
                ("Accessibility audit", "VoiceOver and TalkBack testing", TaskPriority.Urgent, TaskItemStatus.InProgress, "Diana"),
                ("Implement search with filters", "Full-text search with category chips", TaskPriority.High, TaskItemStatus.Todo, "Bob"),
                ("Offline mode indicator", "Banner when connection is lost", TaskPriority.Medium, TaskItemStatus.Todo, null),
                ("Animated transitions between screens", null, TaskPriority.Low, TaskItemStatus.Todo, null),
                ("Tablet layout adaptations", "Responsive grid for iPads", TaskPriority.Medium, TaskItemStatus.Todo, "Eve"),
                ("Performance profiling", "Target 60fps on mid-range devices", TaskPriority.High, TaskItemStatus.Todo, null),
                ("Beta user feedback survey", "In-app feedback form", TaskPriority.Low, TaskItemStatus.Todo, "Charlie"),
                ("Crash reporting integration", "Set up Sentry with source maps", TaskPriority.Urgent, TaskItemStatus.Todo, "Alice"),
                ("Widget for home screen", "Show task count and quick-add", TaskPriority.Low, TaskItemStatus.Todo, null),
            ],
            [2] = // API v2 Migration
            [
                ("Audit current v1 endpoints", "Document all existing endpoints and consumers", TaskPriority.Urgent, TaskItemStatus.Done, "Bob"),
                ("Design v2 schema", "OpenAPI 3.1 spec with examples", TaskPriority.High, TaskItemStatus.Done, "Alice"),
                ("Set up API versioning middleware", null, TaskPriority.High, TaskItemStatus.Done, "Charlie"),
                ("Migrate /users endpoints", "Include pagination and field selection", TaskPriority.High, TaskItemStatus.Done, "Bob"),
                ("Migrate /projects endpoints", null, TaskPriority.High, TaskItemStatus.Done, "Diana"),
                ("Migrate /tasks endpoints", "Add cursor pagination", TaskPriority.High, TaskItemStatus.Review, "Alice"),
                ("Migrate /comments endpoints", null, TaskPriority.Medium, TaskItemStatus.Review, "Eve"),
                ("Add rate limiting", "Token bucket per API key", TaskPriority.High, TaskItemStatus.InProgress, "Bob"),
                ("Write migration guide", "Document breaking changes for consumers", TaskPriority.Medium, TaskItemStatus.InProgress, "Charlie"),
                ("Update SDK clients", "Regenerate TypeScript and C# clients", TaskPriority.High, TaskItemStatus.InProgress, "Alice"),
                ("Deprecation notices on v1", "Log warnings, add Sunset header", TaskPriority.Medium, TaskItemStatus.Todo, null),
                ("Load testing v2 endpoints", "Target 10k RPS sustained", TaskPriority.High, TaskItemStatus.Todo, "Diana"),
                ("Webhook payload v2 format", null, TaskPriority.Medium, TaskItemStatus.Todo, null),
                ("Update Postman collection", "Public workspace with examples", TaskPriority.Low, TaskItemStatus.Todo, "Eve"),
                ("v1 sunset date announcement", "Blog post and email to consumers", TaskPriority.Medium, TaskItemStatus.Todo, "Charlie"),
                ("Add request/response logging", "Structured JSON logs with correlation IDs", TaskPriority.Medium, TaskItemStatus.Todo, null),
                ("GraphQL gateway evaluation", "Spike on Apollo Federation for v3", TaskPriority.Low, TaskItemStatus.Todo, null),
                ("Error response standardization", "RFC 9457 Problem Details format", TaskPriority.High, TaskItemStatus.Todo, "Bob"),
                ("Batch endpoint support", "POST /v2/batch for bulk operations", TaskPriority.Medium, TaskItemStatus.Todo, null),
                ("API analytics dashboard", "Track adoption rate of v2 vs v1", TaskPriority.Low, TaskItemStatus.Todo, "Diana"),
            ],
            [3] = // Q1 Marketing Campaign
            [
                ("Define campaign objectives", "KPIs and target metrics for Q1", TaskPriority.Urgent, TaskItemStatus.Done, "Diana"),
                ("Audience segmentation", "Identify 3 primary segments", TaskPriority.High, TaskItemStatus.Done, "Eve"),
                ("Create brand guidelines update", "2024 refresh for digital channels", TaskPriority.High, TaskItemStatus.Done, "Alice"),
                ("Design email templates", "Welcome, nurture, and re-engagement flows", TaskPriority.Medium, TaskItemStatus.Done, "Charlie"),
                ("Landing page wireframes", "A/B test variants for hero section", TaskPriority.High, TaskItemStatus.Review, "Bob"),
                ("Social media content calendar", "4 weeks of posts across platforms", TaskPriority.Medium, TaskItemStatus.Review, "Diana"),
                ("Video ad script", "30s and 60s variants for YouTube", TaskPriority.High, TaskItemStatus.InProgress, "Eve"),
                ("Influencer outreach list", "Top 20 in our vertical", TaskPriority.Medium, TaskItemStatus.InProgress, "Alice"),
                ("Set up tracking pixels", "Meta, Google, and LinkedIn", TaskPriority.High, TaskItemStatus.InProgress, "Charlie"),
                ("Budget allocation spreadsheet", null, TaskPriority.Urgent, TaskItemStatus.InProgress, "Diana"),
                ("PR press release draft", "Embargo date TBD", TaskPriority.Medium, TaskItemStatus.Todo, null),
                ("Coordinate with sales team", "Enablement deck and talk tracks", TaskPriority.High, TaskItemStatus.Todo, "Bob"),
                ("Customer testimonial videos", "3 case studies to film", TaskPriority.Medium, TaskItemStatus.Todo, "Eve"),
                ("Print collateral for trade show", "Brochures and banners", TaskPriority.Low, TaskItemStatus.Todo, null),
                ("Competitive analysis update", "Positioning matrix refresh", TaskPriority.Medium, TaskItemStatus.Todo, "Alice"),
                ("Launch day social media blitz", "Coordinated posts at 9am ET", TaskPriority.High, TaskItemStatus.Todo, null),
                ("Post-launch survey", "NPS and feature interest", TaskPriority.Low, TaskItemStatus.Todo, "Charlie"),
            ]
        };

        foreach (var (projectId, templates) in taskTemplates)
        {
            foreach (var (title, desc, priority, status, assignee) in templates)
            {
                var taskCreatedAt = now - _random.Next(100_000, 5_000_000);
                var task = new TaskItem
                {
                    Id = _nextTaskId++,
                    ProjectId = projectId,
                    Title = title,
                    Description = desc,
                    Priority = priority,
                    Status = status,
                    AssigneeName = assignee,
                    CommentCount = 0,
                    CreatedAt = taskCreatedAt,
                    UpdatedAt = taskCreatedAt + _random.Next(0, 100_000)
                };
                _tasks.Add(task);
            }
        }

        // Seed comments on a subset of tasks
        var commentBodies = new[]
        {
            "Looks good to me, let's ship it.",
            "Can we revisit the color choice here?",
            "I tested this on Android and it works perfectly.",
            "This needs more error handling for edge cases.",
            "Blocked on the API changes — waiting for the v2 migration.",
            "Updated the design file with the latest feedback.",
            "Let's discuss this in the next standup.",
            "Added unit tests for the happy path.",
            "The performance numbers look promising!",
            "Can someone review my PR for this?",
            "I think we should split this into smaller tasks.",
            "Customer feedback says this is the top priority.",
        };

        foreach (var task in _tasks.Where(t => t.Status is not TaskItemStatus.Todo).Take(20))
        {
            var commentCount = _random.Next(1, 4);
            for (var i = 0; i < commentCount; i++)
            {
                _comments.Add(new Comment
                {
                    Id = _nextCommentId++,
                    TaskId = task.Id,
                    Author = RandomAuthor(),
                    Body = commentBodies[_random.Next(commentBodies.Length)],
                    CreatedAt = task.CreatedAt + _random.Next(10_000, 500_000)
                });
            }
        }
    }
}
