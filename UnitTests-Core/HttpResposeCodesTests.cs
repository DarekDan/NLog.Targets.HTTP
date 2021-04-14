using System.Net;
using System.Net.Http;
using NUnit.Framework;

namespace UnitTests_Core
{
    public class HttpResposeCodesTests
    {
        
        [Test]
        public void Test429()
        {
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage(){StatusCode = HttpStatusCode.TooManyRequests};
            Assert.True(httpResponseMessage.StatusCode == HttpStatusCode.TooManyRequests);
            Assert.True((int)httpResponseMessage.StatusCode == 429);
        }
    }
}