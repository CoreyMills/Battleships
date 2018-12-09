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
        HIT_ATTEMPT,
        SHIPS,
        GAME,
        JOIN_GAME,
        END_GAME,
        QUIT_GAME,
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

        public ChatMessagePacket(String message)
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

        public DirectedMessagePacket(String message, string recipientName)
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
        public List<int> shipIndicies;

        public ShipsChosenPacket(List<int> chosenShips)
        {
            packetType = PacketType.SHIPS;
            shipIndicies = chosenShips;
        }
    }

    [Serializable]
    public class GameMessagePacket : Packet
    {
        public string message;
        public int gameID;

        public GameMessagePacket(String message)
        {
            packetType = PacketType.GAME;
            this.message = message;
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
    public class QuitGamePacket : Packet
    {
        public bool quitGame;

        public QuitGamePacket(bool quitGame)
        {
            packetType = PacketType.QUIT_GAME;
            this.quitGame = quitGame;
        }
    }
}