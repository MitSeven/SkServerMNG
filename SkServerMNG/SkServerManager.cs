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
    [Serializable]
    public class SkServerManager
    {
        private static Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private const int BUFFER_SIZE = 1024 * 32000;
        private static readonly byte[] buffer = new byte[BUFFER_SIZE];
        public delegate void Changed(object T, ModeEventArgs e);
        public event Changed ChangeEvent;
        public bool IsSKCreate { get; private set; } = false;
        public string IpServer { get; private set; } = "127.0.0.1";
        public Hashtable ClientSockets { get; private set; } = new Hashtable();
        public object ExMessage { get; private set; }
        public object ClientRequest { get; private set; }
        public object ClientConnect { get; private set; }
        public object ClientExit { get; private set; }

        public bool SetupServer()
        {
            if (!serverSocket.Connected)
            {
                serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    serverSocket.Bind(new IPEndPoint(IPAddress.Any, 777));
                    serverSocket.Listen(0);
                    serverSocket.BeginAccept(AcceptCallback, null);
                    IpServer = GetIpServer();
                    IsSKCreate = true;
                    return true;
                }
                catch
                {
                    ExMessage = "Không thể tạo Server!";
                    ChangeEvent?.Invoke(ExMessage, new ModeEventArgs("ExMessage"));
                    return false;
                }
            }
            else
            {
                return true;
            }
        }
        private string GetIpServer()
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
        /// <summary>
        /// Close all connected client (we do not need to shutdown the server socket as its connections
        /// are already closed with the clients).
        /// </summary>
        public bool CloseAllSockets()
        {
            try
            {
                try
                {
                    foreach (DictionaryEntry socket in ClientSockets)
                    {
                        Socket sk = socket.Value as Socket;
                        sk.Send(SerializeData("exit"));
                        sk.Shutdown(SocketShutdown.Both);
                        sk.Close();
                    }
                }
                catch { }
                IsSKCreate = false;
                ClientSockets.Clear();
                serverSocket.Close();
            }
            catch
            {
                return false;
            }
            return true;
        }
        public bool RespondClientRq(object Object, string IpClient)
        {
            Socket clientsk = ClientSockets[IpClient] as Socket;
            try
            {
                clientsk.Send(SerializeData(Object));
                return true;
            }
            catch
            {
                return false;
            }
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
                ExMessage = "Mất kết nối!";
                ChangeEvent?.Invoke(ExMessage, new ModeEventArgs("ExMessage"));
                return;
            }
            ClientSockets.Remove("127.0.0.1");
            ClientSockets.Add("127.0.0.1", socket);
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
            string text = DeserializeData(recBuf).ToString();
            string[] arrText = text.Split(new char[] { '&' }, 2);
            CommandEx(arrText, current);//xử lý lệnh nhận được từ client
            try
            {
                current.BeginReceive(buffer, 0, BUFFER_SIZE, SocketFlags.None, ReceiveCallback, current);
            }
            catch
            {

            }
        }
        private void CommandEx(string[] command, Socket current)
        {
            switch (command.First())
            {
                case "ipclient":
                    {
                        Socket skUS = current;
                        ClientSockets.Remove(command[1]);
                        ClientSockets.Add(command[1], skUS);
                        ClientSockets.Remove("127.0.0.1");
                        ClientConnect = command[1];
                        ChangeEvent?.Invoke(ClientConnect, new ModeEventArgs("ClientConnect"));
                        break;
                    }
                case "exit":
                    {
                        Socket skUS = current;
                        try
                        {
                            skUS.Shutdown(SocketShutdown.Both);
                            skUS.Close();
                        }
                        catch { }
                        ClientSockets.Remove(command[1]);
                        ClientExit = command[1];
                        ChangeEvent?.Invoke(ClientExit, new ModeEventArgs("ClientExit"));
                        return;
                    }
                case "rcvrq":
                    {
                        Hashtable rqclient = new Hashtable();
                        foreach (DictionaryEntry ipclient in ClientSockets)
                        {
                            if (ipclient.Value == current)
                            {
                                rqclient.Remove(ipclient.Key);
                                rqclient.Add(ipclient.Key, command[1]);
                            }
                        }
                        byte[] data = SerializeData(RespondRequest(rqclient, command[1]));
                        current.Send(data);
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
        }
        private string RespondRequest(Hashtable Request, string Text)
        {
            string rsp = "Không xác định được yêu cầu!";
            try
            {
                ClientRequest = Request;
                ChangeEvent?.Invoke(ClientRequest, new ModeEventArgs("ClientRequest"));
                rsp = "Đã nhận được yêu cầu: " + Text;
            }
            catch { }
            return rsp;
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
                return "empty";
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
        private string _mode;
        public string Mode { get => _mode; set => _mode = value; }
        public ModeEventArgs(string mode) => Mode = mode;
    }
}
