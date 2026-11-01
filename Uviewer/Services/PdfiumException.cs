using System;

namespace Uviewer.Services;

internal enum PdfiumError
{
    Unknown = 1,
    CannotOpenFile = 2,
    InvalidFormat = 3,
    PasswordProtected = 4,
    UnsupportedSecurity = 5,
    PageNotFound = 6
}

internal sealed class PdfiumException(PdfiumError error) : Exception(error switch
{
    PdfiumError.CannotOpenFile => "PDFium could not open the PDF file.",
    PdfiumError.InvalidFormat => "The PDF file is invalid or corrupted.",
    PdfiumError.PasswordProtected => "The PDF password is missing or incorrect.",
    PdfiumError.UnsupportedSecurity => "The PDF uses an unsupported security scheme.",
    PdfiumError.PageNotFound => "The PDF page could not be loaded.",
    _ => "PDFium could not process the PDF."
})
{
    public PdfiumError Error { get; } = error;
}
