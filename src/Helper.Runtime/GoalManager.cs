using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class GoalManager : IGoalManager
    {
        private readonly string _storagePath;
        private List<Goal> _goals = new();

        public GoalManager(string? storagePath = null)
        {
            _storagePath = string.IsNullOrWhiteSpace(storagePath)
                ? HelperWorkspacePathResolver.ResolveDataFilePath("current_goals.json")
                : Path.GetFullPath(storagePath);
            LoadGoals();
        }

        public Task<List<Goal>> GetGoalsAsync(bool includeCompleted = true, CancellationToken ct = default)
        {
            var snapshot = includeCompleted
                ? _goals.OrderByDescending(goal => goal.CreatedAt).ToList()
                : _goals.Where(goal => !goal.IsCompleted).OrderByDescending(goal => goal.CreatedAt).ToList();
            return Task.FromResult(snapshot);
        }

        public Task<List<Goal>> GetActiveGoalsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_goals.Where(g => !g.IsCompleted).OrderByDescending(g => g.CreatedAt).ToList());
        }

        public Task AddGoalAsync(string title, string description, CancellationToken ct = default)
        {
            _goals.Add(new Goal(Guid.NewGuid(), title, description, false, DateTime.Now));
            SaveGoals();
            return Task.CompletedTask;
        }

        public Task<bool> UpdateGoalAsync(Guid id, string title, string description, CancellationToken ct = default)
        {
            var goalIndex = _goals.FindIndex(goal => goal.Id == id);
            if (goalIndex < 0)
            {
                return Task.FromResult(false);
            }

            var current = _goals[goalIndex];
            _goals[goalIndex] = current with
            {
                Title = title,
                Description = description
            };
            SaveGoals();
            return Task.FromResult(true);
        }

        public Task<bool> DeleteGoalAsync(Guid id, CancellationToken ct = default)
        {
            var removed = _goals.RemoveAll(goal => goal.Id == id) > 0;
            if (removed)
            {
                SaveGoals();
            }

            return Task.FromResult(removed);
        }

        public Task<bool> MarkGoalCompletedAsync(Guid id, CancellationToken ct = default)
        {
            var goalIndex = _goals.FindIndex(goal => goal.Id == id);
            if (goalIndex < 0)
            {
                return Task.FromResult(false);
            }

            var goal = _goals[goalIndex];
            _goals[goalIndex] = goal with { IsCompleted = true };
            SaveGoals();
            return Task.FromResult(true);
        }

        private void LoadGoals()
        {
            if (File.Exists(_storagePath))
            {
                try
                {
                    _goals = JsonSerializer.Deserialize<List<Goal>>(File.ReadAllText(_storagePath)) ?? new();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[GoalManager] Failed to load goals from '{_storagePath}': {ex.Message}");
                    _goals = new();
                }
            }
        }

        private void SaveGoals()
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_storagePath, JsonSerializer.Serialize(_goals, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

