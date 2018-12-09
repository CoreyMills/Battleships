namespace SimpleServer
{
    class Program
    {
        static void Main(string[] args)
        {
            SimpleServer _server = new SimpleServer("127.0.0.1", 4444);

            _server.Start();
            _server.Stop();
        }
    }
}
