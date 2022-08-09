using LiteNetLib;
using LiteNetLib.Utils;
using System.Net;


public class Room 
{
    public List<Player> players;
    public IPEndPoint hostInternalAddr;
    public IPEndPoint hostExternalAddr;

    public Room(Player host){

        players = new List<Player>();
        players.Add(host);

    }

}

public class Player
{

    public DateTime RefreshTime { get; private set; }
    public NetPeer netPeer { get; set; }
    public IPEndPoint InternalAddr;
    public IPEndPoint ExternalAddr;
    public void Refresh()
    {
        RefreshTime = DateTime.UtcNow;
    }

    public Player(NetPeer peer)
    {
        Refresh();
        netPeer = peer;
    }

}

public class Server {

    private Dictionary<int, Room> rooms;
    // Int is the room#, List is the clients. Client acting as host is index 0

    //private Dictionary<int, List<WaitPeer>> waitPeers;
    private List<NetPeer> clients;
    private NetManager net;
    private NetDataWriter writer;
    private EventBasedNetListener listener;
    private EventBasedNatPunchListener natListener;
    private const int ServerPort = 7777;
    private const string ConnectionKey = "test_key";
    private static readonly TimeSpan KickTime = new TimeSpan(0, 0, 6);
    private int currentRoomIndex;

    public static void Main(string[] args){

        new Server();

    }

    public Server(){

        rooms = new Dictionary<int, Room>();
        clients = new List<NetPeer>();

        listener = new EventBasedNetListener();

        listener.ConnectionRequestEvent += (request) => {
            OnConnectionRequest(request);
        };

        listener.PeerConnectedEvent += (peer) => {
            OnPeerConnected(peer);
        };
        
        listener.PeerDisconnectedEvent += (peer, info) => {
            OnPeerDisconnected(peer, info);
        };

        listener.NetworkReceiveEvent += (peer, reader, channel, method) => {
            OnMessageReceive(peer, reader, channel, method);
        };

        net = new NetManager(listener){
            NatPunchEnabled = true
        };

        writer = new NetDataWriter();

        natListener = new EventBasedNatPunchListener();

        natListener.NatIntroductionRequest += (localEndPoint, remoteEndPoint, token) => {
            OnNatIntroductionRequest(localEndPoint, remoteEndPoint, token);
        };

        net.Start(ServerPort);
        net.NatPunchModule.Init(natListener);

        Console.WriteLine("Server started.");

        while(true){
            net.PollEvents();
            net.NatPunchModule.PollEvents();
        }

    }

    // Networking Events /////////////////////////////////////

    public void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token){

        string[] tokenSections = token.Split(":");

        string identity = tokenSections[0];
        string UUID = tokenSections[1];
        int roomNumber = int.Parse(tokenSections[2]);

        if(rooms.ContainsKey(roomNumber)){

            if(identity.Equals("HOST")){

                // Create a room on the server with the host's details
                Room room = rooms[roomNumber];
                room.hostExternalAddr = remoteEndPoint;
                room.hostInternalAddr = localEndPoint;

                // Send host a confirmation to create the game
                SendMessage(room.players[0].netPeer, "$CN-INATCONF", DeliveryMethod.ReliableUnordered);

            } else
            if(identity.Equals("CLIENT")){

                Room room = rooms[roomNumber];

                // Introduce host of room and client
                net.NatPunchModule.NatIntroduce(room.hostInternalAddr, room.hostExternalAddr, localEndPoint, remoteEndPoint, token);

            }
            
        }

    }

    public void OnConnectionRequest(ConnectionRequest req){

        string connectionData = req.Data.GetString();
        string[] connectionDataSections = connectionData.Split(":");

        string identity = connectionDataSections[0];

        if(identity.Equals("HOST")){

            // Accept peer
            NetPeer host = req.Accept();
            Player player = new Player(host);

            // Get room assignment from server
            int roomNumber = AssignRoom();

            // Create room
            Room room = new Room(player);
            rooms.Add(roomNumber, room);
            
            // Tell host to send a nat introduction for the room
            SendMessage(host, PacketBuilder.NatIntroductionRequest(roomNumber), DeliveryMethod.ReliableUnordered);

        } else
        if(identity.Equals("CLIENT")){

            string clientUUID = connectionDataSections[1];
            int roomNumber = int.Parse(connectionDataSections[2]);

            NetPeer client = req.Accept();
            clients.Add(client);

            if(rooms.ContainsKey(roomNumber)){
                
                // Room exists, tell client to send a NAT introduction request
                SendMessage(client, PacketBuilder.NatIntroductionRequest(roomNumber), DeliveryMethod.ReliableUnordered);

            } else {

                // TODO: Room does not exist, send error back to the client
                

            }

        }

    }

    public void OnPeerConnected(NetPeer peer){
        Console.WriteLine("Client connected.");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info){
        
        Console.WriteLine("Client disconnected");

        Room disconnectedRoom = null;
        Player disconnectedPlayer = null;

        // Find room and player
        foreach(Room room in rooms.Values){
            foreach(Player player in room.players){
                if(player.netPeer == peer){
                    disconnectedRoom = room;
                    disconnectedPlayer = player;
                    break;
                }
            }
        }

        if(disconnectedRoom != null && disconnectedPlayer != null){

            if(disconnectedRoom.players[0] == disconnectedPlayer){

                // Disconnected player was the host, destroy room
                DestroyRoom(disconnectedRoom);

            } else {

                // Remove player from the room
                disconnectedRoom.players.Remove(disconnectedPlayer);

            }

        }

    }

    public void OnMessageReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method){



    }

    //////////////////////////////////////////////////////

    // SYNTAX FOR MESSAGES SENT OVER NETWORK /////////////
    /*

    Separate all sections with a colon ':'
    Separate all sub-sections with a comma ','

    $CN = Connection Data
        - INAT = Send NAT introduction
    
    $WD : World Data

    */
    //////////////////////////////////////////////////////

    public void SendMessage(NetPeer peer, string message, DeliveryMethod method){

        writer.Reset();
        writer.Put(message);
        peer.Send(writer, method);

    }

    public int AssignRoom(){
        return 5;//new Random().Next(1, 100000);//currentRoomIndex++;
    }

    public void DestroyRoom(Room room){

        int roomToDestroy = -1;

        foreach(int roomNumber in rooms.Keys){
            if(rooms[roomNumber] == room){
                // Found room
                roomToDestroy = roomNumber;
                break;
            }
        }

        if(roomToDestroy != -1){
            rooms.Remove(roomToDestroy);
            Console.WriteLine("Room disbanded: " + roomToDestroy);
        }

    }


}
