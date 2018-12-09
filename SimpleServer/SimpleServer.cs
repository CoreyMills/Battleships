using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using Packets;

namespace SimpleServer
{
    struct Game
    {
        public int gameID;
        public int maxShips;
        public bool gameEnded;
        public bool gameStarted;
        public bool shipsPlaced;
        public bool nextPlayersTurn;
        public Client player1;
        public Client player2;

        public void Init()
        {
            gameID = 0;
            maxShips = 0;

            gameEnded = false;
            gameStarted = false;
            nextPlayersTurn = false;

            player1 = null;
            player2 = null;
        }

        public bool GameFull()
        {
            if (player1 != null && player2 != null)
            {
                return true;
            }
            return false;
        }

        public bool GameEmpty()
        {
            if (player1 == null && player2 == null)
            {
                return true;
            }
            return false;
        }

        public void Cleanup()
        {
            gameID = 0;
            maxShips = 0;

            gameEnded = false;
            gameStarted = false;
            shipsPlaced = false;
            nextPlayersTurn = false;

            if (player1 != null)
            {
                player1.CleanUp();
            }

            if (player2 != null)
            {
                player2.CleanUp();
            }
        }
    }

    [Serializable]
    class SimpleServer
    {
        private static BinaryFormatter _formatter;

        private TcpListener _tcpListener;

        private UdpClient _udpListener;
        private IPEndPoint _udpEndPoint;
        private Thread _udpThread;

        private static List<Client> _clientList;

        private static ConcurrentDictionary<int, Game> _gameList;

        Game _nextGame;
        string _nextGameLock;

        int _nextClientID;
        int _nextGameID;

        public SimpleServer(string ipAddress, int port)
        {
            IPAddress address = IPAddress.Parse(ipAddress);
            _tcpListener = new TcpListener(address, port);

            _udpListener = new UdpClient(4445);
            _udpEndPoint = new IPEndPoint(IPAddress.Any, 0);

            _formatter = new BinaryFormatter();

            _clientList = new List<Client>();
            _gameList = new ConcurrentDictionary<int, Game>();
            
            _nextGame = new Game();
            _nextGameLock = "Lock";

            _nextClientID = 0;
            _nextGameID = 0;
        }

        public void Start()
        {
            _tcpListener.Start();
            Console.WriteLine("Started Listening");

            _udpThread = new Thread(new ThreadStart(UDP_Listen));
            _udpThread.Start();

            _nextGameID = 1;

            //checking for connections loop
            do
            {
                Socket socket = _tcpListener.AcceptSocket();
                Client client = new Client(socket, _nextClientID);
                _clientList.Add(client);

                _nextClientID++;

                Console.WriteLine("Conection Accepted");

                Thread t = new Thread(new ParameterizedThreadStart(ClientMethod));
                t.Start(client);
            } while (true); //_clientList.ElementAt(0) != null);

            //Stop();
        }

        public void Stop()
        {
            _tcpListener.Stop();
            Console.WriteLine("Stopped Listening");
        }

        private void UDP_Listen()
        {
            while (true)
            {
                byte[] bytes = _udpListener.Receive(ref _udpEndPoint);
                try
                {
                    _formatter = new BinaryFormatter();

                    MemoryStream memStream = new MemoryStream(bytes);

                    Packet packet = _formatter.Deserialize(memStream) as Packet;
                    UDP_HandlePacket(_udpEndPoint, packet);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error: " + e.Message);
                }
                finally
                {
                }
            }
        }

        private void UDP_HandlePacket(IPEndPoint ipEndPoint, Packet packet)
        {
            switch(packet.packetType)
            {
                case PacketType.CHAT_MESSAGE:
                    string returnMessage = ((ChatMessagePacket)packet).message;
                    ChatMessagePacket chatPacket = new ChatMessagePacket(returnMessage);
                    UDP_Send(ipEndPoint, chatPacket);
                    break;
            }
        }

        private void UDP_Send(IPEndPoint endPoint, Packet packet)
        {
            using (MemoryStream memStream = new MemoryStream(100))
            {
                _formatter.Serialize(memStream, packet);

                Byte[] buffer = memStream.GetBuffer();

                _udpListener.Send(buffer, buffer.Length, _udpEndPoint);
            }
        }

        private void ClientMethod(Object clientObj)
        {
            Client client = (Client)clientObj;

            try
            {
                string joinMessage = "Welcome to this Chat Forum!" + '\n' + "--Type '//Help' if you need something--";
                ChatMessagePacket chatPacket = new ChatMessagePacket(joinMessage);
                client.Send(chatPacket);

                _formatter = new BinaryFormatter();

                int noOfIncomingBytes = 0;

                while ((noOfIncomingBytes = client._reader.ReadInt32()) != 0)
                {
                    byte[] bytes = client._reader.ReadBytes(noOfIncomingBytes);

                    MemoryStream memStream = new MemoryStream(bytes);

                    Packet packet = _formatter.Deserialize(memStream) as Packet;
                    TCP_HandlePacket(client, packet);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
            }
            finally
            {
                bool handled = false;
                if (!_nextGame.GameEmpty())
                {
                    if (_nextGame.player1._clientID == client._clientID)
                    {
                        _nextGame.Cleanup();
                        handled = true;
                    }
                }

                if(!handled)
                {
                    int tempI;
                    if ((tempI = GetGameIndex(client)) != -1)
                    {
                        Game thisGame = _gameList[tempI];

                        if (thisGame.gameStarted)
                        {
                            string message = "Game Message" + '\n' + "Your opponent was disconnected!" +
                                                '\n' + "You can now join a new game!";
                            GameMessagePacket gamePacket = new GameMessagePacket(message);
                            EndGamePacket endGame = new EndGamePacket(true);
                            if (client._playerNum == 1)
                            {
                                thisGame.player2.Send(gamePacket);
                                thisGame.player2.Send(endGame);
                            }
                            else if (client._playerNum == 2)
                            {
                                thisGame.player1.Send(gamePacket);
                                thisGame.player1.Send(endGame);
                            }
                        }

                        _gameList.TryRemove(tempI, out thisGame);
                        thisGame.Cleanup();
                        handled = true;
                    }
                }

                //remove client from the servers clientlist
                for(int i = 0; i < _clientList.Count(); i++)
                {
                    if (client._clientID == _clientList[i]._clientID)
                    {
                        _clientList.RemoveAt(i);

                        QuitGamePacket quitGame = new QuitGamePacket(true);
                        client.Send(quitGame);
                        client.Close();
                    }
                }
            }
        }

        private void TCP_HandlePacket(Client authorClient, Packet packet)
        {
            Game thisGame = new Game();
            string returnMessage;
            int tempI;

            for (int i = 0; i< _clientList.Count(); i++)
            {
                Client client = _clientList.ElementAt(i);

                switch (packet.packetType)
                {
                    case PacketType.CHAT_MESSAGE:
                        returnMessage = ((ChatMessagePacket)packet).message;
                        //checking if the client wants to speak to server
                        if (returnMessage.Contains("//"))
                        {
                            if (authorClient._clientID == client._clientID)
                            {
                                returnMessage = GetServerMessage(returnMessage);
                                ChatMessagePacket chatPacket = new ChatMessagePacket("SERVER " + '\n' + returnMessage);
                                client.Send(chatPacket);
                            }
                        }
                        //send clients message to all other clients
                        else if (authorClient._clientID != client._clientID 
                            && authorClient._gameID == client._gameID)
                        {
                            returnMessage = (authorClient._name + ": " + returnMessage);
                            ChatMessagePacket chatPacket = new ChatMessagePacket(returnMessage);
                            client.Send(chatPacket);
                        }
                        break;
                    case PacketType.DIRECT_MESSAGE:
                        string recipientName = ((DirectedMessagePacket)packet).recipientName;
                        if (recipientName == client._name)
                        {
                            returnMessage = "Directed Message" + '\n' + 
                                authorClient._name + ": " + ((DirectedMessagePacket)packet).message;
                            ChatMessagePacket chatPacket = new ChatMessagePacket(returnMessage);
                            client.Send(chatPacket);
                        }
                            break;
                    case PacketType.NICKNAME:
                        //set clients nickname
                        string nickname = ((NicknamePacket)packet).nickname;
                        authorClient._name = nickname;
                        break;
                    case PacketType.HIT_ATTEMPT:
                        if((tempI = GetGameIndex(authorClient)) != -1)
                        {
                            thisGame = _gameList[tempI];
                        }
                        //make sure all aspects of the game are set up
                        if (thisGame.gameStarted && thisGame.shipsPlaced)
                        {
                            char[] letters = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I' };

                            //make sure game has players
                            if (thisGame.player1 != null && thisGame.player2 != null)
                            {
                                //if its the authorClients turn then continue
                                if (thisGame.player1._myTurn && authorClient._playerNum == 1 ||
                                thisGame.player2._myTurn && authorClient._playerNum == 2)
                                {
                                    //if you shot set enemy panel
                                    if (authorClient._clientID == client._clientID)
                                    {
                                        //shooter is player1
                                        if (authorClient._playerNum == 1)
                                        {
                                            bool tempHit = false;
                                            int tempIndex = ((HitAttemptPacket)packet).index;

                                            for (int j = 0; j < thisGame.player2._myShips.Count(); j++)
                                            {
                                                if (thisGame.player2._myShips[j] == tempIndex)
                                                {
                                                    tempHit = true;
                                                    thisGame.player2._shipsHitCount++;
                                                }
                                            }

                                            int x = tempIndex % 10;
                                            int y = ((tempIndex - x) / 10) - 1;
                                            GameMessagePacket gamePacket = new GameMessagePacket("");

                                            if (tempHit)
                                            {
                                                gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                        "You hit an enemy battleship at position " + x.ToString() + letters[y];
                                                thisGame.player1.Send(gamePacket);

                                                gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                    "Your battleship at position " + x.ToString() + letters[y] + " has been hit";
                                                thisGame.player2.Send(gamePacket);
                                            }
                                            else
                                            {
                                                gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                    "You fired at position " + x.ToString() + letters[y] + " no battleships were hit";
                                                thisGame.player1.Send(gamePacket);

                                                gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                    "Enemy fired at position " + x.ToString() + letters[y] + " no battleships were hit";
                                                thisGame.player2.Send(gamePacket);
                                            }

                                            HitAttemptPacket hitPacket = new HitAttemptPacket(tempIndex, tempHit, true);
                                            client.Send(hitPacket);

                                        }
                                        //shooter is player2
                                        else if (authorClient._playerNum == 2)
                                        {
                                            bool tempHit = false;
                                            int tempIndex = ((HitAttemptPacket)packet).index;

                                            for (int j = 0; j < thisGame.player1._myShips.Count(); j++)
                                            {
                                                if (thisGame.player1._myShips[j] == tempIndex)
                                                {
                                                    tempHit = true;
                                                    thisGame.player1._shipsHitCount++;
                                                }
                                            }

                                            int x = tempIndex % 10;
                                            int y = ((tempIndex - x) / 10) - 1;
                                            GameMessagePacket gamePacket = new GameMessagePacket("");

                                            if (tempHit)
                                            {
                                                gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                        "Your battleship at position " + x.ToString() + letters[y] + " has been hit";
                                                thisGame.player1.Send(gamePacket);

                                                gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                    "You hit an enemy battleship at position " + x.ToString() + letters[y];
                                                thisGame.player2.Send(gamePacket);
                                            }
                                            else
                                            {
                                                gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                        "Enemy fired at " + x.ToString() + letters[y] + " no battleships were hit";
                                                thisGame.player1.Send(gamePacket);

                                                gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                    "You fired at position " + x.ToString() + letters[y] + " no battleships were hit";
                                                thisGame.player2.Send(gamePacket);
                                            }

                                            HitAttemptPacket hitPacket = new HitAttemptPacket(tempIndex, tempHit, true);
                                            client.Send(hitPacket);
                                        }
                                    }
                                    //if you didnt shoot, set friendly panel
                                    else if (authorClient._clientID != client._clientID &&
                                        thisGame.gameID == client._gameID)
                                    {
                                        bool tempHit = false;
                                        int tempIndex = ((HitAttemptPacket)packet).index;

                                        for (int j = 0; j < client._myShips.Count(); j++)
                                        {
                                            if (client._myShips[j] == tempIndex)
                                            {
                                                tempHit = true;
                                            }
                                        }

                                        HitAttemptPacket hitPacket = new HitAttemptPacket(tempIndex, tempHit, false);
                                        client.Send(hitPacket);
                                    }

                                    thisGame.nextPlayersTurn = true;
                                }
                                //check if the a player has won
                                if (!thisGame.gameEnded)
                                {
                                    if (thisGame.player1._shipsHitCount == thisGame.player1._myShips.Count ||
                                        thisGame.player2._shipsHitCount == thisGame.player2._myShips.Count && thisGame.player1._myShips.Count > 0 && thisGame.player2._myShips.Count > 0)
                                    {
                                        thisGame.gameEnded = true;
                                        UpdateClientsGame(GetGameIndex(authorClient), thisGame);
                                    }
                                }
                            }
                            //if a player was disconnected
                            else
                            {
                                string connectedMessage = "Your opponent was disconnected!";
                                string disconnectedMessage = "You were disconnected!";

                                GameMessagePacket connectPacket = new GameMessagePacket(connectedMessage);
                                GameMessagePacket disconnectPacket = new GameMessagePacket(disconnectedMessage);

                                if (thisGame.player1 == null)
                                {
                                    thisGame.player1.Send(disconnectPacket);
                                    thisGame.player2.Send(connectPacket);
                                }
                                else if (thisGame.player2 == null)
                                {
                                    thisGame.player2.Send(disconnectPacket);
                                    thisGame.player1.Send(connectPacket);
                                }

                                thisGame.gameEnded = true;
                            }
                        }
                        break;
                    case PacketType.SHIPS:
                        if(authorClient._clientID == client._clientID)
                        {
                            if ((tempI = GetGameIndex(authorClient)) != -1)
                            {
                                thisGame = _gameList[tempI];
                            }

                            if (!thisGame.shipsPlaced)
                            {
                                if (thisGame.gameStarted)
                                {
                                    if (client._playerNum == 1 && thisGame.player1._myShips.Count == 0 ||
                                        client._playerNum == 2 && thisGame.player2._myShips.Count == 0)
                                    {
                                        client._myShips = ((ShipsChosenPacket)packet).shipIndicies;
                                        thisGame.maxShips = ((ShipsChosenPacket)packet).shipIndicies.Count;
                                    }

                                    if (thisGame.player1._myShips.Count == thisGame.maxShips &&
                                       thisGame.player2._myShips.Count == thisGame.maxShips)
                                    {
                                        thisGame.shipsPlaced = true;
                                        UpdateClientsGame(GetGameIndex(authorClient), thisGame);

                                        GameMessagePacket gamePacket = new GameMessagePacket("");

                                        gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                                "Both players have positioned all their ships" + '\n' +
                                                                "It is your turn";
                                        thisGame.player1.Send(gamePacket);

                                        gamePacket.message = "Game " + thisGame.gameID.ToString() + '\n' +
                                                               "Both players have positioned all their ships" + '\n' +
                                                               "It is your opponents turn";
                                        thisGame.player2.Send(gamePacket);
                                    }
                                }
                                else
                                {
                                    GameMessagePacket gamePacket = new GameMessagePacket("Game " + '\n' + "Waiting for a another player...");
                                    client.Send(gamePacket);
                                }
                            }
                        }
                        break;
                    case PacketType.GAME:
                        if ((tempI = GetGameIndex(authorClient)) != -1)
                        {
                            thisGame = _gameList[tempI];
                        }
                        //send game message to all clients in game
                        if (client._gameID == thisGame.gameID 
                            && client._clientID != authorClient._clientID)
                        {
                            returnMessage = "Game Message" + '\n' + "Opponent: ";
                            returnMessage += ((GameMessagePacket)packet).message;

                            GameMessagePacket gamePacket = new GameMessagePacket(returnMessage);
                            client.Send(gamePacket);
                        }
                        break;
                    case PacketType.JOIN_GAME:
                        if (authorClient._clientID == client._clientID)
                        {
                            if (authorClient._gameID == 0)  
                            {
                                Console.WriteLine("makeGame");
                                thisGame = NewGame(client);

                                if(thisGame.player2 == null)
                                {
                                    GameMessagePacket chatPacket = new GameMessagePacket("GAME " + '\n' + "Waiting for a another player...");
                                    client.Send(chatPacket);
                                }
                                else
                                {
                                    returnMessage = "GAME " + thisGame.gameID.ToString() + '\n' + "Start Game" + '\n';
                                    string defualtName = "Anonymous";

                                    if (thisGame.player1._name == "" || thisGame.player1._name ==  null)
                                    {
                                        thisGame.player1._name = defualtName + thisGame.player1._clientID.ToString();

                                        NicknamePacket namePacket = new NicknamePacket(defualtName + 
                                                                                        thisGame.player1._clientID.ToString());
                                        thisGame.player1.Send(namePacket);
                                    }

                                    if (thisGame.player2._name == "" || thisGame.player2._name == null)
                                    {
                                        thisGame.player2._name = defualtName + thisGame.player2._clientID.ToString();

                                        NicknamePacket namePacket = new NicknamePacket(defualtName + 
                                                                                        thisGame.player2._clientID.ToString());
                                        thisGame.player2.Send(namePacket);
                                    }

                                    returnMessage += thisGame.player1._name;
                                    returnMessage += " VS ";
                                    returnMessage += thisGame.player2._name;

                                    GameMessagePacket gamePacket = new GameMessagePacket(returnMessage);

                                    thisGame.player1.Send(gamePacket);
                                    thisGame.player2.Send(gamePacket);

                                    gamePacket.message = "GAME " + '\n' + "Now position your battleships";
                                    thisGame.player1.Send(gamePacket);
                                    thisGame.player2.Send(gamePacket);
                                }
                            }
                            else
                            {
                                GameMessagePacket chatPacket = new GameMessagePacket("GAME " + '\n' + "You are currently in a game!");
                                client.Send(chatPacket);
                            }
                        }
                        break;
                    case PacketType.QUIT_GAME:
                        if(authorClient._clientID == client._clientID)
                        {
                            bool quit = ((QuitGamePacket)packet).quitGame;
                            if (quit)
                            {
                                throw new Exception("Client quit the game");
                            }
                        }
                        break;
                }
            }

            if (thisGame.gameStarted)
            {
                if (!thisGame.gameEnded)
                {
                    if (thisGame.nextPlayersTurn)
                    {
                        //flip each players turn bools
                        thisGame.player1._myTurn = !thisGame.player1._myTurn;
                        thisGame.player2._myTurn = !thisGame.player2._myTurn;
                    }
                }
                else
                {
                    //end the game
                    if (thisGame.player1._shipsHitCount == thisGame.player1._myShips.Count)
                    {
                        EndGameMessage(thisGame, thisGame.player2, thisGame.player1);
                    }
                    else if (thisGame.player2._shipsHitCount == thisGame.player2._myShips.Count)
                    {
                        EndGameMessage(thisGame, thisGame.player1, thisGame.player2);
                    }

                    thisGame.Cleanup();
                    _gameList.TryRemove(GetGameIndex(authorClient), out thisGame);
                }
            }
        }

        private string GetServerMessage(string code)
        {
            string[] subStrings = code.Split('/');
            int length = subStrings.Length - 1;

            string returnMessage = "";

            switch (subStrings[length].ToLower())
            {
                case "help":
                    returnMessage = "1. type '//End' to close client." + '\n'
                        + "2. type '//GetClients' to recieve list of clients in chat." + '\n'
                        + "3. type 'recipientName:your message' to send a message directly to a client (BE CAREFUL OF CAPS)." + '\n'
                        + "4. type '!!your message' to send a message using UDP." + '\n'
                        + "5. type '#your message' to send a message directly to your opponent in your current game" + '\n'
                        + "6. type '//Joke' to recieve a joke.";
                    return returnMessage;
                case "end":
                    returnMessage = "Bye";
                    return returnMessage;
                case "getclients":
                    foreach(Client client in _clientList)
                    {
                        if (client._name == null)
                        {
                            returnMessage += "Anonymous" + '\n';
                        }
                        else
                        {
                            returnMessage += client._name + '\n';
                        }
                    }
                    returnMessage += "Total: " + _clientList.Count();
                    return returnMessage;
                case "joke":
                    return "I hate Russian dolls, they're so full of themselves!";
                default:
                   return "No value for that request";
            }
        }

        private void UpdateClientsGame(int index, Game newGame)
        {
            _gameList[index] = newGame;
        }

        private int GetGameIndex(Client authorClient)
        {
            //problem here
            for(int i = 0; i< _gameList.Count; i++)
            {
                if(_gameList[i].gameID == authorClient._gameID)
                {
                    return i;
                }
            }
            return -1;
        }

        private Game NewGame(Client client)
        {
            lock (_nextGameLock)
            {
                if (_nextGame.GameEmpty())
                {
                    _nextGame = new Game();
                    _nextGame.gameID = _nextGameID;
                }

                if (_nextGame.player1 == null)
                {
                    client._gameID = _nextGameID;
                    client._playerNum = 1;
                    client._myTurn = true;
                    _nextGame.player1 = client;
                }
                else if (_nextGame.player2 == null)
                {
                    client._gameID = _nextGameID;
                    client._playerNum = 2;
                    client._myTurn = false;
                    _nextGame.player2 = client;
                    _nextGame.gameStarted = true;
                }

                Game temp = _nextGame;

                if (_nextGame.GameFull())
                {
                    _gameList.TryAdd(_gameList.Count, temp);
                    //_gameList.Add(temp);
                    _nextGame.Init();
                    _nextGameID++;
                }

                return temp;
            }
        }

        private void EndGameMessage(Game game, Client winner, Client loser)
        {
            string winnerMessage = "Game " + game.gameID.ToString() + '\n'
                + "You defeated " + loser._name + "!" + '\n' + '\n'
                + "You can now join a new game!";

            string loserMessage = "Game " + game.gameID.ToString() + '\n'
                + "You have been defeated by " + winner._name + "!" + '\n' + '\n'
                + "You can now join a new game!";

            GameMessagePacket gamePacket = new GameMessagePacket(winnerMessage);
            winner.Send(gamePacket);

            gamePacket.message = loserMessage;
            loser.Send(gamePacket);

            EndGamePacket endGame = new EndGamePacket(true);
            winner.Send(endGame);
            loser.Send(endGame);
        }
    }
}