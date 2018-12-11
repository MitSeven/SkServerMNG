using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;

namespace SkServerMNG
{
    public class SkServerManager
    {
        private static Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private const int BUFFER_SIZE = 1024 * 32000;
        private static readonly byte[] buffer = new byte[BUFFER_SIZE];
        private int rdconnect = 100;
        public delegate void Changed(object T, ModeEventArgs e);
        public event Changed ReceivedEvent;
        public string IpServer
        {
            get
            {
                string IP_current;
                try
                {
                    IP_current = GetLocalIPv4(NetworkInterfaceType.Ethernet);
                    if (string.IsNullOrEmpty(IP_current))
                    {
                        IP_current = GetLocalIPv4(NetworkInterfaceType.Wireless80211);
                    }
                }
                catch
                {
                    IP_current = "127.0.0.1";
                }
                return IP_current;
            }
            private set { }
        }
        public bool IsSKCreate { get; private set; } = false;
        public Hashtable ClientSockets { get; private set; } = new Hashtable();
        private object exMessage;
        private bool AbortSend = false;
        public bool CreateServer()
        {
            if (!serverSocket.Connected)
            {
                serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    serverSocket.Bind(new IPEndPoint(IPAddress.Any, 777));
                    serverSocket.Listen(0);
                    serverSocket.BeginAccept(AcceptCallback, null);
                    IsSKCreate = true;
                    return true;
                }
                catch
                {
                    exMessage = "Cannot create Server!";
                    ReceivedEvent?.Invoke(exMessage, new ModeEventArgs((int)ModeEvent.SocketMessage));
                    return false;
                }
            }
            else
            {
                return true;
            }
        }
        public bool CloseAll()
        {
            try
            {
                AbortSend = true;
                try
                {
                    foreach (DictionaryEntry socket in ClientSockets)
                    {
                        Socket sk = socket.Value as Socket;
                        SendtoClient(new DictionaryEntry(DataType.Exit, ""), sk);
                        sk.Shutdown(SocketShutdown.Both);
                        sk.Close();
                    }
                }
                catch { }
                AbortSend = false;
                IsSKCreate = false;
                ClientSockets.Clear();
                serverSocket.Close();
                rdconnect = 100;
                exMessage = "Server closed!";
                ReceivedEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.SocketMessage));
            }
            catch
            {
                return false;
            }
            return true;
        }
        public bool Send(object Object, string IpClient)
        {
            try
            {
                Socket clientsk = ClientSockets[IpClient] as Socket;
                SendtoClient(new DictionaryEntry(DataType.Object, Object), clientsk);
                return true;
            }
            catch
            {
                return false;
            }
        }
        private bool SendtoClient(DictionaryEntry dictionaryEntry, Socket socket)
        {
            var dtsend = new DictionaryEntry
            {
                Key = (int)dictionaryEntry.Key,
                Value = dictionaryEntry.Value
            };
            try
            {
                byte[] buffer = SerializeData(dtsend);
                socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
            }
            catch
            {
                IsSKCreate = false;
                if (!AbortSend)
                {
                    exMessage = "Lost connect from client!";
                    ReceivedEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.ClientExit));
                }
                socket.Close();
                return false;
            }
            return true;
        }
        private void AcceptCallback(IAsyncResult AR)
        {
            Socket socket;
            try
            {
                socket = serverSocket.EndAccept(AR);
            }
            catch (ObjectDisposedException)
            {
                if (!AbortSend)
                {
                    exMessage = "Lost connect!";
                    ReceivedEvent?.Invoke(exMessage, new ModeEventArgs((int)ModeEvent.SocketMessage));
                }
                return;
            }
            ClientSockets.Remove("user" + rdconnect.ToString());
            ClientSockets.Add("user" + rdconnect.ToString(), socket);
            rdconnect++;
            socket.BeginReceive(buffer, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCallback, socket);
            serverSocket.BeginAccept(AcceptCallback, null);
        }
        private void ReceiveCallback(IAsyncResult AR)
        {
            Socket current = (Socket)AR.AsyncState;
            int received;
            try
            {
                if (current.Connected)
                {
                    received = current.EndReceive(AR);
                }
                else
                {
                    received = 0;
                    Array.Clear(buffer, 0, BUFFER_SIZE);
                }
            }
            catch (SocketException)
            {
                current.Close();
                return;
            }
            byte[] recBuf = new byte[received];
            Array.Copy(buffer, recBuf, received);
            object text = "";
            try
            {
                text = DeserializeData(recBuf);
            }
            catch
            {
                text = System.Text.Encoding.ASCII.GetString(recBuf);
            }
            CommandEx(text, current);//xử lý lệnh nhận được từ client
            try
            {
                current.BeginReceive(buffer, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCallback, current);
            }
            catch
            {

            }
        }
        private void CommandEx(object text, Socket current)
        {
            try
            {
                var cmd = (DictionaryEntry)text;
                var command = (DataType)cmd.Key;
                switch (command)
                {
                    case DataType.IpClient:
                        {
                            int lastconn = rdconnect - 1;
                            Socket skUS = current;
                            ClientSockets.Remove(cmd.Value.ToString());
                            ClientSockets.Add(cmd.Value.ToString(), skUS);
                            ClientSockets.Remove("user" + lastconn.ToString());
                            exMessage = cmd.Value;
                            ReceivedEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.ClientConnect));
                            break;
                        }
                    case DataType.Exit:
                        {
                            Socket skUS = current;
                            try
                            {
                                skUS.Shutdown(SocketShutdown.Both);
                                skUS.Close();
                            }
                            catch { }
                            ClientSockets.Remove(cmd.Value.ToString());
                            exMessage = cmd.Value;
                            ReceivedEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.ClientExit));
                            return;
                        }
                    case DataType.Object:
                        {
                            DictionaryEntry rqclient = new DictionaryEntry();
                            foreach (DictionaryEntry ipclient in ClientSockets)
                            {
                                if (ipclient.Value == current)
                                {
                                    rqclient.Key = ipclient.Key;
                                    rqclient.Value = cmd.Value;
                                }
                            }

                            exMessage = rqclient;
                            ReceivedEvent?.Invoke(exMessage, new ModeEventArgs(ModeEvent.ClientRequest));
                            SendtoClient(new DictionaryEntry(DataType.Received, cmd.Value), current);
                            break;
                        }
                    default:
                        {
                            break;
                        }
                }
            }
            catch
            {
                try
                {
                    byte[] data = System.Text.Encoding.ASCII.GetBytes("Request received: " + text.ToString());
                    current.Send(data);
                }
                catch { }
            }

        }
        /// <summary>
        /// Nén đối tượng thành mảng byte[]
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        private byte[] SerializeData(Object o)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    BinaryFormatter bf1 = new BinaryFormatter();
                    bf1.Serialize(ms, o);
                    return ms.ToArray();
                }
            }
            catch
            {
                return new byte[0];
            }
        }

        /// <summary>
        /// Giải nén mảng byte[] thành đối tượng object
        /// </summary>
        /// <param name="theByteArray"></param>
        /// <returns></returns>
        private object DeserializeData(byte[] theByteArray)
        {
            if ((theByteArray.Length == 0))
            {
                return null;
            }
            else
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream(theByteArray))
                    {
                        BinaryFormatter bf1 = new BinaryFormatter();
                        ms.Position = 0;
                        return bf1.Deserialize(ms);
                    }
                }
                catch
                {
                    return null;
                }
            }
        }
        /// <summary>
        /// Lấy ra IP V4 của card mạng đang dùng
        /// </summary>
        /// <param name="_type"></param>
        /// <returns></returns>
        private string GetLocalIPv4(NetworkInterfaceType _type)
        {
            string output = "";
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.NetworkInterfaceType == _type && item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            output = ip.Address.ToString();
                        }
                    }
                }
            }
            return output;
        }
    }

    public class ModeEventArgs
    {
        private ModeEvent _mode;
        public ModeEvent ModeEvent { get => _mode; set => _mode = value; }
        public ModeEventArgs(ModeEvent mode) => ModeEvent = mode;
    }
    public enum ModeEvent
    {
        SocketMessage,
        ClientConnect,
        ClientExit,
        ClientRequest,
    }
    public enum DataType
    {
        IpClient,
        Exit,
        Object,
        Received,
    }
}
