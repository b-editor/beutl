using System.Net;
using System.Text;

namespace Beutl.UnitTests.TestInfrastructure;

public static class TestHttp
{
    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
