using System;
using System.Globalization;

namespace Uviewer.Services
{
    internal static class WebDavServerAddress
    {
        public static bool TryParse(string address, int defaultPort, out string host, out int port)
        {
            host = "";
            port = defaultPort;
            string url = address.Trim();
            if (!url.Contains("://", StringComparison.Ordinal))
                url = "https://" + url;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
                uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.UserInfo.Length != 0)
                return false;

            // Keep the separate port field unless the pasted address includes a port.
            string authority = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..].TrimEnd('/');
            int separator = authority.LastIndexOf(':');
            if (separator >= 0 && int.TryParse(authority.AsSpan(separator + 1),
                NumberStyles.None, CultureInfo.InvariantCulture, out int explicitPort))
                port = explicitPort;

            if (port < 1 || port > 65535)
                return false;

            host = uri.Host;
            return true;
        }
    }
}
