using System;
using System.Collections.Generic;

namespace Uviewer.Models
{
    public class RecentItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Type { get; set; } = ""; // "Folder", "File", "Archive"
        public string? ArchiveEntryKey { get; set; }
        public DateTime AccessedAt { get; set; } = DateTime.Now;
        public double? ScrollOffset { get; set; }
        public int SavedPage { get; set; } = 0;
        public int ChapterIndex { get; set; } = 0;
        public int SavedLine { get; set; } = 1;
        public int SavedBlockIndex { get; set; } = -1;
        public bool IsWebDav { get; set; } = false;
        public string? WebDavServerName { get; set; }
        public bool IsVertical { get; set; } = false;
        public double Progress { get; set; } = 0; // 0-100 reading progress
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(List<RecentItem>))]
    public partial class RecentContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}
