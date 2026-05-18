using System;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Uviewer.Services
{
    public sealed class TextFileReaderService
    {
        public async Task<string> ReadAsync(StorageFile file, string encodingName)
        {
            var buffer = await FileIO.ReadBufferAsync(file);
            using var dataReader = DataReader.FromBuffer(buffer);
            byte[] bytes = new byte[buffer.Length];
            dataReader.ReadBytes(bytes);

            return Decode(bytes, encodingName);
        }

        public string Decode(byte[] bytes, string encodingName)
        {
            Encoding encoding = TextEncodingService.GetTextEncoding(bytes, encodingName);
            return encoding.GetString(bytes);
        }
    }
}
