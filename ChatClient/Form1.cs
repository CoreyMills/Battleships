using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Media;
using Packets;
using System.Security.Permissions;

namespace ChatClient
{
    public partial class Form1 : Form
    {
        private Socket _udpSocket;
        private IPEndPoint _udpEndPoint;
        private Thread _udpThread;

        private TcpClient _tcpClient;
        private NetworkStream _stream;

        private BinaryWriter _writer;
        private BinaryReader _reader;
        private BinaryFormatter _formatter;

        private StringVoidReturn _delChat;
        private UpdateGamePanels _delPanels;
        private SetName _delNewName;
        private FormClose _delClose;

        private Thread _serverResponse;

        private bool _closing = false;
        private bool _gameInProgress = false;
        private bool _previousServerMessage = true;
        private bool _resetPanels = true;

        private bool _spectating = false;
        public bool _challengeAccept;

        private List<int> _myShips;
        private int _shipsChosenCount = 0;
        private int _maxShips = 5;
        private bool _shipsChosen = false;
        private Image _safeShipI;
        private Image _hitShipI1;
        private Image _hitShipI2;
        private Image _noShipI;

        private DateTime _timeStart;
        private TimeSpan _timePassed;
        private float _frameCount = 0.0f;
        private float _delayAnim = 0.1f;
        private float _randtimeOffset;

        private delegate void StringVoidReturn(string value, bool serverMessage);
        private delegate void UpdateGamePanels(Packet packet);
        private delegate void SetName(string name);
        private delegate void FormClose();

        private Thread _challengeThread;

        public Form1()
        {
            _timeStart = DateTime.Now;

            InitializeComponent();
            _tcpClient = new TcpClient();

            _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            _formatter = new BinaryFormatter();
            _myShips = new List<int>();

            _safeShipI = Image.FromFile("../../Images/battleship_safe.png");
            _hitShipI1 = Image.FromFile("../../Images/battleship_hit1.png");
            _hitShipI2 = Image.FromFile("../../Images/battleship_hit2.png");
            _noShipI = Image.FromFile("../../Images/battleship_none.png");
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (Connect("127.0.0.1", 4444))
            {
                _delChat = new StringVoidReturn(UpdateChatWindow);
                _delPanels = new UpdateGamePanels(UpdatePanels);
                _delNewName = new SetName(UpdateNameTxt);
                _delClose = new FormClose(CloseForm);

                _challengeThread = new Thread(new ParameterizedThreadStart(ChallengeForm));

                _serverResponse = new Thread(new ThreadStart(TCP_ProcessServerResponse));
                _serverResponse.Start();

                _udpThread = new Thread(new ThreadStart(UDP_ProcessServerResponse));
                _udpThread.Start();

                //set all labels to use the handleLabel methods and send its index to the method ready to use
                for (int i = 0; i < friendPanel.Controls.Count; ++i)
                {
                    int tempIndex = i;
                    friendPanel.Controls[i].Click += (label, e1) => HandleLabelClick_Friend(label, e1, tempIndex);
                }

                for (int i = 0; i < enemyPanel.Controls.Count; ++i)
                {
                    int tempIndex = i;
                    enemyPanel.Controls[i].Click += (label, e1) => HandleLabelClick_Enemy(label, e1, tempIndex);
                }
                
                ResetPanels();
            }
            else
            {
                Console.WriteLine("Connection Failed");
            }
        }

        public bool Connect(string ipAddress, int port)
        {
            try
            {
                _tcpClient.Connect(ipAddress, port);
                _stream = _tcpClient.GetStream();

                _udpEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), 4445);
                _udpSocket.Connect(_udpEndPoint);

                _writer = new BinaryWriter(_stream);
                _reader = new BinaryReader(_stream);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message);
                return false;
            }
            return true;
        }

        [SecurityPermissionAttribute(SecurityAction.Demand, ControlThread = true)]
        public void Disconnect()
        {
            _writer.Write(0);
            _writer.Flush();

            _tcpClient.Dispose();
            _tcpClient.Close();
            _serverResponse.Abort();
            _challengeThread.Abort();
            _udpThread.Abort();
        }

        ////////TCP Methods////////
        private void TCP_ProcessServerResponse()
        {
            while (_tcpClient.Connected & !_closing)
            {
                try
                {
                    int noOfIncomingBytes = 0;  
                    while ((noOfIncomingBytes = _reader.ReadInt32()) > 0)
                    {
                        _closing = false;
                        byte[] bytes = _reader.ReadBytes(noOfIncomingBytes);
                        MemoryStream memStream = new MemoryStream(bytes);
                        Packet packet = _formatter.Deserialize(memStream) as Packet;

                        string message = "";
                        switch (packet.packetType)
                        {
                            case PacketType.CHAT_MESSAGE:
                                message = ((ChatMessagePacket)packet).message;
                                UpdateChatWindow(message, true);
                                if (message.ToLower() == "server " + '\n' + "bye")
                                {
                                    CloseForm();
                                }
                                break;
                            case PacketType.GAME_MESSAGE:
                                message = ((GameMessagePacket)packet).message;
                                UpdateChatWindow(message, true);
                                if(message == "GAME " + '\n' + "Now position your battleships")
                                {
                                    _gameInProgress = true;
                                }
                                break;
                            case PacketType.HIT_ATTEMPT:
                            case PacketType.SPECTATING_HIT:
                            case PacketType.GAME_DATA:
                                UpdatePanels(packet);
                                break;
                            case PacketType.END_GAME:
                                if (((EndGamePacket)packet).endGame)
                                {
                                    if (_spectating)
                                    {
                                        _spectating = false;
                                    }

                                    if(_gameInProgress)
                                    {
                                        _gameInProgress = false;
                                    }
                                    _resetPanels = true;
                                }
                                break;
                            case PacketType.CHALLENGE:
                                message = ((ChallengePacket)packet).message;
                                _challengeThread.Start(message);
                                break;
                            case PacketType.SPECTATING:
                                _resetPanels = true;
                                ResetPanels();

                                _spectating = true;
                                string player1 = ((SpectatingPacket)packet).chosenClient;
                                string player2 = ((SpectatingPacket)packet).clientsOpponent;

                                message = "Spectating" + '\n' + "You are spectating: " + '\n' +
                                    player1 + " VS " + player2 + '\n' + '\n' +
                                    "type '&leave' to stop spectating.";
                                UpdateChatWindow(message, true);
                                UpdatePanels(packet);
                                break;
                            case PacketType.QUIT_ALL:
                                _closing = true;
                                //Disconnect();
                                //CloseForm();
                                break;
                            case PacketType.NICKNAME:
                                UpdateNameTxt(((NicknamePacket)packet).nickname);
                                break;
                        }
                    }
                }
                catch (EndOfStreamException e)
                {
                    Console.WriteLine("Error writing data: {0}.", e.GetType().Name);
                }
                catch (ThreadAbortException e)
                {
                    Console.WriteLine("Error writing data: {0}.", e.GetType().Name);
                }
                catch(Exception e)
                {
                    if (!_closing)
                    {
                        UpdateChatWindow("ERROR: " + '\n' + "You have been disconnected." +
                            '\n' + "Restart the program and try again :D", false);
                        _closing = true;
                    }
                }
            }
            Disconnect();
            CloseForm();
        }

        private void TCP_Send(Packet packet)
        {
            if (_tcpClient.Connected)
            {
                using (MemoryStream memStream = new MemoryStream(255))
                {
                    try
                    {

                        _formatter.Serialize(memStream, packet);

                        Byte[] buffer = memStream.GetBuffer();

                        _writer.Write(buffer.Length);
                        _writer.Write(buffer);
                    }
                    catch (SerializationException e)
                    {
                        Console.WriteLine("Failed to serialize. Reason: " + e.Message);
                        throw;
                    }
                }
            }
        }

        ////////UDP Methods////////
        private void UDP_ProcessServerResponse()
        {
            byte[] bytes = new byte[256];
            int noOfIncomingBytes;
            while ((noOfIncomingBytes = _udpSocket.Receive(bytes)) != 0)
            {
                MemoryStream memStream = new MemoryStream(bytes);

                Packet packet = _formatter.Deserialize(memStream) as Packet;

                switch (packet.packetType)
                {
                    case PacketType.CHAT_MESSAGE:
                        string message = ((ChatMessagePacket)packet).message;
                        UpdateChatWindow(message, true);
                        break;
                    default:
                        break;
                }
            }
        }

        private void UDP_Send(Packet packet)
        {
            using (MemoryStream memStream = new MemoryStream(100))
            {
                _formatter.Serialize(memStream, packet);

                Byte[] buffer = memStream.GetBuffer();

                _udpSocket.Send(buffer);
            }
        }

        ////////Challenge Form Methods////////
        private void ChallengeForm(object message)
        {
            string returnMessage = (string)message;
            string[] subStrings = returnMessage.Split('\n');
            returnMessage = subStrings[subStrings.Length - 1];

            ChallengeForm challengeForm = new ChallengeForm(this);
            challengeForm.challengeMessage = returnMessage.Trim();
            Application.Run(challengeForm);
        
            subStrings = subStrings[subStrings.Length - 1].Split(':');

            AcceptChallengePacket packet = new AcceptChallengePacket(_challengeAccept, subStrings[0]);
            TCP_Send(packet);
        
            Console.WriteLine(_challengeAccept.ToString());
        }

        ////////Form Methods////////
        private TimeSpan GetDeltaTime()
        {
            TimeSpan deltaTime = DateTime.Now - _timeStart;
            deltaTime -= _timePassed;
            _timePassed = DateTime.Now - _timeStart;
            return deltaTime;
        }

        private void Update(object sender, EventArgs e)
        {
            TimeSpan deltaTime = GetDeltaTime();
            Random _rand = new Random();

            _frameCount = _frameCount + ((float)deltaTime.TotalSeconds);
            if(_frameCount > _delayAnim)
            {
                for (int i = 0; i < enemyPanel.Controls.Count; i++)
                {
                    _randtimeOffset = (_rand.Next(1, 7)) / 10;

                    if (enemyPanel.Controls[i].BackgroundImage == _hitShipI1)
                    {
                        enemyPanel.Controls[i].BackgroundImage = _hitShipI2;
                    }
                    else if(enemyPanel.Controls[i].BackgroundImage == _hitShipI2)
                    {
                        enemyPanel.Controls[i].BackgroundImage = _hitShipI1;
                    }
                }

                for (int i = 0; i < friendPanel.Controls.Count; i++)
                {
                    _randtimeOffset = (_rand.Next(1, 7)) / 10;

                    if (friendPanel.Controls[i].BackgroundImage == _hitShipI1)
                    {
                        friendPanel.Controls[i].BackgroundImage = _hitShipI2;
                    }
                    else if (friendPanel.Controls[i].BackgroundImage == _hitShipI2)
                    {
                        friendPanel.Controls[i].BackgroundImage = _hitShipI1;
                    }
                }

                _frameCount = 0.0f;
            }
        }
        
        private void UpdateChatWindow(string message, bool serverMessage)
        {
            if (InvokeRequired)
            {
                Invoke(_delChat, message, serverMessage);
            }
            else
            {
                if (_previousServerMessage)
                {
                    //updating with other clients message
                    messageList.Text += ('\n' + message + '\n');
                }
                else
                {
                    if (serverMessage)
                    {
                        //updating with other clients message
                        messageList.Text += ('\n' + message + '\n');
                    }
                    else
                    {
                        //updating with this clients message
                        messageList.Text += (message + '\n');
                    }
                }

                messageList.SelectionStart = messageList.Text.Length;
                messageList.ScrollToCaret();

                _previousServerMessage = serverMessage;
            }
        }

        private void UpdatePanels(Packet packet)
        {
            if (InvokeRequired)
            {
                Invoke(_delPanels, packet);
            }
            else
            {
                if (_spectating)
                {
                    switch (packet.packetType)
                    {
                        case PacketType.SPECTATING:
                            string player1 = ((SpectatingPacket)packet).chosenClient;
                            string player2 = ((SpectatingPacket)packet).clientsOpponent;
                            textBox1.Text = player1;
                            textBox2.Text = player2;

                            AccessibleRole accessibleRole1 = AccessibleRole.ColumnHeader;
                            AccessibleRole accessibleRole2 = AccessibleRole.RowHeader;
                            for (int i = 0; i < enemyPanel.Controls.Count; i++)
                            {
                                if (accessibleRole1 != enemyPanel.Controls[i].AccessibleRole &&
                                accessibleRole2 != enemyPanel.Controls[i].AccessibleRole)
                                {
                                    enemyPanel.Controls[i].Text = "";
                                    enemyPanel.Controls[i].BackColor = Color.GreenYellow;
                                    enemyPanel.Controls[i].BackgroundImage = _noShipI;
                                }
                            }
                            break;
                        case PacketType.GAME_DATA:
                            List<int> temp;
                            if ((temp = ((GameDataPacket )packet).p1Ships) != null)
                            {
                                for (int i = 0; i < temp.Count; i++)
                                {
                                    friendPanel.Controls[temp[i]].Text = "";
                                    friendPanel.Controls[temp[i]].BackColor = Color.White;
                                    friendPanel.Controls[temp[i]].BackgroundImage = _safeShipI;
                                }
                            }
                            if ((temp = ((GameDataPacket )packet).p2Ships) != null)
                            {
                                for (int i = 0; i < temp.Count; i++)
                                {
                                    enemyPanel.Controls[temp[i]].Text = "";
                                    enemyPanel.Controls[temp[i]].BackColor = Color.White;
                                    enemyPanel.Controls[temp[i]].BackgroundImage = _safeShipI;
                                }
                            }
                            if ((temp = ((GameDataPacket )packet).p1DeadShips) != null)
                            {
                                for (int i = 0; i < temp.Count; i++)
                                {
                                    friendPanel.Controls[temp[i]].Text = "";
                                    friendPanel.Controls[temp[i]].BackColor = Color.Red;
                                    friendPanel.Controls[temp[i]].BackgroundImage = _hitShipI1;
                                }
                            }
                            if ((temp = ((GameDataPacket )packet).p2DeadShips) != null)
                            {
                                for (int i = 0; i < temp.Count; i++)
                                {
                                    enemyPanel.Controls[temp[i]].Text = "";
                                    enemyPanel.Controls[temp[i]].BackColor = Color.Red;
                                    enemyPanel.Controls[temp[i]].BackgroundImage = _hitShipI1;
                                }
                            }
                            break;
                        case PacketType.SPECTATING_HIT:
                            int index = ((SpectatorHitPacket)packet).index;
                            bool hit = ((SpectatorHitPacket)packet).hit;
                            bool p1Turn = ((SpectatorHitPacket)packet).player1Turn;

                            if (p1Turn)
                            {
                                if (hit)
                                {
                                    friendPanel.Controls[index].Text = "";
                                    friendPanel.Controls[index].BackColor = Color.Red;
                                    friendPanel.Controls[index].BackgroundImage = _hitShipI1;
                                }
                                else
                                {
                                    friendPanel.Controls[index].Text = "";
                                    friendPanel.Controls[index].BackColor = Color.White;
                                    friendPanel.Controls[index].BackgroundImage = _noShipI;
                                }
                            }
                            else
                            {
                                if (hit)
                                {
                                    enemyPanel.Controls[index].Text = "";
                                    enemyPanel.Controls[index].BackColor = Color.Red;
                                    enemyPanel.Controls[index].BackgroundImage = _hitShipI1;
                                }
                                else
                                {
                                    enemyPanel.Controls[index].Text = "";
                                    enemyPanel.Controls[index].BackColor = Color.White;
                                    enemyPanel.Controls[index].BackgroundImage = _noShipI;
                                }
                            }
                            break;
                    }
                }
                else
                {
                    if (_resetPanels)
                    {
                        _resetPanels = false;
                        AccessibleRole accessibleRole1 = AccessibleRole.ColumnHeader;
                        AccessibleRole accessibleRole2 = AccessibleRole.RowHeader;

                        textBox1.Text = "Friendly Battleships";
                        textBox2.Text = "Enemy Battleships";

                        for (int i = 0; i < enemyPanel.Controls.Count; i++)
                        {
                            if (accessibleRole1 != enemyPanel.Controls[i].AccessibleRole &&
                            accessibleRole2 != enemyPanel.Controls[i].AccessibleRole)
                            {
                                enemyPanel.Controls[i].Text = "?";
                                enemyPanel.Controls[i].BackColor = Color.GreenYellow;
                                enemyPanel.Controls[i].BackgroundImage = null;
                            }
                        }

                        for (int i = 0; i < friendPanel.Controls.Count; i++)
                        {
                            if (accessibleRole1 != friendPanel.Controls[i].AccessibleRole &&
                            accessibleRole2 != friendPanel.Controls[i].AccessibleRole)
                            {
                                friendPanel.Controls[i].BackColor = Color.DeepSkyBlue;
                                friendPanel.Controls[i].BackgroundImage = _noShipI;
                            }
                        }
                    }
                    else
                    {
                        int index = ((HitAttemptPacket)packet).index;
                        bool hit = ((HitAttemptPacket)packet).hit;
                        bool myTurn = ((HitAttemptPacket)packet).myTurn;

                        SoundPlayer explosionSound = new SoundPlayer("../../SoundEffects/Explosion.wav");

                        if (myTurn)
                        {
                            if (hit)
                            {
                                enemyPanel.Controls[index].Text = "";
                                enemyPanel.Controls[index].BackColor = Color.Red;
                                enemyPanel.Controls[index].BackgroundImage = _hitShipI1;

                                explosionSound.Play();
                            }
                            else
                            {
                                enemyPanel.Controls[index].Text = "";
                                enemyPanel.Controls[index].BackColor = Color.White;
                                enemyPanel.Controls[index].BackgroundImage = _noShipI;
                            }
                        }
                        else
                        {
                            if (hit)
                            {
                                friendPanel.Controls[index].Text = "";
                                friendPanel.Controls[index].BackColor = Color.Red;
                                friendPanel.Controls[index].BackgroundImage = _hitShipI1;

                                explosionSound.Play();
                            }
                            else
                            {
                                friendPanel.Controls[index].Text = "";
                                friendPanel.Controls[index].BackColor = Color.White;
                                friendPanel.Controls[index].BackgroundImage = _noShipI;
                            }
                        }
                    }
                }
            }
        }

        private void UpdateNameTxt(string name)
        {
            if (InvokeRequired)
            {
                Invoke(_delNewName, name);
            }
            else
            {
                txtClientName.Text = name;
            }
        }

        private void button1_MouseClick(object sender, MouseEventArgs e)
        {
            string clientText = txtNewMessage.Text;
            txtNewMessage.Text = "";

            if (clientText.TrimEnd().ToCharArray().Length != 0)
            {
                if(clientText.Contains("*"))
                {
                    if (_gameInProgress)
                    {
                        string message = "GAME " + '\n' + "You are currently in a game!";
                        UpdateChatWindow(message, false);
                    }
                    else if(_spectating)
                    {
                        string message = "GAME " + '\n' + "You are currently spectating a game!";
                        UpdateChatWindow(message, false);
                    }
                    else
                    {
                        string[] subStrings = clientText.Split('*');

                        clientText = subStrings[1];
                        string chosenOpponent = subStrings[0];

                        ChallengePacket packet = new ChallengePacket(chosenOpponent, clientText);
                        TCP_Send(packet);

                        string message = "Challenge" + '\n' + "You: " + chosenOpponent + "*" + clientText;
                        UpdateChatWindow(message, false);
                    }
                }
                else if(clientText.Contains("&"))
                {
                    if (_gameInProgress)
                    {
                        string message = "GAME " + '\n' + "You are currently in a game!";
                        UpdateChatWindow(message, false);
                    }
                    else
                    {
                        string[] subStrings = clientText.Split('&');
                        string chosenClient = subStrings[1];

                        if (_spectating)
                        {
                            if (chosenClient.Trim().ToLower() == "leave")
                            {
                                SpectatingPacket packet = new SpectatingPacket(false, null, null);
                                TCP_Send(packet);

                                string message = "Spectating" + '\n' + "You: " + "&" + chosenClient;
                                UpdateChatWindow(message, false);

                                _spectating = false;
                                _resetPanels = true;
                                ResetPanels();
                            }
                            else
                            {
                                string message = "Spectating " + '\n' + "You are currently spectating a game!";
                                UpdateChatWindow(message, false);
                            }
                        }
                        else
                        {
                            if (chosenClient.Trim().ToLower() == "leave")
                            {
                                string message = "Spectating " + '\n' + "You aren't currently spectating a game!";
                                UpdateChatWindow(message, false);
                            }
                            else
                            {
                                SpectatingPacket packet = new SpectatingPacket(true, chosenClient, null);
                                TCP_Send(packet);

                                string message = "Spectating" + '\n' + "You: " + "&" + chosenClient;
                                UpdateChatWindow(message, false);
                            }
                        }
                    }
                }
                else if (clientText.Contains(":"))
                {
                    string[] subStrings = clientText.Split(':');

                    clientText = subStrings[1];
                    string recipientName = subStrings[0];

                    DirectedMessagePacket packet = new DirectedMessagePacket(clientText, recipientName);
                    TCP_Send(packet);

                    string message = "Directed Message" + '\n' + "You: " + clientText;
                    UpdateChatWindow(message, false);
                }
                else if (clientText.Contains("!!"))
                {
                    string[] subStrings = clientText.Split('!');
                    int length = subStrings.Length - 1;

                    clientText = subStrings[length];

                    ChatMessagePacket packet = new ChatMessagePacket(clientText);
                    UDP_Send(packet);

                    string message = "You: " + clientText;

                    UpdateChatWindow(message, false);
                }
                else if (clientText.Contains("#"))
                {
                    string[] subStrings = clientText.Split('#');

                    clientText = subStrings[1];

                    GameMessagePacket packet = new GameMessagePacket(clientText);
                    TCP_Send(packet);

                    string message = "Game Message" + '\n' + "You: " + clientText;
                    UpdateChatWindow(message, false);
                }
                else
                {
                    ChatMessagePacket packet = new ChatMessagePacket(clientText);
                    TCP_Send(packet);

                    string message = "You: " + clientText;

                    UpdateChatWindow(message, false);
                }
            }
        }

        private void txtClientName_TextChanged(object sender, EventArgs e)
        {
            string clientText = txtClientName.Text;

            NicknamePacket packet = new NicknamePacket(clientText);
            TCP_Send(packet);
        }

        private void txtClientName_Click(object sender, EventArgs e)
        {
            txtClientName.Text = "";
        }

        private void HandleLabelClick_Friend(object sender, EventArgs e, int index)
        {
            if (_spectating)
            {
                string message = "GAME " + '\n' + "You are currently spectating a game";
                UpdateChatWindow(message, false);
            }
            else
            {
                if (_gameInProgress)
                {
                    if (_shipsChosenCount < _maxShips && !this._shipsChosen)
                    {
                        Control instigatorControl = (Control)sender;

                        AccessibleRole accessibleRole1 = AccessibleRole.ColumnHeader;
                        AccessibleRole accessibleRole2 = AccessibleRole.RowHeader;

                        if (accessibleRole1 != instigatorControl.AccessibleRole &&
                            accessibleRole2 != instigatorControl.AccessibleRole)
                        {
                            bool sameShip = false;
                            for (int i = 0; i < _myShips.Count; i++)
                            {
                                if (index == _myShips[i])
                                {
                                    sameShip = true;
                                }
                            }

                            if (!sameShip)
                            {
                                instigatorControl.BackgroundImage = _safeShipI;

                                _myShips.Add(index);
                                _shipsChosenCount++;

                                UpdateChatWindow("You have " + (_maxShips - _shipsChosenCount).ToString() + " battleships left to position.", false);
                            }
                            else
                            {
                                UpdateChatWindow("You already have a ship at that position, choose a different position", false);
                            }
                        }
                    }

                    if (_shipsChosenCount == _maxShips)
                    {
                        _shipsChosen = true;
                        ShipsChosenPacket ships = new ShipsChosenPacket(_myShips);
                        TCP_Send(ships);

                        _myShips.Clear();
                    }
                }
                else
                {
                    UpdateChatWindow("GAME " + '\n' + "You need to be in a game, before you can position your battleships!", false);
                }
            }
        }

        private void HandleLabelClick_Enemy(object sender, EventArgs e, int index)
        {
            if (_spectating)
            {
                string message = "GAME " + '\n' + "You are currently spectating a game";
                UpdateChatWindow(message, false);
            }
            else
            {
                Control instigatorControl = (Control)sender;
                AccessibleRole accessibleRole1 = AccessibleRole.ColumnHeader;
                AccessibleRole accessibleRole2 = AccessibleRole.RowHeader;

                if (accessibleRole1 != instigatorControl.AccessibleRole &&
                    accessibleRole2 != instigatorControl.AccessibleRole)
                {
                    HitAttemptPacket packet = new HitAttemptPacket(index, false, true);
                    TCP_Send(packet);
                }
            }
        }

        private void JoinButton_Click(object sender, EventArgs e)
        {
            if (_spectating)
            {
                string message = "GAME " + '\n' + "You are currently spectating a game";
                UpdateChatWindow(message, false);
            }
            else
            {
                JoinGamePacket newGame = new JoinGamePacket(true);
                TCP_Send(newGame);

                if (_resetPanels)
                {
                    ResetPanels();
                }
            }
        }

        private void ResetPanels()
        {
            _shipsChosenCount = 0;
            _shipsChosen = false;

            Packet fakePacket = new Packet();
            UpdatePanels(fakePacket);
        }

        private void CloseForm()
        {
            if (InvokeRequired)
            {
                Invoke(_delClose);
            }
            else
            {
                this.Close();
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            QuitAllPacket quitPacket = new QuitAllPacket(true);
            TCP_Send(quitPacket);
        }

        private void backgroundWorker1_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {

        }
    }
}