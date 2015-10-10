using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Net;
using DCWavStreamer.Audio;
using System.Text.RegularExpressions;

namespace DCWavStreamer
{
    class DCServer
    {
        /// <summary>
        /// 
        /// Member variables
        /// 
        /// m_dataBuffer   : retrieves the inbound request from the connecting client
        /// m_clients      : list of connected sockets to serve
        /// m_serverSocket : reference to the socket that maintains a connection to the server via TCP
        /// 
        /// </summary>
        private static byte[] m_dataBuffer;
        private static List<Socket> m_clients;
        private static Socket m_serverSocket;

        private static string m_serverIP;

        /// <summary>
        /// 
        /// Constructor for a new DCServer, accepts a DCConfig structure as a parameters.
        /// The DCConfig structure dictates where the server should run, the port it should run on,
        /// the servers title and other information, such as the size of the input buffer.
        /// 
        /// The default value for an inbound buffer is 256 (default GET request len), however a larger
        /// size can be provided if you know you will be serving larger packets.
        /// 
        /// </summary>
        /// <param name="config">Configuration constants</param>
        public DCServer(DCConfig config)
        {
            // Ensure we can create a connection, if any of the fundamental components are missing
            // then we log an error to the console.
            if (config.SERVER_ADDR == null || config.SERVER_PORT <= 0 || config.INBOUND_BUFFER_SIZE == 0)
            {
                Console.WriteLine("Terminating app, configuration constants not set.");
                return;
            }
            if (config.SERVER_TITLE != null)
                Console.Title = config.SERVER_TITLE;

            // Perform any initialisation of member variables, dependent on values set in 
            // the configuration struct
            m_serverIP = config.SERVER_ADDR;
            m_clients = new List<Socket>();
            m_dataBuffer = new byte[config.INBOUND_BUFFER_SIZE];
            m_serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Attempt to initialise the server by binding the server to the socket, this will be our listener
            // for inbound request.  We also set a backlog of 5 pending connections here, meaning the 6th
            // connection will be rejected if the server queue is full, finally we tell the server to start
            // accepting connections and then perform the recursive server logic in the AcceptCallback func.
            Console.WriteLine("Initialising the server.");
            try
            {
                m_serverSocket.Bind(new IPEndPoint(IPAddress.Parse(config.SERVER_ADDR), config.SERVER_PORT));
                m_serverSocket.Listen(config.BACK_LOG_SIZE);
                m_serverSocket.BeginAccept(new AsyncCallback(AcceptCallback), null);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// 
        /// Defines a Callback to Accept a new connection to the server.  This works similar to how mutex locks
        /// work in multi threaded applications.  The server will acquire the lock by ending its accept state, 
        /// then it will add the new socket to the managed socket pool, start listening to that client for a request
        /// and then it will finally allow new sockets to connect, this will fire recursively and shall listen
        /// to all clients on each loop.
        /// 
        /// </summary>
        /// <param name="res">Pointer to the socket we are listening to</param>
        private static void AcceptCallback(IAsyncResult res)
        {
            try
            {
                var socket = m_serverSocket.EndAccept(res);
                m_clients.Add(socket);
                socket.BeginReceive(m_dataBuffer, 0, m_dataBuffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), socket);
                m_serverSocket.BeginAccept(new AsyncCallback(AcceptCallback), null);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// 
        /// Defines a callback to receive data from the connected client socket.  The pointer passed in the Accept method
        /// tells the server which socket it is listening on (this runs asynchronously as we listen to multiple sockets at once)
        /// we are then able to pull the request from that socket into the readbuffer, allowing us to see what type 
        /// of request is sent. This is normally an RTSP DESCRIBE request, from here we can determine what stream the
        /// listening socket should read from.
        /// 
        /// </summary>
        /// <param name="res"></param>
        private static void ReceiveCallback(IAsyncResult res)
        {
            try
            {
                // Get the pointer to our socket from the result, and then create a temporary buffer
                // to pull the information from 
                var socket = (Socket)res.AsyncState;
                int data = socket.EndReceive(res);
                var tmpBuffer = new byte[data];

                // Copy the request data into the temp buffer (this is written to in accept callback from the connecting socket)
                Array.Copy(m_dataBuffer, tmpBuffer, data);

                string request = Encoding.ASCII.GetString(tmpBuffer);
                Console.WriteLine("Request from client: " + request);

                // See what type of RTSP request we recieve on our end, DESCRIBE means that the client is asking for
                // the data at the specified resource i.e. rtsp://server.com/streams/streamX would respond with a PLAY request
                // with a chunk of data from that stream at the current position.
                var requestParams = request.Split(' ');
                var requestURI = requestParams[1];

                // Extract the stream from the URI request
                int pos = requestURI.LastIndexOf("/") + 1;
                var stream = requestURI.Substring(pos, requestURI.Length - pos);

                switch (requestParams[0])
                { 
                    case "DESCRIBE":
                        try
                        {
                            // Build the RTSP Response string
                            var body =     "v=0\r\n" +
                                           "o=- 575000 575000 IN IP4 " + m_serverIP + "\r\n" +
                                           "s=" + stream + "\r\n" +
                                           "i=<No author> <No copyright>\r\n" + 
                                           "c= IN IP4 0.0.0.0\r\n"+
                                           "t=0 0\r\n" +
                                           "a=SdpplinVersion:1610641560\r\n" +
                                           "a=StreamCount:integer;1\r\n" +
                                           "a=control:*\r\n" +
                                           "a=Flags:integer;1\r\n" +
                                           "a=HasParam:integer;0\r\n" +
                                           "a=LatencyMode:integer;0\r\n" + 
                                           "a=LiveStream:integer;1\r\n" +
                                           "a=mr:integer;0\r\n" +
                                           "a=nr:integer;0\r\n" +
                                           "a=sr:integer;0\r\n" + 
                                           "a=URL:string;\"Streams/" + stream + "\"\r\n" +
                                           "a=range:npt=0-\r\n" + 
                                           "m=audio 0 RTP/AVP 8" + // 49170 is the RTP transport port and 8 is A-Law audio
                                           "b=AS:90\r\n" +
                                           "b=TIAS:64000\r\n" +
                                           "b=RR:1280\r\n" +
                                           "b=RS:640\r\n" + 
                                           "a=maxprate:50.000000\r\n" +
                                           "a=control:streamid=1\r\n" +
                                           "a=range:npt=0-\r\n" +
                                           "a=length:npt=0\r\n" +
                                           "a=rtpmap:8 pcma/8000/1\r\n" +
                                           "a=fmtp:8" +
                                           "a=mimetype:string;\"audio/pcma\"\r\n" +
                                           "a=ASMRuleBook:string;\"marker=0, Avera**MSG 00053 TRUNCATED**\r\n" +
                                           "**MSG 0053 CONTINUATION #01**geBandwidth=64000, Priority=9, timestampdelivery=true;\"\r\n" +
                                           "a=3GPP-Adaptation-Support:1\r\n" +
                                           "a=Helix-Adaptation-Support:1\r\n" +
                                           "a=AvgBitRate:integer;64000\r\n" +
                                           "a=AvgPacketSize:integer;160\r\n" + 
                                           "a=BitsPerSample:integer;16\r\n" +
                                           "a=LiveStream:integer;1\r\n" + 
                                           "a=MaxBitRate:integer;64000\r\n" +
                                           "a=MaxPacketSize:integer;160\r\n" +
                                           "a=Preroll:integer;2000\r\n" +
                                           "a=StartTime:integer;0\r\n" +
                                           "a=OpaqueData:buffer;\"AAB2dwAGAAEAAB9AAAAfQAABABAAAA==\""; 

                            var header =   "RTSP/1.0 200 OK\r\n" +
                                           "Content-Length: " + body.Length + "\r\n" +
                                           "x-real-usestrackid:1\r\n" +
                                           "Content-Type: application/sdp\r\n" +
                                           "Vary: User-Agent, ClientID\r\n" +
                                           "Content-Base: " + requestURI + "\r\n" +
                                           "vsrc:" + m_serverIP + "/viewsource/template.html\r\n" +
                                           "Set-Cookie: " + Auth.getCookie() + "\r\n" +
                                           "Date: " + System.DateTime.Now + "\r\n" +
                                           "CSeq: 0\r\n";

                            var response = header + "\r\n" + body;
                            var byteArray = System.Text.Encoding.UTF8.GetBytes(response);
                            respondToClient(byteArray, socket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                        break;
                    case "SETUP":
                        try
                        {
                            // Build the RTSP Response string
                            var header =   "RTSP/1.0 200 OK\r\n" +
                                           "CSeq: 1\r\n" +
                                           "Date: " + System.DateTime.Now + "\r\n" +
                                           "Session: 4833-0;timeout=79\r\n" +
                                           "Transport: RTP/AVP;unicast;client_port=19588-19589;server_port=20900-20901\r\n" +
                                           "Reconnect: true\r\n" +
                                           "RDTFeatureLevel: 0\r\n" +
                                           "StatsMask: 8\r\n" +
                                           "RealChallenge1: 75fc4fa91054c00bdc3a5e8678f2f8aa\r\n" + 
                                           "\r\n";

                            var byteArray = System.Text.Encoding.UTF8.GetBytes(header);
                            respondToClient(byteArray, socket);
                        }
                        catch(Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                        break;
                    case "PLAY":
                        try 
                        {
                            Console.WriteLine("Fetching chunk from the requested stream...");
                            // Convert the WAV chunk to a byte array and send it to the current socket, once the data
                            // has been transmitted the socket will automatically end its sending state and start listening
                            // for more data, this is how we set up our continuous stream from client to server...
                            // Collect byte information from audio file
                            ALawWaveStream waveStream = new ALawWaveStream("holdmusic.wav");

                            // Build the RTSP Response string
                            var header =   "RTSP/1.0 200 OK\r\n" +
                                           "Content_length: 0\r\n" + 
                                           "Date: " + System.DateTime.Now + "\r\n" +
                                           "Session: 4833-0;timeout=79\r\n" +
                                           "RTCP-Interval: 250\r\n" + 
                                           "RTP-Info: url=rtsp://" + m_serverIP + ":554/Streams/" + stream + "/streamid=1;seq=0;rtptime=1296048584\r\n" +
                                           "CSeq: 2\r\n" +
                                           "\r\n";

                            var byteArray  = System.Text.Encoding.UTF8.GetBytes(header) ;
                            var audioArray = waveStream.getByteChunk(0, 3);

                            respondToClient(byteArray, socket);
                        }
                        catch(Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                        break;
                    case "TEARDOWN":
                        try 
                        {
                            // Build the RTP Response string
                            var response = "RTSP/1.0 200 OK\r\n" +
                                            "Date: " + System.DateTime.Now + "\r\n" +
                                            "Server: " + m_serverIP + "\r\n" +
                                            "CSeq: 3\r\n";

                            var byteChunk = System.Text.Encoding.UTF8.GetBytes(response);
                            socket.BeginSend(byteChunk, 0, byteChunk.Length, SocketFlags.None, new AsyncCallback(DisposeCallback), socket);
                        } 
                        catch(Exception e)
                        {
                            Console.WriteLine("The socket could not be disconnected as it does not exist. " + e.Message);
                        }
                        break;
                    default:
                        Console.WriteLine("Unsupported request type.");
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// 
        /// Send a message back to the client
        /// 
        /// </summary>
        /// <param name="data"></param>
        private static void respondToClient(byte[] byteChunk, Socket socket)
        {

                socket.BeginSend(byteChunk, 0, byteChunk.Length, SocketFlags.None, new AsyncCallback(SendCallback), socket);
                socket.BeginReceive(m_dataBuffer, 0, m_dataBuffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), socket);
            
        }

        /// <summary>
        /// 
        /// Defines a callback for when all other transport has finished from S->C and C->S, we then dispose of the socket from 
        /// pur pool of connected sockets.
        /// 
        /// </summary>
        ///// <param name="res"></param>
        private static void DisposeCallback(IAsyncResult res)
        {
            try
            {
                var socket = (Socket)res.AsyncState;
                socket.EndSend(res);

                m_clients.Remove(socket);
                socket.Dispose();
            }
            catch (Exception e)
            {
                Console.Write(e.Message);
            }
        }

        /// <summary>
        /// 
        /// Defines a callback for when a data is sent to a connected socket. This tells the server socket that the data in the buffer
        /// has been sent and that the socket can start recieving data again. This will allow us to consitently send chunks to the
        /// listening SIP application.
        /// 
        /// </summary>
        /// <param name="res"></param>
        private static void SendCallback(IAsyncResult res)
        {
            try
            {
                var socket = (Socket)res.AsyncState;
                socket.EndSend(res);
            }
            catch (Exception e)
            {
                Console.Write(e.Message);
            }
        }
    }
}