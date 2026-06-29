using Uviewer.Services;

namespace Uviewer
{
    public sealed partial class MainWindow
    {
        private static partial class MainWindowComposition
        {
            private static class WebDavFeatureComposition
            {
                public static void Initialize(MainWindow window)
                {
                    window._webDavDocumentOpenCoordinator = new WebDavDocumentOpenCoordinator(new WebDavDocumentOpenHandlers
                    {
                        LoadFolderAsync = window.LoadWebDavFolderAsync,
                        CloseCurrentPdfAsync = window.CloseCurrentPdfAsync,
                        CloseCurrentEpubAsync = window.CloseCurrentEpubAsync,
                        CloseCurrentArchiveAsync = window.CloseCurrentArchiveAsync,
                        SetCurrentItemPath = path => window._currentWebDavItemPath = path,
                        ClearImageResources = window.ClearImageResources,
                        SetStatusText = text => window.FileNameText.Text = text,
                        CreateLoadingStatus = name => name + Strings.Loading,
                        CreateDownloadFailedStatus = () => "다운로드 실패",
                        CreateFileOpenFailedStatus = ex => $"파일 열기 실패: {ex.Message}",
                        CreateArchiveOpenFailedStatus = ex => $"압축 파일 열기 실패: {ex.Message}",
                        RestartOperation = window._webDavState.RestartOperation,
                        DownloadToTempFileAsync = window._webDavService.DownloadToTempFileAsync,
                        DownloadFileAsync = window._webDavService.DownloadFileAsync,
                        OpenLocalArchiveAsync = window.LoadImagesFromArchiveAsync,
                        OpenLocalPdfAsync = window.LoadImagesFromPdfAsync,
                        PrepareSequentialEntries = window.PrepareWebDavSequentialEntries,
                        OpenEpubFileAsync = window.LoadEpubFileAsync,
                        DisplayCurrentImageAsync = window.DisplayCurrentImageAsync,
                        StartPreload = window.StartWebDavPreload,
                        OpenArchiveStreamAsync = window.OpenWebDavArchiveStreamAsync,
                        Log = message => System.Diagnostics.Debug.WriteLine(message)
                    });
                }
            }
        }
    }
}
