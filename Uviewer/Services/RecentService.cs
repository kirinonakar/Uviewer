using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Uviewer.Models;

namespace Uviewer.Services
{
    public class RecentService
    {
        private ObservableCollection<RecentItem> _recentItems = new();
        public ReadOnlyObservableCollection<RecentItem> RecentItems { get; }

        public event EventHandler? RecentItemsUpdated;

        private const string RecentFilePath = "recent.json";
        private const int MaxRecentItems = 10;
        private string GetRecentFilePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Uviewer", RecentFilePath);

        public RecentService()
        {
            RecentItems = new ReadOnlyObservableCollection<RecentItem>(_recentItems);
        }

        public async Task LoadRecentItemsAsync()
        {
            try
            {
                var recentFile = GetRecentFilePath();
                var recentDir = Path.GetDirectoryName(recentFile);

                if (!string.IsNullOrEmpty(recentDir) && !Directory.Exists(recentDir))
                {
                    Directory.CreateDirectory(recentDir);
                }

                if (File.Exists(recentFile))
                {
                    var json = await File.ReadAllTextAsync(recentFile);
                    var items = System.Text.Json.JsonSerializer.Deserialize(json, typeof(List<RecentItem>), RecentContext.Default);
                    if (items != null)
                    {
                        _recentItems.Clear();
                        foreach (var item in (List<RecentItem>)items)
                        {
                            _recentItems.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recent items: {ex.Message}");
            }
        }

        public async Task SaveRecentItemsAsync()
        {
            try
            {
                var recentFile = GetRecentFilePath();
                var recentDir = Path.GetDirectoryName(recentFile);

                if (!string.IsNullOrEmpty(recentDir) && !Directory.Exists(recentDir))
                {
                    Directory.CreateDirectory(recentDir);
                }

                var json = System.Text.Json.JsonSerializer.Serialize(_recentItems.ToList(), typeof(List<RecentItem>), RecentContext.Default);
                await File.WriteAllTextAsync(recentFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving recent items: {ex.Message}");
            }
        }

        public async Task AddToRecentAsync(RecentItem newItem)
        {
            RecentItem? existing = _recentItems.FirstOrDefault(r =>
                r.Path.Equals(newItem.Path, StringComparison.OrdinalIgnoreCase) &&
                r.Type.Equals(newItem.Type, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                _recentItems.Remove(existing);
            }

            _recentItems.Insert(0, newItem);

            while (_recentItems.Count > MaxRecentItems)
            {
                _recentItems.RemoveAt(_recentItems.Count - 1);
            }

            await SaveRecentItemsAsync();
            RecentItemsUpdated?.Invoke(this, EventArgs.Empty);
        }

        public async Task RemoveRecentAsync(RecentItem recent)
        {
            if (_recentItems.Remove(recent))
            {
                await SaveRecentItemsAsync();
                RecentItemsUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        public RecentItem? FindExisting(string path, string type)
        {
            return _recentItems.FirstOrDefault(r =>
                r.Path.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                r.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }
    }
}
