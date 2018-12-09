using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace SimpleClient
{
    class SimpleClient
    {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private StreamWriter _writer;
        private StreamReader _reader;

        public SimpleClient()
        {
            _tcpClient = new TcpClient();

        }

        public bool Connect(string ipAddress, int port)
        {
            try
            {
                _tcpClient.Connect(ipAddress, port);
                _stream = _tcpClient.GetStream();

                _writer = new StreamWriter(_stream);
                _reader = new StreamReader(_stream);
            }
            catch(Exception e)
            {
                Console.WriteLine("Exception: " + e.Message);
                return false;
            }
            return true;
        }

        public void Run()
        {
            string userInput;

            ProcessServerResponse();

            while((userInput = Console.ReadLine()) != null)
            {
                _writer.WriteLine(userInput);
                _writer.Flush();

                ProcessServerResponse();

                if(userInput == "end")
                {
                    break;
                }
            }
            _tcpClient.Close();
        }

        private void ProcessServerResponse()
        {
            Console.WriteLine("Server Says " + _reader.ReadLine());
            Console.WriteLine("");
        }
    }
}