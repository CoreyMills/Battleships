using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Packets;

namespace SimpleServer
{
    class Client
    {
        private Socket _socket;
        private NetworkStream _stream;

        public BinaryWriter _writer { get; private set; }
        public BinaryReader _reader { get; private set; }

        public BinaryFormatter _formatter;

        public String _name { get; set; }
        public int _clientID { get; private set; }
        public int _gameID { get; set; }
        public int _playerNum { get; set; }
        public bool _myTurn { get; set; }
   
        public bool _closing { get; set; }

        public List<int> _myShips { get; set; }
        public int _shipsHitCount { get; set; }

        public Client(Socket socket, int clientID)
        {
            _socket = socket;
            _stream = new NetworkStream(_socket);

            _formatter = new BinaryFormatter(); 

            _writer = new BinaryWriter(_stream);
            _reader = new BinaryReader(_stream);

            _clientID = clientID;
            _gameID = 0;

            _closing = false;

            _myShips = new List<int>();
            _shipsHitCount = 0;
        }

        public void Send(Packet packet)
        {
            using (MemoryStream memStream = new MemoryStream(100))
            {
                _formatter.Serialize(memStream, packet);

                Byte[] buffer = memStream.GetBuffer();

                _writer.Write(buffer.Length);
                _writer.Write(buffer);
            }
        }

        public void CleanUp()
        {
            _myShips.Clear();
            _shipsHitCount = 0;
            _gameID = 0;
            _playerNum = 0;
            _myTurn = false;
        }

        public void Close()
        {
            _socket.Close();
        }
    }
}