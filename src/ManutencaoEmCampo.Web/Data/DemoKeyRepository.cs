using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace ManutencaoEmCampo.Web.Data;

internal sealed class DemoKeyRepository : IXmlRepository
{
    private readonly ConcurrentQueue<XElement> keys = new();

    public IReadOnlyCollection<XElement> GetAllElements() =>
        keys.Select(element => new XElement(element)).ToArray();

    public void StoreElement(XElement element, string friendlyName) =>
        keys.Enqueue(new XElement(element));
}
