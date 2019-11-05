using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            byte[] buffer = new byte[1024 * 1024];
            MemoryStream stream = new MemoryStream(buffer);

            Task.WaitAll(
            Task.Run(() =>
            {
                int offset = 0;
                while (true)
                {
                    var randomBuffer = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
                    stream.Write(randomBuffer, 0, randomBuffer.Length);
                    offset += randomBuffer.Length;
                    Thread.Sleep(10);
                }
            }),

            Task.Run(() =>
            {
                int offset = 0;
                while (true)
                {
                    Thread.Sleep(100);
                    var randomBuffer = new byte[32];
                    var result = stream.Read(randomBuffer, 0, randomBuffer.Length);
                    offset += randomBuffer.Length;
                }
            }));
        }
    }
}