using System.Security.Cryptography;
using System.Text;

namespace Coldstart.Application.Harvest;

public sealed record RawItem(string Source, string ExternalId, string Title, string Text,
    string Author, string Url, string CreatedAt)
{
    public string Fingerprint => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{Source}:{ExternalId}"))).ToLowerInvariant();
}
