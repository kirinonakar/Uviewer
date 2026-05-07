using System;
using System.Threading;

namespace Uviewer.Models
{
    public sealed class WebDavState : IDisposable
    {
        private CancellationTokenSource? _operationCts;

        public string? CurrentPath { get; set; }
        public string? CurrentItemPath { get; set; }
        public bool IsWebDavMode { get; set; }

        public CancellationToken RestartOperation()
        {
            CancelOperation();
            _operationCts = new CancellationTokenSource();
            return _operationCts.Token;
        }

        public void CancelOperation()
        {
            if (_operationCts == null) return;

            _operationCts.Cancel();
            _operationCts.Dispose();
            _operationCts = null;
        }

        public void Dispose()
        {
            CancelOperation();
        }
    }
}
