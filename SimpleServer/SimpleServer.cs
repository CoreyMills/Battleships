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
        public ConcurrentDictionary<int, Client> spectatorList;

        public void Init()
        {
            gameID = 0;
            maxShips = 0;

            gameEnded = false;
            gameStarted = false;
            nextPlayersTurn = false;

            player1 = null;
            player2 = null;

            spectatorList = new ConcurrentDictionary<int, Client>();
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

            if(!spectatorList.IsEmpty)
            {
                for (int i = 0; i < spectatorList.Count; i++)
                {
                    spectatorList[i].CleanUp();
                }
                spectatorList.Clear();
            }

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

        private static ConcurrentDictionary<int, Client> _clientList;
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

            _clientList = new ConcurrentDictionary<int, Client>();
            _gameList = new ConcurrentDictionary<int, Game>();
            
            _nextGame = new Game();
            _nextGame.Init();
            _nextGameLock = "Lock";

            _nextClientID = 0;
            _nextGameID = 1;
        }

        public void Start()
        {
            _tcpListener.Start();
            Console.WriteLine("Started Listening");

            _udpThread = new Thread(new ThreadStart(UDP_Listen));
            _udpThread.Start();

            //checking for connections loop
            do
            {
                Socket socket = _tcpListener.AcceptSocket();
                Client client = new Client(socket, _nextClientID);
                _clientList.TryAdd(_clientList.Count(), client);

                _nextClientID++;

                Console.WriteLine("Conection Accepted");

                Thread t = new Thread(new ParameterizedThreadStart(ClientMethod));
                t.Start(client);
            } while (true);
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
                    //not tested
                    _udpListener.Close();
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
                if (!_nextGame.GameEmpty() &&
                    _nextGame.player1._clientID == client._clientID)
                {
                    _nextGame.Cleanup();
                    _nextGame.Init();
                    handled = true;
                }

                int tempI;
                if (!handled && (tempI = GetGameIndex(client)) != -1)
                {
                    Game thisGame = _gameList[tempI];

                    if (thisGame.gameStarted)
                    {
                        string message = "Game" + '\n' + "Your opponent was disconnected!" +
                                            '\n' + "You can now join or spectate a new game!";
                        string specMessage = "Game" + '\n' + client._name + " was disconnected!" +
                                                    '\n' + "You can now join or spectate a new game!";

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

                        for (int i = 0; i < thisGame.spectatorList.Count(); i++)
                        {
                            gamePacket.message = specMessage;
                            thisGame.spectatorList[i].Send(gamePacket);
                            thisGame.spectatorList[i].Send(endGame);
                        }
                    }

                    thisGame.Cleanup();
                    _gameList.TryRemove(tempI, out thisGame);
                    handled = true;
                }

                //remove client from the servers clientlist
                for (int i = 0; i < _clientList.Count(); i++)
                {
                    if (client._clientID == _clientList.ElementAt(i).Value._clientID)
                    {
                        client.Close();
                        _clientList.TryRemove(_clientList.ElementAt(i).Key, out client);
                        break;
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
                Client client = _clientList[i];

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
                                                    thisGame.player2._destroyedShips.Add(tempIndex);
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
                                                    thisGame.player1._destroyedShips.Add(tempIndex);
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

                                        if (!thisGame.spectatorList.IsEmpty)
                                        {
                                            if (thisGame.player1._clientID == client._clientID)
                                            {
                                                SpectatorHitPacket specHitPacket = new SpectatorHitPacket(tempIndex, tempHit, true);
                                                for (int j = 0; j < thisGame.spectatorList.Count; j++)
                                                {
                                                    thisGame.spectatorList[j].Send(specHitPacket);
                                                }
                                            }
                                            else if (thisGame.player2._clientID == client._clientID)
                                            {
                                                SpectatorHitPacket specHitPacket = new SpectatorHitPacket(tempIndex, tempHit, false);
                                                for (int j = 0; j < thisGame.spectatorList.Count; j++)
                                                {
                                                    thisGame.spectatorList[j].Send(specHitPacket);
                                                }
                                            }
                                        }
                                    }

                                    thisGame.nextPlayersTurn = true;
                                }
                                //check if the a player has won
                                if (!thisGame.gameEnded)
                                {
                                    if (thisGame.player1._destroyedShips.Count == thisGame.player1._myShips.Count ||
                                        thisGame.player2._destroyedShips.Count == thisGame.player2._myShips.Count && 
                                        thisGame.player1._myShips.Count > 0 && thisGame.player2._myShips.Count > 0)
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

                                    string specMessage = thisGame.player1._name + " has been disconnected from the game";
                                    GameMessagePacket specPacket = new GameMessagePacket(specMessage);
                                    
                                    for(int j = 0; j < thisGame.spectatorList.Count; j++)
                                    {
                                        thisGame.spectatorList[j].Send(specPacket);
                                    }
                                }
                                else if (thisGame.player2 == null)
                                {
                                    thisGame.player2.Send(disconnectPacket);
                                    thisGame.player1.Send(connectPacket);

                                    string specMessage = thisGame.player2._name + " has been disconnected from the game";
                                    GameMessagePacket specPacket = new GameMessagePacket(specMessage);

                                    for (int j = 0; j < thisGame.spectatorList.Count; j++)
                                    {
                                        thisGame.spectatorList[j].Send(specPacket);
                                    }
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
                                if (thisGame.gameID != 0)
                                {
                                    if (thisGame.gameStarted)
                                    {
                                        if (client._playerNum == 1 && thisGame.player1._myShips.Count == 0 ||
                                            client._playerNum == 2 && thisGame.player2._myShips.Count == 0)
                                        {
                                            client._myShips = ((ShipsChosenPacket)packet).shipIndices;
                                            thisGame.maxShips = ((ShipsChosenPacket)packet).shipIndices.Count;
                                        }

                                        if (thisGame.player1._myShips.Count == thisGame.maxShips &&
                                           thisGame.player2._myShips.Count == thisGame.maxShips)
                                        {
                                            thisGame.shipsPlaced = true;
                                            UpdateClientsGame(GetGameIndex(authorClient), thisGame);

                                            GameMessagePacket gamePacket = new GameMessagePacket("");

                                            gamePacket.message = "GAME " + thisGame.gameID.ToString() + '\n' +
                                                                    "Both players have positioned all their ships" + '\n' +
                                                                    "It is your turn";
                                            thisGame.player1.Send(gamePacket);

                                            gamePacket.message = "GAME " + thisGame.gameID.ToString() + '\n' +
                                                                   "Both players have positioned all their ships" + '\n' +
                                                                   "It is your opponents turn";
                                            thisGame.player2.Send(gamePacket);

                                            for(int j = 0; j < thisGame.spectatorList.Count; j++)
                                            {
                                                GameDataPacket  gameData = new GameDataPacket (thisGame.player1._myShips, thisGame.player2._myShips, null, null);
                                                thisGame.spectatorList[j].Send(gameData);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        GameMessagePacket gamePacket = new GameMessagePacket("GAME " + '\n' + "Waiting for a another player...");
                                        client.Send(gamePacket);
                                    }
                                }
                                else
                                {
                                    GameMessagePacket gamePacket = new GameMessagePacket("GAME " + '\n' + "You need to press the 'Join' button!");
                                    client.Send(gamePacket);
                                }
                            }
                        }
                        break;
                    case PacketType.GAME_MESSAGE:
                        if ((tempI = GetGameIndex(authorClient)) != -1)
                        {
                            thisGame = _gameList[tempI];
                        }
                        //send game message to all clients in game
                        if (client._gameID == thisGame.gameID 
                            && client._clientID != authorClient._clientID)
                        {
                            returnMessage = "GAME Message" + '\n' + "Opponent: ";
                            returnMessage += ((GameMessagePacket)packet).message;

                            GameMessagePacket gamePacket = new GameMessagePacket(returnMessage);
                            client.Send(gamePacket);
                        }
                        break;
                    case PacketType.CHALLENGE:
                        if(authorClient._clientID == client._clientID)
                        {
                            string authorMessage;
                            if (client._name != "" && client._name != null)
                            {
                                bool sent = false;
                                bool inGame = false;
                                string opponent = ((ChallengePacket)packet).chosenOpponent;
                                string message = ((ChallengePacket)packet).message;

                                for (int j = 0; j < _clientList.Count(); j++)
                                {
                                    if (_clientList[j]._name == opponent)
                                    {
                                        if (_clientList[j]._gameID == 0)
                                        {
                                            returnMessage = "Challenge " + '\n' + authorClient._name + " has challenged you to a game with the following message." +
                                                '\n' + authorClient._name + ": " + message;

                                            ChallengePacket challengePacket = new ChallengePacket(authorClient._name, returnMessage);
                                            _clientList[j].Send(challengePacket);
                                            sent = true;
                                        }
                                        else
                                        {
                                            inGame = true;
                                        }
                                    }
                                }

                                if (sent)
                                {
                                    authorMessage = "SERVER " + '\n' + "Your challenge was recieved.";
                                    ChatMessagePacket notifiyPacket = new ChatMessagePacket(authorMessage);
                                    authorClient.Send(notifiyPacket);
                                }
                                else if (!sent)
                                {
                                    authorMessage = "SERVER " + '\n' + "There is no challenger by that name. Be careful of caps.";
                                    ChatMessagePacket notifiyPacket = new ChatMessagePacket(authorMessage);
                                    authorClient.Send(notifiyPacket);
                                }
                                else if (inGame)
                                {
                                    authorMessage = "SERVER " + '\n' + "The challenger is already in a game.";
                                    ChatMessagePacket notifiyPacket = new ChatMessagePacket(authorMessage);
                                    authorClient.Send(notifiyPacket);
                                }
                            }
                            else
                            {
                                authorMessage = "SERVER " + '\n' + "You require a name in order to challenge another player.";
                                ChatMessagePacket notifiyPacket = new ChatMessagePacket(authorMessage);
                                authorClient.Send(notifiyPacket);
                            }
                        }
                        break;
                    case PacketType.ACCEPT_CHALLENGE:
                        if (authorClient._clientID == client._clientID)
                        {
                            string challenger = ((AcceptChallengePacket)packet).challenger;
                            bool accept = ((AcceptChallengePacket)packet).accept;

                            string authorMessage;
                            string challengerMessage;

                            Client challengerClient;

                            for (int j = 0; j < _clientList.Count(); j++)
                            {
                                if (_clientList[j]._name == challenger)
                                {
                                    challengerClient = _clientList[j];
                                    if (accept)
                                    {
                                        authorMessage = "Challenge " + '\n' + "You accepted the challenge.";
                                        ChatMessagePacket authorPacket = new ChatMessagePacket(authorMessage);
                                        authorClient.Send(authorPacket);

                                        challengerMessage = "Challenge " + '\n' + "Your challenge was accepted.";
                                        ChatMessagePacket challengerPacket = new ChatMessagePacket(challengerMessage);
                                        challengerClient.Send(challengerPacket);

                                        ////////////////////////////////////////////////
                                        
                                        Console.WriteLine("makeGame");
                                        thisGame = NewGame(challengerClient, authorClient);

                                        returnMessage = "GAME " + thisGame.gameID.ToString() + '\n' + "Start Game" + '\n';

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
                                    else
                                    {
                                        authorMessage = "Challenge " + '\n' + "Your declined the challenge.";
                                        ChatMessagePacket authorPacket = new ChatMessagePacket(authorMessage);
                                        authorClient.Send(authorPacket);

                                        challengerMessage = "Challenge " + '\n' + "Your challenge was declined.";
                                        ChatMessagePacket challengerPacket = new ChatMessagePacket(challengerMessage);
                                        challengerClient.Send(challengerPacket);
                                    }
                                }
                            }
                        }
                        break;
                    case PacketType.JOIN_GAME:
                        if (authorClient._clientID == client._clientID)
                        {
                            if (authorClient._gameID == 0 && !authorClient._spectating)  
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
                                if (_gameList[GetGameIndex(authorClient)].gameStarted)
                                { 
                                    GameMessagePacket chatPacket = new GameMessagePacket("GAME " + '\n' + "You are currently in a game!");
                                    client.Send(chatPacket);
                                }
                                else
                                {
                                    GameMessagePacket chatPacket = new GameMessagePacket("GAME " + '\n' + "You are waiting to be placed into a game.");
                                    client.Send(chatPacket);
                                }
                            }
                        }
                        break;
                    case PacketType.SPECTATING:
                        if (authorClient._clientID == client._clientID)
                        {
                            if (authorClient._gameID == 0 || authorClient._spectating)
                            {
                                if (((SpectatingPacket)packet).startSpectating)
                                {
                                    bool inGame = false;
                                    string playerMessage;
                                    string challenger = ((SpectatingPacket)packet).chosenClient;

                                    for (int j = 0; j < _clientList.Count(); j++)
                                    {
                                        if (_clientList[j]._name == challenger)
                                        {
                                            if ((tempI = GetGameIndex(_clientList[j])) != -1)
                                            {
                                                thisGame = _gameList[tempI];
                                                if (thisGame.GameFull())
                                                {
                                                    SpectatingPacket specPacket = new SpectatingPacket(true, thisGame.player1._name, thisGame.player2._name);
                                                    authorClient.Send(specPacket);

                                                    playerMessage = "Spectating" + '\n' + authorClient._name + ": is now spectating your game.";
                                                    ChatMessagePacket playersPacket = new ChatMessagePacket(playerMessage);
                                                    thisGame.player1.Send(playersPacket);
                                                    thisGame.player2.Send(playersPacket);

                                                    authorClient._gameID = thisGame.gameID;

                                                    thisGame.spectatorList.TryAdd(thisGame.spectatorList.Count, authorClient);

                                                    GameDataPacket  gameData = new GameDataPacket (thisGame.player1._myShips, thisGame.player2._myShips,
                                                                                    thisGame.player1._destroyedShips, thisGame.player2._destroyedShips);
                                                    authorClient.Send(gameData);

                                                    authorClient._spectating = true;
                                                    inGame = true;
                                                }
                                            }
                                            else
                                            {
                                                inGame = false;
                                            }
                                        }
                                    }

                                    if (!inGame)
                                    {
                                        string spectatorMessage = "SERVER: " + '\n' + "The chosen client isn't in a game.";
                                        ChatMessagePacket specPacket = new ChatMessagePacket(spectatorMessage);
                                        authorClient.Send(specPacket);
                                    }
                                }
                                else
                                {
                                    int index = GetGameIndex(authorClient);
                                    for (int j = 0; j < _gameList[index].spectatorList.Count; j++)
                                    {
                                        if (_gameList[index].spectatorList[j]._name == authorClient._name)
                                        {
                                            _gameList[index].spectatorList[j].CleanUp();
                                            _gameList[index].spectatorList.TryRemove(j, out authorClient);

                                            string spectatorMessage = "Spectating: " + '\n'
                                                + "You have stopped spectating Game" + _gameList[index].gameID + ".";
                                            ChatMessagePacket specPacket = new ChatMessagePacket(spectatorMessage);
                                            authorClient.Send(specPacket);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                returnMessage = "SERVER" + '\n' + "You are waiting to be placed into a game.";
                                ChatMessagePacket specPacket = new ChatMessagePacket(returnMessage);
                                authorClient.Send(specPacket);
                            }
                        }
                        break;
                    case PacketType.QUIT_ALL:
                        if(authorClient._clientID == client._clientID)
                        {
                            bool quit = ((QuitAllPacket)packet).quitAll;

                            if (quit)
                            {
                                client.Send(packet);
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
                    if (thisGame.player1._destroyedShips.Count == thisGame.player1._myShips.Count)
                    {
                        EndGameMessage(thisGame, thisGame.player2, thisGame.player1);
                    }
                    else if (thisGame.player2._destroyedShips.Count == thisGame.player2._myShips.Count)
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
                        + "5. type '#your message' to send a message directly to your opponent in your current game." + '\n'
                        + "6. type 'recipientName*yourmessage' to send another client a game challenge." + '\n'
                        + "7. type '&clientname' to start spectating the game that client is currently in." + '\n'
                        + "8. type '&leave' to stop spectating the current game you are watching."
                        + "9. type '//Joke' to recieve a joke." + '\n'
                        + "10. type '//help' to recieve this message again.";
                    return returnMessage;
                case "end":
                    returnMessage = "Bye";
                    return returnMessage;
                case "getclients":
                    for(int i = 0; i < _clientList.Count; i++)
                    {
                        if (_clientList[i]._name == null)
                        {
                            returnMessage += "Anonymous" + '\n';
                        }
                        else
                        {
                            returnMessage += _clientList[i]._name + '\n';
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
            for (int i = 0; i< _gameList.Count; i++)
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
                    _nextGame.spectatorList = new ConcurrentDictionary<int, Client>();
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
                    _nextGame.Init();
                    _nextGameID++;
                }

                return temp;
            }
        }

        private Game NewGame(Client player1, Client player2)
        {
            Game temp = new Game();
            temp.gameID = _nextGameID;

            //player1 vars
            player1._gameID = _nextGameID;
            player1._playerNum = 1;
            player1._myTurn = true;
            temp.player1 = player1;
            
            //player2 vars
            player2._gameID = _nextGameID;
            player2._playerNum = 2;
            player2._myTurn = false;
            temp.player2 = player2;

            temp.spectatorList = new ConcurrentDictionary<int, Client>();
            temp.gameStarted = true;

            _gameList.TryAdd(_gameList.Count, temp);
            _nextGameID++;

            return temp;
        }

        private void EndGameMessage(Game game, Client winner, Client loser)
        {
            EndGamePacket endGame = new EndGamePacket(true);

            string winnerMessage = "Game " + game.gameID.ToString() + '\n'
                + "You defeated " + loser._name + "!" + '\n' + '\n'
                + "You can now join a new game or spectate anothers game!";

            string loserMessage = "Game " + game.gameID.ToString() + '\n'
                + "You have been defeated by " + winner._name + "!" + '\n' + '\n'
                + "You can now join a new game or spectate anothers game!";

            GameMessagePacket gamePacket = new GameMessagePacket(winnerMessage);
            winner.Send(gamePacket);

            gamePacket.message = loserMessage;
            loser.Send(gamePacket);

            if(!game.spectatorList.IsEmpty)
            {
                string specMessage = "Game " + game.gameID.ToString() + '\n'
                    + winner._name + " has defeated " + loser._name + '\n'
                    + "You can now join a new game or spectate anothers game!";
                gamePacket.message = specMessage;

                for(int i = 0; i < game.spectatorList.Count; i++)
                {
                    game.spectatorList[i].Send(gamePacket);
                    game.spectatorList[i].Send(endGame);
                }
            }

            winner.Send(endGame);
            loser.Send(endGame);
        }
    }
}