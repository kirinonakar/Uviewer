namespace Uviewer.Models
{
    // 이미지, 텍스트, 아카이브 항목 등을 나타내는 데이터 클래스
    public class ImageEntry
    {
        public string DisplayName { get; set; } = "";
        public string? FilePath { get; set; }
        public string? ArchiveEntryKey { get; set; }
        public bool IsArchiveEntry => ArchiveEntryKey != null;
        public bool IsPdfEntry { get; set; } = false;
        public uint PdfPageIndex { get; set; } = 0;
        public string? WebDavPath { get; set; }
        public bool IsWebDavEntry => WebDavPath != null;
    }
}
