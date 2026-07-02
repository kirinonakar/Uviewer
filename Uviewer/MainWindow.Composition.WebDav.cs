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
                        CloseCurrentPdfAsync = () => window._pdfDocumentController.CloseCurrentPdfAsync(),
                        CloseCurrentEpubAsync = () => window._epubReaderController.CloseCurrentEpubAsync(),
                        CloseCurrentArchiveAsync = () => window._archiveDocumentController.CloseCurrentArchiveAsync(),
                        SetCurrentItemPath = path => window._currentWebDavItemPath = path,
                        ClearImageResources = window._imageViewerController.ClearImageResources,
                        SetStatusText = text => window.FileNameText.Text = text,
                        CreateLoadingStatus = name => name + Strings.Loading,
                        CreateDownloadFailedStatus = () => "다운로드 실패",
                        CreateFileOpenFailedStatus = ex => $"파일 열기 실패: {ex.Message}",
                        CreateArchiveOpenFailedStatus = ex => $"압축 파일 열기 실패: {ex.Message}",
                        RestartOperation = window._webDavState.RestartOperation,
                        DownloadToTempFileAsync = window._webDavService.DownloadToTempFileAsync,
                        DownloadFileAsync = window._webDavService.DownloadFileAsync,
                        OpenLocalArchiveAsync = path => window._archiveDocumentController.LoadImagesFromArchiveAsync(path),
                        OpenLocalPdfAsync = path => window._pdfDocumentController.LoadImagesFromPdfAsync(path),
                        PrepareSequentialEntries = window.PrepareWebDavSequentialEntries,
                        OpenEpubFileAsync = (file, entry, token) => window._epubReaderController.LoadEpubFileAsync(file, entry, token),
                        DisplayCurrentImageAsync = window._imageViewerController.DisplayCurrentImageAsync,
                        StartPreload = window.StartWebDavPreload,
                        OpenArchiveStreamAsync = window.OpenWebDavArchiveStreamAsync,
                        Log = message => System.Diagnostics.Debug.WriteLine(message)
                    });
                }
            }
        }
    }
}
