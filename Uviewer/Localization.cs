using System;
using System.Globalization;

namespace Uviewer
{
    public static class Strings
    {
        private static bool? _isKorean;
        public static bool IsKorean
        {
            get
            {
                if (!_isKorean.HasValue)
                {
                    _isKorean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase);
                }
                return _isKorean.Value;
            }
        }

        private static string S(string ko, string en) => IsKorean ? ko : en;

        // Tooltips
        public static string ToggleSidebarTooltip => S("사이드바 토글(Ctrl+B)", "Toggle Sidebar (Ctrl+B)");
        public static string OpenFileTooltip => S("파일 열기(Ctrl+O)", "Open File (Ctrl+O)");
        public static string OpenFolderTooltip => S("폴더 열기", "Open Folder");
        public static string ZoomOutTooltip => S("축소(-)", "Zoom Out (-)");
        public static string ZoomInTooltip => S("확대(+)", "Zoom In (+)");
        public static string ZoomFitTooltip => S("맞춤", "Fit to Window");
        public static string ZoomActualTooltip => S("원본 크기", "Actual Size");
        public static string SharpenTooltip => S("샤프닝(S)", "Sharpen (S)");
        public static string SideBySideTooltip => S("좌우 보기(Space)", "Side by Side (Space)");
        public static string NextImageSideTooltip => S("다음 그림 위치", "Next Image Position");
        public static string PrevFileTooltip => S("이전 파일", "Previous File");
        public static string NextFileTooltip => S("다음 파일", "Next File");
        public static string PrevPageTooltip => S("이전 이미지/페이지", "Previous Image/Page");
        public static string NextPageTooltip => S("다음 이미지/페이지", "Next Image/Page");
        public static string AozoraTooltip => "Aozora/md(A)"; // Same
        public static string FontTooltip => S("폰트 변경(F)", "Change Font (F)");
        public static string GoToPageTooltip => S("줄 이동(G)", "Go to Line (G)");
        public static string TextSizeDownTooltip => S("글자 작게(-)", "Text Smaller (-)");
        public static string TextSizeUpTooltip => S("글자 크게(+)", "Text Larger (+)");
        public static string ThemeTooltip => S("배경색 변경(B)", "Change Background (B)");
        public static string FullscreenTooltip => S("전체화면(F11)", "Fullscreen (F11)");
        public static string CloseWindowTooltip => S("닫기(Esc)", "Close (Esc)");
        public static string ToggleViewTooltip => S("썸네일 보기", "Thumbnail View");
        public static string ListViewTooltip => S("리스트 보기", "List View");
        public static string ParentFolderTooltip => S("상위 폴더로 가기(Backspace)", "Up Level (Backspace)");
        public static string RecentTooltip => S("최근 파일", "Recent Files");
        public static string NoRecentFiles => S("최근 파일 없음", "No Recent Files");
        public static string FavoritesTooltip => S("즐겨찾기", "Favorites");
        public static string NoFavorites => S("즐겨찾기 없음", "No Favorites");
        public static string TocTooltip => S("목차", "Table of Contents");
        public static string TocTitle => S("목차", "Contents");
        public static string NoTocContent => S("목차 없음", "No Table of Contents");
        public static string BrowseFolderTooltip => S("폴더 찾아보기", "Browse Folder");

        // UI Texts
        public static string CurrentPathPlaceholder => S("폴더를 선택하세요", "Select a folder");
        public static string EmptyStateDrag => S("이미지를 여기에 드래그하거나", "Drag images here or");
        public static string EmptyStateClick => S("'열기' 버튼을 클릭하세요", "Click 'Open Button'");
        public static string EmptyStateButton => S("이미지 열기", "Open Image");
        
        public static string FastNavText => S("빠른 탐색 중...", "Fast Navigating...");
        public static string TextFastNavText => S("페이지 이동 중...", "Navigating...");
        public static string EpubFastNavText => S("챕터 이동 중...", "Navigating Chapter...");
        public static string CalculatingPages => S(" (페이지 계산중...)", " (Calculating pages...)");
        public static string FileSelectPlaceholder => S("이미지를 선택해주세요", "Select an image");
        public static string LoadImageError => S("이미지를 불러올 수 없습니다.", "Cannot load image.");
        
        public static string EpubLoadError(string msg) => S($"EPUB 로드 실패: {msg}", $"EPUB Load Failed: {msg}");
        public static string EpubParseError(string msg) => S($"EPUB 파싱 오류: {msg}", $"EPUB Parse Error: {msg}");
        public static string EpubPageInfo(int p, int tp, int l, int tl, int c, int tc) => 
            S($"페이지 {p}/{tp} 줄 {l}/{tl} (챕터 {c}/{tc})", 
              $"Page {p}/{tp} Line {l}/{tl} (Ch.{c}/{tc})");

        // Dialogs & Menus
        public static string AddToFavorites => S("➕ 즐겨찾기 추가", "➕ Add to Favorites");
        public static string DialogTitle => S("줄 이동", "Go to Line");
        public static string DialogPrimary => S("이동", "Go");
        public static string DialogClose => S("취소", "Cancel");
    }
}
