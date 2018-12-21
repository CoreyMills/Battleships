using System;
using System.Collections.Generic;

namespace Packets
{
    public enum PacketType
    {
        EMPTY = 0,
        NICKNAME,
        CHAT_MESSAGE,
        DIRECT_MESSAGE,
        GAME_MESSAGE,
        HIT_ATTEMPT,
        SHIPS,
        CHALLENGE,
        ACCEPT_CHALLENGE,
        JOIN_GAME,
        SPECTATING,
        SPECTATING_HIT,
        GAME_DATA,
        END_GAME,
        QUIT_ALL,
    }

    [Serializable]
    public class Packet
    {
        public PacketType packetType = PacketType.EMPTY;
    }

    [Serializable]
    public class NicknamePacket : Packet
    {
        public string nickname;

        public NicknamePacket(string nickname)
        {
            packetType = PacketType.NICKNAME;
            this.nickname = nickname;
        }
    }

    [Serializable]
    public class ChatMessagePacket : Packet
    {
        public string message;

        public ChatMessagePacket(string message)
        {
            packetType = PacketType.CHAT_MESSAGE;
            this.message = message;
        }
    }

    [Serializable]
    public class DirectedMessagePacket : Packet
    {
        public string message;

        //recipient of the message
        public string recipientName;

        public DirectedMessagePacket(string message, string recipientName)
        {
            packetType = PacketType.DIRECT_MESSAGE;
            this.recipientName = recipientName;
            this.message = message;
        }
    }

    [Serializable]
    public class HitAttemptPacket : Packet
    {
        //public label control index;
        public int index;
        public bool hit;
        public bool myTurn;

        //cIndex = index of the control, shipHit = was a shipHit, myTurn = was it your clients turn
        public HitAttemptPacket(int cIndex, bool shipHit, bool myTurn)
        {
            packetType = PacketType.HIT_ATTEMPT;
            index = cIndex;
            hit = shipHit;
            this.myTurn = myTurn;
        }
    }

    [Serializable]
    public class ShipsChosenPacket : Packet
    {
        public List<int> shipIndices;

        public ShipsChosenPacket(List<int> chosenShips)
        {
            packetType = PacketType.SHIPS;
            shipIndices = chosenShips;
        }
    }

    [Serializable]
    public class GameMessagePacket : Packet
    {
        public string message;
        public int gameID;

        public GameMessagePacket(String message)
        {
            packetType = PacketType.GAME_MESSAGE;
            this.message = message;
        }
    }

    //new
    [Serializable]
    public class ChallengePacket : Packet
    {
        public string chosenOpponent;
        public string message;

        public ChallengePacket(string chosenOpponent, string message)
        {
            packetType = PacketType.CHALLENGE;
            this.chosenOpponent = chosenOpponent;
            this.message = message;
        }
    }

    [Serializable]
    public class AcceptChallengePacket : Packet
    {
        public bool accept;
        public string challenger;

        public AcceptChallengePacket(bool accept, string challenger)
        {
            packetType = PacketType.ACCEPT_CHALLENGE;
            this.accept = accept;
            this.challenger = challenger;
        }
    }

    [Serializable]
    public class SpectatingPacket : Packet
    {
        public bool startSpectating;
        public string chosenClient;
        public string clientsOpponent;

        public SpectatingPacket(bool startSpectating, string chosenClient, string clientsOpponent)
        {
            packetType = PacketType.SPECTATING;
            this.startSpectating = startSpectating;
            this.chosenClient = chosenClient;
            this.clientsOpponent = clientsOpponent;
        }
    }

    [Serializable]
    public class SpectatorHitPacket : Packet
    {
        //public label control index;
        public int index;
        public bool hit;
        public bool player1Turn;

        //cIndex = index of the control, shipHit = was a shipHit, myTurn = was it your clients turn
        public SpectatorHitPacket(int cIndex, bool shipHit, bool player1Turn)
        {
            packetType = PacketType.SPECTATING_HIT;
            this.index = cIndex;
            this.hit = shipHit;
            this.player1Turn = player1Turn;
        }
    }

    [Serializable]
    public class GameDataPacket : Packet
    {
        public List<int> p1Ships;
        public List<int> p2Ships;
        public List<int> p1DeadShips;
        public List<int> p2DeadShips;

        public GameDataPacket (List<int> p1Ships, List<int> p2Ships, 
                        List<int> p1DeadShips, List<int> p2DeadShips)
        {
            packetType = PacketType.GAME_DATA;

            this.p1Ships = p1Ships;
            this.p2Ships = p2Ships;
            this.p1DeadShips = p1DeadShips;
            this.p2DeadShips = p2DeadShips;
        }
    }

    [Serializable]
    public class JoinGamePacket : Packet
    {
        public bool joinGame;

        public JoinGamePacket(bool joinGame)
        {
            packetType = PacketType.JOIN_GAME;
            this.joinGame = joinGame;
        }
    }

    [Serializable]
    public class EndGamePacket : Packet
    {
        public bool endGame;

        public EndGamePacket(bool endGame)
        {
            packetType = PacketType.END_GAME;
            this.endGame = endGame;
        }
    }

    [Serializable]
    public class QuitAllPacket : Packet
    {
        public bool quitAll;

        public QuitAllPacket(bool quitGame)
        {
            packetType = PacketType.QUIT_ALL;
            this.quitAll = quitGame;
        }
    }
}