using System;
using System.Globalization;

namespace Uviewer
{
    public static class Strings
    {
        private static int? _langIndex;
        public static int LangIndex
        {
            get
            {
                if (!_langIndex.HasValue)
                {
                    string culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                    if (culture == "ko") _langIndex = 0;
                    else if (culture == "ja") _langIndex = 2;
                    else _langIndex = 1; // Default to English
                }
                return _langIndex.Value;
            }
        }

        private static string S(string ko, string en, string ja)
        {
            if (LangIndex == 0) return ko;
            if (LangIndex == 2) return ja;
            return en;
        }

        // Tooltips
        public static string ToggleSidebarTooltip => S("사이드바 토글(Ctrl+B)", "Toggle Sidebar (Ctrl+B)", "サイドバーの切り替え (Ctrl+B)");
        public static string OpenFileTooltip => S("파일 열기(Ctrl+O)", "Open File (Ctrl+O)", "ファイルを開く (Ctrl+O)");
        public static string OpenFolderTooltip => S("폴더 열기", "Open Folder", "フォルダを開く");
        public static string ZoomOutTooltip => S("축소(-)", "Zoom Out (-)", "縮小 (-)");
        public static string ZoomInTooltip => S("확대(+)", "Zoom In (+)", "拡大 (+)");
        public static string ZoomFitTooltip => S("맞춤", "Fit to Window", "ウィンドウに合わせる");
        public static string ZoomActualTooltip => S("원본 크기", "Actual Size", "実サイズ");
        public static string SharpenTooltip => S("샤프닝(S)", "Sharpen (S)", "シャープネス (S)");
        public static string SideBySideTooltip => S("좌우 보기(Space)", "Side by Side (Space)", "見開き表示 (Space)");
        public static string NextImageSideTooltip => S("다음 그림 위치", "Next Image Position", "次ページの配置");
        public static string PrevFileTooltip => S("이전 파일", "Previous File", "前のファイル");
        public static string NextFileTooltip => S("다음 파일", "Next File", "次のファイル");
        public static string PrevPageTooltip => S("이전 이미지/페이지", "Previous Image/Page", "前の画像/ページ");
        public static string NextPageTooltip => S("다음 이미지/페이지", "Next Image/Page", "次の画像/ページ");
        public static string AozoraTooltip => S("푸른하늘문고/md(A)", "Aozora/md(A)", "青空文庫/md (A)");
        public static string FontTooltip => S("폰트 변경(F)", "Change Font (F)", "フォント変更 (F)");
        public static string GoToPageTooltip => S("줄 이동(G)", "Go to Line (G)", "行移動 (G)");
        public static string TextSizeDownTooltip => S("글자 작게(-)", "Text Smaller (-)", "文字を小さく (-)");
        public static string TextSizeUpTooltip => S("글자 크게(+)", "Text Larger (+)", "文字を大きく (+)");
        public static string VerticalTooltip => S("세로쓰기(V)", "Vertical Mode (V)", "縦書き (V)");
        public static string ThemeTooltip => S("배경색 변경(B)", "Change Background (B)", "背景色変更 (B)");
        public static string FullscreenTooltip => S("전체화면(F11)", "Fullscreen (F11)", "フルスクリーン (F11)");
        public static string CloseWindowTooltip => S("닫기(Esc)", "Close (Esc)", "閉じる (Esc)");
        public static string ToggleViewTooltip => S("썸네일 보기", "Thumbnail View", "サムネイル表示");
        public static string ListViewTooltip => S("리스트 보기", "List View", "リスト表示");
        public static string ParentFolderTooltip => S("상위 폴더로 가기(Backspace)", "Up Level (Backspace)", "上のフォルダへ (Backspace)");
        public static string RecentTooltip => S("최근 파일", "Recent Files", "最近使ったファイル");
        public static string NoRecentFiles => S("최근 파일 없음", "No Recent Files", "履歴なし");
        public static string FavoritesTooltip => S("즐겨찾기(Ctrl+S)", "Favorites (Ctrl+S)", "お気に入り (Ctrl+S)");
        public static string NoFavorites => S("즐겨찾기 없음", "No Favorites", "お気に入りなし");
        public static string TocTooltip => S("목차", "Table of Contents", "目次");
        public static string TocTitle => S("목차", "Contents", "目次");
        public static string NoTocContent => S("목차 없음", "No Table of Contents", "目次なし");
        public static string BrowseFolderTooltip => S("폴더 찾아보기", "Browse Folder", "フォルダ参照");
        public static string SettingsTooltip => S("설정", "Settings", "設定");
        public static string LightModeTooltip => S("라이트 모드", "Light Mode", "ライトモード");
        public static string DarkModeTooltip => S("다크 모드", "Dark Mode", "ダークモード");
        public static string ChangeFont => S("폰트 변경", "Change Font", "フォント変更");
        public static string FontSelectionTitle => S("폰트 선택", "Font Selection", "フォント選択");
        public static string FontSearchPlaceholder => S("폰트 검색...", "Search Fonts...", "フォント検索...");

        public static string MatchControlDirection => S("컨트롤 방향 일치", "Match Control Direction", "コントロール方向を一致させる");
        public static string MatchControlDirectionTooltip => S("페이지 진행 방향에 따라 컨트롤 방향을 변경", "Change control direction based on page progression direction", "ページの進行方向に合わせてコントロール方向を変更する");

        public static string AllowMultipleInstances => S("다중 실행", "Allow Multiple Instances", "多重起動を許可する");
        public static string AllowMultipleInstancesTooltip => S("여러 개의 앱을 동시에 실행할 수 있게 함. 해제하면 이미 실행중인 창에서 이미지를 엽니다.", "Allows multiple instances of the app. If disabled, images will open in the already running instance.", "アプリの多重起動を許可します。オフの場合、起動中のウィンドウでファイルを開きます。");

        // UI Texts
        public static string CurrentPathPlaceholder => S("폴더를 선택하세요", "Select a folder", "フォルダを選択してください");
        public static string EmptyStateDrag => S("파일을 여기에 드래그하거나", "Drag files here or", "ファイルをここにドラッグするか");
        public static string EmptyStateClick => S("'열기' 버튼을 클릭하세요", "Click 'Open Button'", "「開く」ボタンをクリックしてください");
        public static string EmptyStateButton => S("파일 열기", "Open File", "ファイルを開く");
        
        public static string FastNavText => S("빠른 탐색 중...", "Fast Navigating...", "高速ナビゲート中...");
        public static string TextFastNavText => S("페이지 이동 중...", "Navigating...", "ページ移動中...");
        public static string CalculatingPages => S(" (페이지 계산중...)", " (Calculating pages...)", " (ページ計算中...)");
        public static string Paginating => S("페이지 계산 중...", "Paginating...", "ページ計算中...");
        public static string FileSelectPlaceholder => S("파일을 선택해주세요", "Select a file", "ファイルを選択してください");
        public static string LoadImageError => S("파일을 불러올 수 없습니다.", "Cannot load file.", "ファイルを読み込めません。");
        public static string AddedToFavoritesNotification => S("즐겨찾기에 추가되었습니다", "Added to Favorites", "お気に入りに追加されました");
        public static string Loading => S(" (로딩중...)", " (Loading...)", " (読み込み中...)");
        public static string LineInfo(int cur, int total) => S($"줄 {cur} / {total}", $"Line {cur} / {total}", $"行 {cur} / {total}");
        
        public static string EpubLoadError(string msg) => S($"EPUB 로드 실패: {msg}", $"EPUB Load Failed: {msg}", $"EPUB読み込み失敗: {msg}");
        public static string EpubParseError(string msg) => S($"EPUB 파싱 오류: {msg}", $"EPUB Parse Error: {msg}", $"EPUB解析エラー: {msg}");
        public static string EpubPageInfo(int p, int tp, int l, int tl, int c, int tc) => 
            S($"페이지 {p}/{tp} 줄 {l}/{tl} (챕터 {c}/{tc})", 
              $"Page {p}/{tp} Line {l}/{tl} (Ch.{c}/{tc})",
              $"ページ {p}/{tp} 行 {l}/{tl} (チャプター {c}/{tc})");

        // Dialogs & Menus
        public static string AddToFavorites => S("➕ 즐겨찾기 추가(Ctrl+S)", "➕ Add to Favorites (Ctrl+S)", "➕ お気に入りに追加 (Ctrl+S)");
        public static string DialogTitle => S("줄 이동", "Go to Line", "行移動");
        public static string DialogPrimary => S("이동", "Go", "移動");
        public static string DialogClose => S("취소", "Cancel", "キャンセル");

        // WebDAV
        public static string WebDavTooltip => S("webdav 연결", "WebDAV Connect", "WebDAV接続");
        public static string AddWebDavServer => S("➕ webdav 서버 추가", "➕ Add WebDAV Server", "➕ WebDAVサーバー追加");
        public static string WebDavServerName => S("서버 이름", "Server Name", "サーバー名");
        public static string WebDavAddress => S("주소", "Address", "アドレス");
        public static string WebDavPort => S("포트", "Port", "ポート");
        public static string WebDavId => S("ID", "ID", "ID");
        public static string WebDavPassword => S("비밀번호", "Password", "パスワード");
        public static string WebDavSave => S("저장", "Save", "保存");
        public static string WebDavCancel => S("취소", "Cancel", "キャンセル");
        public static string WebDavConnecting => S("연결 중...", "Connecting...", "接続中...");
        public static string WebDavConnectionFailed => S("연결 실패", "Connection Failed", "接続失敗");
    }
}
